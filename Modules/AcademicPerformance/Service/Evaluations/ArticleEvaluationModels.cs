using System.Text.Json.Serialization;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Evaluations;

public static class ArticleEvaluationStatus
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string Completed = "Completed";
    public const string CompletedWithFailures = "CompletedWithFailures";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
    public const string Interrupted = "Interrupted";
    public const string Skipped = "Skipped";
}

public sealed class ArticleEvaluationRun
{
    public long Id { get; set; }
    public Guid RunId { get; set; }
    public string? OwnerPersonelId { get; set; } = null;
    public string Status { get; set; } = ArticleEvaluationStatus.Pending;
    public string DatasetVersion { get; set; } = string.Empty;
    public string EvaluatorVersion { get; set; } = string.Empty;
    public string PolicyVersion { get; set; } = string.Empty;
    public string ProfilesJson { get; set; } = "[]";
    public bool IncludesRealCases { get; set; }
    public int TotalCases { get; set; }
    public int TotalWorkItems { get; set; }
    public int WorstCaseModelCalls { get; set; }
    public int CompletedWorkItems { get; set; }
    public int FailedWorkItems { get; set; }
    public int SkippedWorkItems { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; } = null;
    public DateTimeOffset? CompletedAt { get; set; } = null;
    public List<ArticleEvaluationCase> Cases { get; set; } = [];
}

public sealed class ArticleEvaluationCase
{
    public long Id { get; set; }
    public long ArticleEvaluationRunId { get; set; }
    [JsonIgnore] public ArticleEvaluationRun? Run { get; set; } = null;
    public int Ordinal { get; set; }
    public string CaseId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public int? CanonicalWorkId { get; set; } = null;
    public long? BaseAnalysisRunId { get; set; } = null;
    public string Language { get; set; } = string.Empty;
    public string SourceHash { get; set; } = string.Empty;
    public string SourceSnapshotJson { get; set; } = string.Empty;
    public string ReferenceJson { get; set; } = string.Empty;
    public string? ExpectedVerdictsJson { get; set; } = null;
    public string RequestPayloadJson { get; set; } = string.Empty;
    public List<ArticleEvaluationWorkItem> WorkItems { get; set; } = [];
}

public sealed class ArticleEvaluationWorkItem
{
    public long Id { get; set; }
    public long ArticleEvaluationCaseId { get; set; }
    [JsonIgnore] public ArticleEvaluationCase? Case { get; set; } = null;
    public long? DependsOnWorkItemId { get; set; } = null;
    [JsonIgnore] public ArticleEvaluationWorkItem? DependsOn { get; set; } = null;
    public int Ordinal { get; set; }
    public string Phase { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
    public string ProfileFingerprint { get; set; } = string.Empty;
    public string ProfileSnapshotJson { get; set; } = string.Empty;
    public string Status { get; set; } = ArticleEvaluationStatus.Pending;
    public int AttemptCount { get; set; }
    public int MaximumAttempts { get; set; } = 1;
    public Guid? ExecutionToken { get; set; } = null;
    public DateTimeOffset? StartedAt { get; set; } = null;
    public DateTimeOffset? CompletedAt { get; set; } = null;
    public string? OutcomeCode { get; set; } = null;
    public string? OutcomeMessage { get; set; } = null;
    public List<ArticleEvaluationAttempt> Attempts { get; set; } = [];
    public ArticleEvaluationResult? Result { get; set; } = null;
}

public sealed class ArticleEvaluationAttempt
{
    public long Id { get; set; }
    public long ArticleEvaluationWorkItemId { get; set; }
    [JsonIgnore] public ArticleEvaluationWorkItem? WorkItem { get; set; } = null;
    public int AttemptNumber { get; set; }
    public Guid ExecutionToken { get; set; }
    public string Status { get; set; } = ArticleEvaluationStatus.Running;
    public string RequestJson { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string? ResponseJson { get; set; } = null;
    public string? ReturnedModelIdentity { get; set; } = null;
    public string? TelemetryJson { get; set; } = null;
    public decimal? EstimatedCostUsd { get; set; } = null;
    public string CostStatus { get; set; } = "Unknown";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; } = null;
    public string? ErrorCode { get; set; } = null;
    public string? ErrorMessage { get; set; } = null;
}

public sealed class ArticleEvaluationResult
{
    public long Id { get; set; }
    public long ArticleEvaluationWorkItemId { get; set; }
    [JsonIgnore] public ArticleEvaluationWorkItem? WorkItem { get; set; } = null;
    public string ActualModelIdentity { get; set; } = string.Empty;
    public string MetricsJson { get; set; } = string.Empty;
    public string ResultJson { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
