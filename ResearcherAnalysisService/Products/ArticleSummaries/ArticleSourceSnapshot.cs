using System.Text.Json.Serialization;

namespace ResearcherAnalysisService.Products.ArticleSummaries;

public sealed class ArticleSourceSnapshot
{
    public long Id { get; set; }
    public int CanonicalWorkId { get; set; }

    public string ExtractedTextHash { get; set; } = string.Empty;
    public string SourceKind { get; set; } = string.Empty;
    public string ExtractionVersion { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public List<ArticleSourcePageSnapshot> Pages { get; set; } = [];
    public List<ArticleSourceSpanSnapshot> Spans { get; set; } = [];
}
