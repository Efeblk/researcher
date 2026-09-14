using System.Text.Json.Serialization;
using ResearcherAnalysisService.SourceData.Works;

namespace ResearcherAnalysisService.Products.ArticleSummaries;

public sealed class ArticleSourceSnapshot
{
    public long Id { get; set; }
    public int CanonicalWorkId { get; set; }

    [JsonIgnore]
    public CanonicalWork? CanonicalWork { get; set; } = null;

    public string ExtractedTextHash { get; set; } = string.Empty;
    public string SourceKind { get; set; } = string.Empty;
    public string ExtractionVersion { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public List<ArticleSourcePageSnapshot> Pages { get; set; } = [];
    public List<ArticleSourceSpanSnapshot> Spans { get; set; } = [];
}
