using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using AcademicCollector.Analysis.Contracts;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Configuration;

namespace ResearcherAnalysisService.Integrations.Gemini;

public sealed class GeminiArticleReviewVerifier(GeminiArticleClient client, IOptions<AiOptions> options)
    : IArticleReviewVerifier
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async Task<GeneratedArticleReviewVerification> VerifyAsync(
        string role,
        string language,
        IReadOnlyList<GeneratedArticleReviewFinding> findings,
        IReadOnlyList<ArticleSourceSpan> sourceSpans,
        CancellationToken cancellationToken)
    {
        string input = CreateInput(role, language, findings, sourceSpans);
        string model = string.IsNullOrWhiteSpace(options.Value.ArticleVerifierModel)
            ? options.Value.ArticleModel : options.Value.ArticleVerifierModel;
        GeminiArticleResult result = await client.GenerateAsync(model,
            ArticleReviewVerificationPrompt.Instructions, input,
            ArticleReviewVerificationPrompt.CreateSchema(findings.Select(finding => finding.FindingId)),
            options.Value.ArticleVerifierMaxOutputTokens, options.Value.ArticleVerifierThinkingLevel,
            cancellationToken);
        return ParseResult(result, ArticleReviewVerificationPrompt.Version);
    }

    internal static string CreateInput(string role, string language,
        IReadOnlyList<GeneratedArticleReviewFinding> findings,
        IReadOnlyList<ArticleSourceSpan> sourceSpans) =>
        ArticleVerificationInput.CreateFindings(role, language, findings, sourceSpans);

    internal static GeneratedArticleReviewVerification ParseResult(
        GeminiArticleResult result, string promptVersion)
    {
        try
        {
            VerificationEnvelope envelope = JsonSerializer.Deserialize<VerificationEnvelope>(result.Json, JsonOptions)
                ?? throw new InvalidAnalysisException(AnalysisFailure.InvalidJson);
            return new(envelope.Verdicts, result.Model, promptVersion);
        }
        catch (JsonException)
        {
            throw new InvalidAnalysisException(AnalysisFailure.InvalidJson);
        }
    }

    private sealed record VerificationEnvelope(IReadOnlyList<GeneratedArticleReviewVerdict> Verdicts);
}
