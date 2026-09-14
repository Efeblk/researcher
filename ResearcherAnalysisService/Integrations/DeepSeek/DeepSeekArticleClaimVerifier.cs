using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using AcademicCollector.Analysis.Contracts;
using ResearcherAnalysisService.Analysis;

namespace ResearcherAnalysisService.Integrations.DeepSeek;

public sealed class DeepSeekArticleClaimVerifier(DeepSeekArticleClient client, int maxOutputTokens)
    : IArticleClaimVerifier
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async Task<GeneratedVerificationBatch> VerifyAsync(string language,
        IReadOnlyList<GeneratedArticleClaim> claims, IReadOnlyList<ArticleSourceSpan> sourceSpans,
        CancellationToken cancellationToken)
    {
        string input = ArticleVerificationInput.CreateClaims(language, claims, sourceSpans);
        DeepSeekArticleResult result = await client.GenerateAsync(
            ArticleVerificationPrompt.Instructions, input, maxOutputTokens, cancellationToken);
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
        if (verdicts is null || verdicts.Count != claims.Count || verdicts.Any(value => value is null) ||
            verdicts.Select(value => value.ClaimId).Distinct(StringComparer.Ordinal).Count() != verdicts.Count ||
            verdicts.Any(value => !claims.Any(claim => claim.ClaimId == value.ClaimId) ||
                value.Verdict is not ("supported" or "unsupported" or "uncertain") ||
                string.IsNullOrWhiteSpace(value.Reason) || value.Reason.Length > 500))
            throw new InvalidAnalysisException(AnalysisFailure.InvalidEvidence);
    }

    private sealed record VerificationEnvelope(IReadOnlyList<GeneratedClaimVerdict> Verdicts);
}
