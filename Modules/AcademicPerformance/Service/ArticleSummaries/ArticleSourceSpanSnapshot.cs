using System.Text.Json.Serialization;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;

public sealed class ArticleSourceSpanSnapshot
{
    public long Id { get; set; }
    public long ArticleSourceSnapshotId { get; set; }

    [JsonIgnore]
    public ArticleSourceSnapshot? ArticleSourceSnapshot { get; set; } = null;

    public string SourceId { get; set; } = string.Empty;
    public int Ordinal { get; set; }
    public int? PageNumber { get; set; }
    public int StartOffset { get; set; }
    public int EndOffset { get; set; }
    public string Text { get; set; } = string.Empty;
}
