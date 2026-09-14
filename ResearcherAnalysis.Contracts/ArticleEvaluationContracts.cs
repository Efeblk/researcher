namespace AcademicCollector.Analysis.Contracts;

public static class ArticleEvaluationTaskKinds
{
    public const string Calibration = "calibration";
    public const string Review = "review";
    public const string CrossCheck = "cross_check";
}

public static class ArticleEvaluationOutcomes
{
    public const string Completed = "completed";
    public const string Failed = "failed";
}

public sealed record ArticleEvaluationRequest(
    string ProfileId,
    string TaskKind,
    string ExpectedSettingsFingerprint,
    ReviewArticleRequest Source)
{
    public IReadOnlyList<ArticleEvaluationCalibrationClaim>? CalibrationClaims { get; init; }
    public IReadOnlyList<ArticleReviewFinding>? Findings { get; init; }
}

public sealed record ArticleEvaluationCalibrationClaim(
    string ClaimId,
    string Text,
    string Section,
    IReadOnlyList<string> SourceIds);

public sealed record ArticleEvaluationVerdict(
    string ItemId,
    string Verdict,
    string Reason);

public sealed record ArticleEvaluationAttemptTelemetry(
    int AttemptNumber,
    string Stage,
    string? Role,
    string Provider,
    string RequestedModel,
    string? ReturnedModel,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    long ElapsedMilliseconds,
    // InputTokens is total billed input, including CacheReadTokens and CacheWriteTokens.
    int? InputTokens,
    // OutputTokens is total billed output, including the informational ThinkingTokens subset.
    int? OutputTokens,
    int? CacheReadTokens,
    int? CacheWriteTokens,
    int? ThinkingTokens,
    decimal? EstimatedCostUsd,
    string? PricingVersion,
    string? ErrorCode);

public sealed record ArticleEvaluationTelemetry(
    long ElapsedMilliseconds,
    int AttemptCount,
    IReadOnlyList<ArticleEvaluationAttemptTelemetry> Attempts);

public sealed record AnalysisFailureDetail(
    string Reason,
    string? Stage,
    string? Role);

public sealed record AnalysisErrorResponse(
    string Message,
    string ErrorCode)
{
    public AnalysisFailureDetail? Failure { get; init; }
    public ArticleReviewProviderAttempt? ProviderAttempt { get; init; }
}

public sealed record ArticleEvaluationResponse(
    string ProfileId,
    string TaskKind,
    string Provider,
    string RequestedModel,
    IReadOnlyList<string> ReturnedModels,
    string SettingsFingerprint,
    string SettingsVersion,
    string SourceIdentity,
    string Outcome,
    string? ErrorCode,
    ArticleEvaluationTelemetry Telemetry)
{
    public ArticleReviewReport? ReviewReport { get; init; }
    public IReadOnlyList<ArticleEvaluationVerdict>? Verdicts { get; init; }
    public IReadOnlyDictionary<string, string>? ExecutionSettings { get; init; }
    public AnalysisFailureDetail? Failure { get; init; }
}

public sealed record ArticleEvaluationProfile(
    string ProfileId,
    string DisplayName,
    string Provider,
    string RequestedModel,
    string? ModelRevision,
    string SettingsFingerprint,
    string SettingsVersion,
    IReadOnlyList<string> TaskKinds,
    string Availability,
    string? AvailabilityReasonCode,
    bool LocalOnly)
{
    public IReadOnlyDictionary<string, string>? ExecutionSettings { get; init; }
}

public sealed record ArticleEvaluationProfilesResponse(
    IReadOnlyList<ArticleEvaluationProfile> Profiles);
