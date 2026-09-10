using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using AcademicCollector.Analysis.Contracts;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Configuration;

namespace ResearcherAnalysisService.Integrations.Gemini;

public sealed class GeminiArticleSummaryGenerator(GeminiArticleClient client, IOptions<AiOptions> options)
    : IArticleSummaryGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async Task<GeneratedArticleChunk> GenerateAsync(string language, string sourceKind,
        IReadOnlyList<ArticleSourceSpan> sourceSpans, CancellationToken cancellationToken)
    {
        var sources = sourceSpans.Select(x => new { x.SourceId, x.PageNumber, x.Text });
        string input = JsonSerializer.Serialize(new { language, sourceKind, sources }, JsonOptions);
        GeminiArticleResult result = await client.GenerateAsync(options.Value.ArticleModel,
            ArticleSummaryPrompt.Instructions, input,
            ArticleSummaryPrompt.CreateSchema(sourceSpans.Where(x => !string.IsNullOrWhiteSpace(x.Text))
                .Select(x => x.SourceId)), options.Value.ArticleMaxOutputTokens, cancellationToken);
        try
        {
            GeneratedArticleSections sections = JsonSerializer.Deserialize<GeneratedArticleSections>(result.Json, JsonOptions)
                ?? throw new InvalidAnalysisException(AnalysisFailure.InvalidJson);
            Validate(sections, sourceSpans);
            sections = Normalize(sections);
            return new(sections, result.Model, ArticleSummaryPrompt.Version);
        }
        catch (JsonException)
        {
            throw new InvalidAnalysisException(AnalysisFailure.InvalidJson);
        }
    }

    private static GeneratedArticleSections Normalize(GeneratedArticleSections sections)
    {
        IReadOnlyList<GeneratedArticleClaim> Claims(IReadOnlyList<GeneratedArticleClaim> claims) =>
            claims.Select(x => x with { SourceIds = x.SourceIds.Distinct(StringComparer.Ordinal).ToList() }).ToList();
        return new(Claims(sections.Purpose), Claims(sections.Methods), Claims(sections.Data),
            Claims(sections.Findings), Claims(sections.Limitations));
    }

    private static void Validate(GeneratedArticleSections sections, IReadOnlyList<ArticleSourceSpan> spans)
    {
        IReadOnlyList<GeneratedArticleClaim>?[] groups =
            [sections.Purpose, sections.Methods, sections.Data, sections.Findings, sections.Limitations];
        if (groups.Any(x => x is null || x.Count > 3)) throw new InvalidAnalysisException();
        List<GeneratedArticleClaim> claims = groups.SelectMany(x => x!).ToList();
        if (claims.Any(x => x is null || string.IsNullOrWhiteSpace(x.ClaimId) || string.IsNullOrWhiteSpace(x.Text) ||
            x.Text.Length > 1200 || x.SourceIds is null || x.SourceIds.Count is 0 or > 2 ||
            x.SourceIds.Any(id => !spans.Any(span => span.SourceId == id && !string.IsNullOrWhiteSpace(span.Text)))) ||
            claims.Select(x => x.ClaimId).Distinct(StringComparer.Ordinal).Count() != claims.Count)
            throw new InvalidAnalysisException(AnalysisFailure.InvalidEvidence);
    }
}
