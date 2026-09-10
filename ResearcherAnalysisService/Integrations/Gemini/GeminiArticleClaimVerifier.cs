using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using AcademicCollector.Analysis.Contracts;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Configuration;

namespace ResearcherAnalysisService.Integrations.Gemini;

public sealed class GeminiArticleClaimVerifier(GeminiArticleClient client, IOptions<AiOptions> options)
    : IArticleClaimVerifier
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async Task<GeneratedVerificationBatch> VerifyAsync(string language, IReadOnlyList<GeneratedArticleClaim> claims,
        IReadOnlyList<ArticleSourceSpan> sourceSpans, CancellationToken cancellationToken)
    {
        HashSet<string> cited = claims.SelectMany(x => x.SourceIds).ToHashSet(StringComparer.Ordinal);
        HashSet<int> indexes = [];
        for (int i = 0; i < sourceSpans.Count; i++)
            if (cited.Contains(sourceSpans[i].SourceId))
                for (int context = Math.Max(0, i - 1); context <= Math.Min(sourceSpans.Count - 1, i + 1); context++)
                    indexes.Add(context);
        var sources = indexes.Order().Select(i => new { sourceSpans[i].SourceId, sourceSpans[i].PageNumber,
            sourceSpans[i].Text, citedEvidence = cited.Contains(sourceSpans[i].SourceId) });
        string input = JsonSerializer.Serialize(new { language, claims, sources }, JsonOptions);
        string model = string.IsNullOrWhiteSpace(options.Value.ArticleVerifierModel)
            ? options.Value.ArticleModel : options.Value.ArticleVerifierModel;
        GeminiArticleResult result = await client.GenerateAsync(model, ArticleVerificationPrompt.Instructions, input,
            ArticleVerificationPrompt.CreateSchema(claims.Select(x => x.ClaimId)),
            options.Value.ArticleVerifierMaxOutputTokens, cancellationToken);
        try
        {
            VerificationEnvelope envelope = JsonSerializer.Deserialize<VerificationEnvelope>(result.Json, JsonOptions)
                ?? throw new InvalidAnalysisException(AnalysisFailure.InvalidJson);
            Validate(claims, envelope.Verdicts);
            return new(envelope.Verdicts, result.Model, ArticleVerificationPrompt.Version);
        }
        catch (JsonException)
        {
            throw new InvalidAnalysisException(AnalysisFailure.InvalidJson);
        }
    }

    private static void Validate(IReadOnlyList<GeneratedArticleClaim> claims,
        IReadOnlyList<GeneratedClaimVerdict>? verdicts)
    {
        if (verdicts is null || verdicts.Count != claims.Count || verdicts.Any(x => x is null) ||
            verdicts.Select(x => x.ClaimId).Distinct(StringComparer.Ordinal).Count() != verdicts.Count ||
            verdicts.Any(x => !claims.Any(c => c.ClaimId == x.ClaimId) ||
                x.Verdict is not ("supported" or "unsupported" or "uncertain") ||
                string.IsNullOrWhiteSpace(x.Reason) || x.Reason.Length > 500))
            throw new InvalidAnalysisException(AnalysisFailure.InvalidEvidence);
    }

    private sealed record VerificationEnvelope(IReadOnlyList<GeneratedClaimVerdict> Verdicts);
}
