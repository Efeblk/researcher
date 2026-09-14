using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using AcademicCollector.Analysis.Contracts;
using ResearcherAnalysisService.Analysis;

namespace ResearcherAnalysisService.Integrations.DeepSeek;

public sealed class DeepSeekArticleReviewVerifier(DeepSeekArticleClient client, int maxOutputTokens)
    : IArticleReviewVerifier
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async Task<GeneratedArticleReviewVerification> VerifyAsync(string role, string language,
        IReadOnlyList<GeneratedArticleReviewFinding> findings, IReadOnlyList<ArticleSourceSpan> sourceSpans,
        CancellationToken cancellationToken)
    {
        string input = ArticleVerificationInput.CreateFindings(role, language, findings, sourceSpans);
        DeepSeekArticleResult result = await client.GenerateAsync(
            ArticleReviewVerificationPrompt.Instructions, input, maxOutputTokens, cancellationToken);
        try
        {
            VerificationEnvelope envelope = JsonSerializer.Deserialize<VerificationEnvelope>(result.Json, JsonOptions)
                ?? throw new InvalidAnalysisException(AnalysisFailure.InvalidJson);
            return new(envelope.Verdicts, result.Model, ArticleReviewVerificationPrompt.Version);
        }
        catch (JsonException)
        {
            throw new InvalidAnalysisException(AnalysisFailure.InvalidJson);
        }
    }

    private sealed record VerificationEnvelope(IReadOnlyList<GeneratedArticleReviewVerdict> Verdicts);
}
