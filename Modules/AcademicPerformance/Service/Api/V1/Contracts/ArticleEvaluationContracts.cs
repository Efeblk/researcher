using System.ComponentModel.DataAnnotations;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class StartArticleEvaluationRequest
{
    [Required, MinLength(1), MaxLength(3)]
    public List<string> ProfileIds { get; set; } = [];

    [Required, StringLength(200, MinimumLength = 1)]
    public string PersonelId { get; set; } = string.Empty;

    [MaxLength(3)]
    public List<ArticleEvaluationRealCaseRequest> RealCases { get; set; } = [];

    public bool EnableBlindCrossCheck { get; set; }
}

public sealed class ArticleEvaluationRealCaseRequest
{
    [Range(1, int.MaxValue)]
    public int CanonicalWorkId { get; set; }

    [Required, StringLength(20, MinimumLength = 2)]
    public string Language { get; set; } = "en";
}

public sealed class GetArticleEvaluationRequest
{
    public Guid RunId { get; set; }

    [Required, StringLength(200, MinimumLength = 1)]
    public string PersonelId { get; set; } = string.Empty;

    [Range(0, int.MaxValue)]
    public int Skip { get; set; }

    [Range(1, 100)]
    public int Take { get; set; } = 50;
}

public sealed class CancelArticleEvaluationRequest
{
    public Guid RunId { get; set; }

    [StringLength(200)]
    public string? PersonelId { get; set; } = null;
}

public sealed record StartArticleEvaluationResponse(
    Guid RunId,
    string Status,
    int TotalCases,
    int TotalWorkItems,
    int WorstCaseModelCalls,
    string DatasetVersion,
    string EvaluatorVersion,
    string ScientificAccuracy);

public sealed record ArticleEvaluationProfileDto(
    string ProfileId,
    string DisplayName,
    string Provider,
    string RequestedModel,
    string? ModelRevision,
    string SettingsFingerprint,
    string SettingsVersion,
    IReadOnlyDictionary<string, string>? ExecutionSettings,
    bool LocalOnly);

public sealed record ArticleEvaluationWorkItemDto(
    long WorkItemId,
    string CaseId,
    string CaseKind,
    string Phase,
    string ProfileId,
    string Status,
    int AttemptCount,
    string? OutcomeCode,
    string? ActualModelIdentity,
    object? Metrics,
    object? Telemetry,
    decimal? EstimatedCostUsd,
    string CostStatus);

public sealed record ArticleEvaluationAggregateDto(
    int ScheduledClaims,
    int PendingClaims,
    int FailedClaims,
    int ScoredClaims,
    int CorrectClaims,
    int MissingClaims,
    int InvalidClaims,
    double? ControlledSourceReadingAccuracy,
    double? AbstentionRate,
    double? ErrorRate,
    IReadOnlyList<object> ConfusionMatrix,
    decimal? EstimatedCostUsd,
    string CostStatus,
    double? CrossModelVerdictAgreement,
    double? ExactEvidenceLinkRate,
    int ReviewCandidateFindings,
    int ReviewSupportedFindings,
    int ReviewUnsupportedFindings,
    int ReviewUncertainFindings,
    int ReviewOmittedFindings,
    double? ScientificAccuracy,
    double? RealArticleOmissionRecall);

public sealed record ArticleEvaluationProfileAggregateDto(
    string ProfileId,
    ArticleEvaluationAggregateDto Aggregate,
    long ElapsedMilliseconds,
    int? InputTokens,
    int? OutputTokens,
    string TokenStatus);

public sealed record ArticleEvaluationResponse(
    Guid RunId,
    string Status,
    string? OwnerPersonelId,
    string DatasetVersion,
    string EvaluatorVersion,
    string PolicyVersion,
    IReadOnlyList<ArticleEvaluationProfileDto> Profiles,
    int TotalCases,
    int TotalWorkItems,
    int WorstCaseModelCalls,
    int CompletedWorkItems,
    int FailedWorkItems,
    int SkippedWorkItems,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    ArticleEvaluationAggregateDto Aggregate,
    IReadOnlyList<ArticleEvaluationProfileAggregateDto> ProfileAggregates,
    int Skip,
    int Take,
    int Returned,
    IReadOnlyList<ArticleEvaluationWorkItemDto> WorkItems,
    string Interpretation);
