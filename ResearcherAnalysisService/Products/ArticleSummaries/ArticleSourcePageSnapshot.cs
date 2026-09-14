using System.Text.Json.Serialization;

namespace ResearcherAnalysisService.Products.ArticleSummaries;

public sealed class ArticleSourcePageSnapshot
{
    public long Id { get; set; }
    public long ArticleSourceSnapshotId { get; set; }

    [JsonIgnore]
    public ArticleSourceSnapshot? ArticleSourceSnapshot { get; set; } = null;

    public int Ordinal { get; set; }
    public int? PageNumber { get; set; }
    public string Text { get; set; } = string.Empty;
}
