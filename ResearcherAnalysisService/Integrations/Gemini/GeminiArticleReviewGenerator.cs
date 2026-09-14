using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using AcademicCollector.Analysis.Contracts;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Configuration;

namespace ResearcherAnalysisService.Integrations.Gemini;

public sealed class GeminiArticleReviewGenerator(GeminiArticleClient client, IOptions<AiOptions> options)
    : IArticleReviewRecoveryGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async Task<GeneratedArticleReviewPass> GenerateAsync(
        string role,
        string language,
        string sourceKind,
        IReadOnlyList<ArticleSourceSpan> sourceSpans,
        CancellationToken cancellationToken)
        => await GenerateAsync(role, language, sourceKind, sourceSpans,
            options.Value.ArticleGenerationThinkingLevel, cancellationToken);

    public async Task<GeneratedArticleReviewPass> GenerateAsync(
        string role,
        string language,
        string sourceKind,
        IReadOnlyList<ArticleSourceSpan> sourceSpans,
        string thinkingLevel,
        CancellationToken cancellationToken)
    {
        string input = CreateInput(role, language, sourceKind, sourceSpans);
        GeminiArticleResult result = await client.GenerateAsync(options.Value.ArticleModel,
            ArticleReviewPrompt.InstructionsForRole(role), input,
            ArticleReviewPrompt.CreateSchema(role), options.Value.ArticleMaxOutputTokens,
            thinkingLevel, cancellationToken);
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

    internal static string CreateInput(string role, string language, string sourceKind,
        IReadOnlyList<ArticleSourceSpan> sourceSpans)
    {
        var sources = sourceSpans.Select(span => new { span.SourceId, span.PageNumber, span.Text });
        return JsonSerializer.Serialize(new { role, language, sourceKind, sources }, JsonOptions);
    }

    private sealed record ReviewEnvelope(string Role, IReadOnlyList<GeneratedArticleReviewFinding> Findings);
}
