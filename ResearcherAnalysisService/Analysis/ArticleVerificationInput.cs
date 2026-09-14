using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using AcademicCollector.Analysis.Contracts;

namespace ResearcherAnalysisService.Analysis;

internal static class ArticleVerificationInput
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    public static string CreateClaims(string language, IReadOnlyList<GeneratedArticleClaim> claims,
        IReadOnlyList<ArticleSourceSpan> sourceSpans)
    {
        var items = claims.Select(claim => new
        {
            claim,
            sources = SourcesFor(claim.SourceIds, sourceSpans)
        });
        return JsonSerializer.Serialize(new { language, items }, JsonOptions);
    }

    public static string CreateFindings(string role, string language,
        IReadOnlyList<GeneratedArticleReviewFinding> findings,
        IReadOnlyList<ArticleSourceSpan> sourceSpans)
    {
        var items = findings.Select(finding => new
        {
            finding,
            sources = SourcesFor(finding.SourceIds, sourceSpans)
        });
        return JsonSerializer.Serialize(new { role, language, items }, JsonOptions);
    }

    private static object SourcesFor(IReadOnlyList<string> sourceIds,
        IReadOnlyList<ArticleSourceSpan> sourceSpans)
    {
        HashSet<string> cited = sourceIds.ToHashSet(StringComparer.Ordinal);
        return sourceSpans.Where(span => cited.Contains(span.SourceId)).Select(span => new
        {
            span.SourceId,
            span.PageNumber,
            span.Text
        }).ToList();
    }
}
