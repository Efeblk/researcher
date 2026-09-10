namespace AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;

public sealed class SavedArticleSummary
{
    public long Id { get; set; }
    public int? AcademicWorkId { get; set; }
    public int OriginalAcademicWorkId { get; set; }
    public string PersonelId { get; set; } = string.Empty;
    public DateTimeOffset SavedAt { get; set; }
    public string? SourceUrl { get; set; }
    public string SourceHash { get; set; } = string.Empty;
    public string SourceKind { get; set; } = string.Empty;
    public string ExtractionVersion { get; set; } = string.Empty;
    public string SnapshotJson { get; set; } = string.Empty;
    public string ReportJson { get; set; } = string.Empty;
}
