using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;

public sealed class ArticleSummaryAutomationJob
{
    public long Id { get; set; }
    public int CanonicalWorkId { get; set; }
    public CanonicalWork? CanonicalWork { get; set; } = null;
    public string Language { get; set; } = string.Empty;
    public string Status { get; set; } = ArticleSummaryAutomationJobStatus.Pending;
    public string DesiredInputHash { get; set; } = string.Empty;
    public string DesiredPolicyVersion { get; set; } = string.Empty;
    public string? RunningInputHash { get; set; } = null;
    public string? RunningPolicyVersion { get; set; } = null;
    public string? ProcessedInputHash { get; set; } = null;
    public string? ProcessedPolicyVersion { get; set; } = null;
    public Guid? ExecutionToken { get; set; } = null;
    public int AttemptGeneration { get; set; }
    public int Attempts { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public DateTime? StartedAt { get; set; } = null;
    public DateTime? CompletedAt { get; set; } = null;
    public DateTime UpdatedAt { get; set; }
    public string? LastOutcomeCode { get; set; } = null;
    public string? LastOutcomeMessage { get; set; } = null;
    public long? LastSuccessfulAnalysisRunId { get; set; } = null;
    public CanonicalArticleAnalysisRun? LastSuccessfulAnalysisRun { get; set; } = null;
}

public static class ArticleSummaryAutomationJobStatus
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string RetryWaiting = "RetryWaiting";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
}

public sealed record ArticleSummaryAutomationAttempt(
    long JobId,
    int CanonicalWorkId,
    string Language,
    string InputHash,
    string PolicyVersion,
    Guid ExecutionToken);

public sealed record ArticleSummaryAutomationExecutionResult(long AnalysisRunId, bool Reused);
