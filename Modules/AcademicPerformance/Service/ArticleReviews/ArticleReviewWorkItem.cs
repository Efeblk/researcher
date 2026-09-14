using System.Text.Json.Serialization;
using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.ArticleReviews;

public sealed class ArticleReviewWorkItem
{
    public long Id { get; set; }
    public string WorkKey { get; set; } = string.Empty;
    public int CanonicalWorkId { get; set; }
    [JsonIgnore] public CanonicalWork? CanonicalWork { get; set; } = null;
    public long BaseAnalysisRunId { get; set; }
    [JsonIgnore] public CanonicalArticleAnalysisRun? BaseAnalysisRun { get; set; } = null;
    public long ArticleSourceSnapshotId { get; set; }
    [JsonIgnore] public ArticleSourceSnapshot? ArticleSourceSnapshot { get; set; } = null;
    public string Language { get; set; } = string.Empty;
    public string PolicyVersion { get; set; } = string.Empty;
    public string SettingsFingerprint { get; set; } = string.Empty;
    public Guid GenerationNonce { get; set; }
    public string Status { get; set; } = "Pending";
    public int MaximumCalls { get; set; }
    public decimal MaximumSpendUsd { get; set; }
    public string ConfigurationJson { get; set; } = string.Empty;
    public string SourceSnapshotJson { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<ArticleReviewStageCheckpoint> Checkpoints { get; set; } = [];
}
