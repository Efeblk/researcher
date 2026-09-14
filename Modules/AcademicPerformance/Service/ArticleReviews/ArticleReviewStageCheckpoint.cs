using System.Text.Json.Serialization;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.ArticleReviews;

public sealed class ArticleReviewStageCheckpoint
{
    public long Id { get; set; }
    public long ArticleReviewWorkItemId { get; set; }
    [JsonIgnore] public ArticleReviewWorkItem? ArticleReviewWorkItem { get; set; } = null;
    public string Stage { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string BatchKey { get; set; } = string.Empty;
    public string? ParentBatchKey { get; set; } = null;
    public int Ordinal { get; set; }
    public string Status { get; set; } = "Pending";
    public string RequestJson { get; set; } = string.Empty;
    public string? ResultJson { get; set; } = null;
    public Guid? AttemptId { get; set; } = null;
    public string? RequestFingerprint { get; set; } = null;
    public decimal? ReservedCostUsd { get; set; } = null;
    public decimal? ActualCostUsd { get; set; } = null;
    public string? PricingVersion { get; set; } = null;
    public string? ErrorCode { get; set; } = null;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
