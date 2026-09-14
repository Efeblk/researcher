using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using AcademicCollector.Analysis.Contracts;
using ResearcherAnalysisService.Analysis;

namespace ResearcherAnalysisService.Integrations.DeepSeek;

public sealed class DeepSeekArticleReviewGenerator(DeepSeekArticleClient client, int maxOutputTokens)
    : IArticleReviewGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async Task<GeneratedArticleReviewPass> GenerateAsync(string role, string language,
        string sourceKind, IReadOnlyList<ArticleSourceSpan> sourceSpans, CancellationToken cancellationToken)
    {
        var sources = sourceSpans.Select(span => new { span.SourceId, span.PageNumber, span.Text });
        string input = JsonSerializer.Serialize(new { role, language, sourceKind, sources }, JsonOptions);
        DeepSeekArticleResult result = await client.GenerateAsync(
            ArticleReviewPrompt.InstructionsForRole(role), input, maxOutputTokens, cancellationToken);
        try
        {
            ReviewEnvelope envelope = JsonSerializer.Deserialize<ReviewEnvelope>(result.Json, JsonOptions)
                ?? throw new InvalidAnalysisException(AnalysisFailure.InvalidJson);
            return new(envelope.Role, envelope.Findings, result.Model, ArticleReviewPrompt.Version);
        }
        catch (JsonException)
        {
            throw new InvalidAnalysisException(AnalysisFailure.InvalidJson);
        }
    }

    private sealed record ReviewEnvelope(string Role, IReadOnlyList<GeneratedArticleReviewFinding> Findings);
}
