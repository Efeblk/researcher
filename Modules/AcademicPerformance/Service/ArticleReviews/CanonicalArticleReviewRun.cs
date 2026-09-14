using System.Text.Json.Serialization;
using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.ArticleReviews;

public sealed class CanonicalArticleReviewRun
{
    public long Id { get; set; }
    public int CanonicalWorkId { get; set; }
    [JsonIgnore] public CanonicalWork? CanonicalWork { get; set; } = null;
    public long BaseAnalysisRunId { get; set; }
    [JsonIgnore] public CanonicalArticleAnalysisRun? BaseAnalysisRun { get; set; } = null;
    public long ArticleSourceSnapshotId { get; set; }
    [JsonIgnore] public ArticleSourceSnapshot? ArticleSourceSnapshot { get; set; } = null;
    public DateTimeOffset ReviewedAt { get; set; }
    public string Language { get; set; } = string.Empty;
    public string PolicyVersion { get; set; } = string.Empty;
    public string? SettingsFingerprint { get; set; } = null;
    public string Model { get; set; } = string.Empty;
    public string PromptVersion { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string VerificationStatus { get; set; } = string.Empty;
    public string VerificationModel { get; set; } = string.Empty;
    public string VerificationPromptVersion { get; set; } = string.Empty;
    public bool UsesSameModelFamily { get; set; }
    public string? VerificationLimitation { get; set; } = null;
    public int ProcessedPages { get; set; }
    public int TextBearingPages { get; set; }
    public int TotalPages { get; set; }
    public bool IsPartial { get; set; }
    public string? ScopeReason { get; set; } = null;
    public int ProcessedRoles { get; set; }
    public int TotalRoles { get; set; }
    public int CandidateFindings { get; set; }
    public int AutomaticallyCheckedFindings { get; set; }
    public int SupportedFindings { get; set; }
    public int UnsupportedFindings { get; set; }
    public int UncertainFindings { get; set; }
    public int OmittedFindings { get; set; }
    public string OmissionReasonsJson { get; set; } = "[]";
    public string ReportJson { get; set; } = string.Empty;
    public List<CanonicalArticleReviewFinding> Findings { get; set; } = [];
}
