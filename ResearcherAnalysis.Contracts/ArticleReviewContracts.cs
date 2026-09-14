namespace AcademicCollector.Analysis.Contracts;

public sealed record ReviewArticleRequest(
    string Language,
    string SourceKind,
    string SourceHash,
    string ExtractionVersion,
    string PolicyVersion,
    IReadOnlyList<ArticlePage> Pages,
    int TotalSourcePages,
    bool IsPartial,
    string? ScopeReason)
{
    public IReadOnlyList<ArticleSourceSpan>? SourceSpans { get; init; }
}

public sealed record ArticleReviewEvidence(
    string SourceId,
    int? PageNumber,
    int StartOffset,
    int EndOffset,
    string Quote);

public sealed record ArticleReviewFinding(
    string FindingId,
    string Role,
    string Kind,
    string Basis,
    string? Suggestion,
    IReadOnlyList<ArticleReviewEvidence> Evidence);

public sealed record ArticleSpecialistReview(
    string Role,
    string Status,
    IReadOnlyList<ArticleReviewFinding> Findings);

public sealed record ArticleReviewSourceCoverage(
    int ProcessedPages,
    int TextBearingPages,
    int TotalPages,
    bool IsPartial,
    string? ScopeReason);

public sealed record ArticleReviewCoverage(
    int ProcessedRoles,
    int TotalRoles,
    int CandidateFindings,
    int AutomaticallyCheckedFindings,
    int SupportedFindings,
    int UnsupportedFindings,
    int UncertainFindings,
    int OmittedFindings,
    IReadOnlyList<string> OmissionReasons);

public sealed record ArticleReviewVerificationMetadata(
    string Status,
    string Model,
    string PromptVersion,
    bool UsesSameModelFamily,
    string? Limitation);

public sealed record ArticleReviewReport(
    string Language,
    string SourceKind,
    string SourceHash,
    string ExtractionVersion,
    string PolicyVersion,
    string Outcome,
    ArticleReviewSourceCoverage SourceCoverage,
    ArticleReviewCoverage Coverage,
    IReadOnlyList<ArticleSpecialistReview> Reviews,
    string Model,
    string PromptVersion,
    ArticleReviewVerificationMetadata Verification)
{
    public ArticleSourceFidelity? SourceFidelity { get; init; }
}

public static class ArticleReviewStageKinds
{
    public const string Generation = "generation";
    public const string Verification = "verification";
}

public static class ArticleReviewGenerationRecovery
{
    public const string PolicyVersion = "article-review-generation-output-limit-recovery-v1";
    public const string InitialThinkingLevel = "high";
    public const string RecoveryThinkingLevel = "medium";
}

public sealed record ArticleReviewRuntimeConfiguration(
    string SettingsFingerprint,
    string Provider,
    string GenerationModel,
    string VerificationModel,
    string GenerationPromptVersion,
    string VerificationPromptVersion,
    int GenerationMaxOutputTokens,
    int VerificationMaxOutputTokens,
    string GenerationThinkingLevel,
    string VerificationThinkingLevel,
    string PricingVersion)
{
    public string? GenerationRecoveryPolicyVersion { get; init; }
    public string? GenerationRecoveryThinkingLevel { get; init; }
}

public sealed record ArticleReviewCandidateFinding(
    string FindingId,
    string Role,
    string Kind,
    string Basis,
    string? Suggestion,
    IReadOnlyList<string> SourceIds);

public sealed record ArticleReviewStageQuoteRequest(
    string Stage,
    string Role,
    ReviewArticleRequest Source)
{
    public IReadOnlyList<ArticleReviewCandidateFinding>? Findings { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? GenerationThinkingLevel { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Guid? RecoveryOfAttemptId { get; init; }
}

public sealed record ArticleReviewStageQuote(
    string SettingsFingerprint,
    string RequestFingerprint,
    decimal MaximumChargeUsd);

public sealed record ArticleReviewStageDispatchRequest(
    Guid AttemptId,
    string SettingsFingerprint,
    string RequestFingerprint,
    decimal MaximumChargeUsd,
    string Role,
    ReviewArticleRequest Source)
{
    public IReadOnlyList<ArticleReviewCandidateFinding>? Findings { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? GenerationThinkingLevel { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Guid? RecoveryOfAttemptId { get; init; }
}

public sealed record ArticleReviewProviderAttempt(
    Guid AttemptId,
    string Outcome,
    string? ReturnedModel,
    decimal? EstimatedCostUsd,
    string? PricingVersion);

public sealed record ArticleReviewGenerationStageResult(
    string Role,
    IReadOnlyList<ArticleReviewCandidateFinding> Findings,
    string Model,
    string PromptVersion,
    ArticleReviewProviderAttempt Attempt);

public sealed record ArticleReviewVerificationVerdict(
    string FindingId,
    string Verdict,
    string Reason);

public sealed record ArticleReviewVerificationStageResult(
    IReadOnlyList<ArticleReviewVerificationVerdict> Verdicts,
    string Model,
    string PromptVersion,
    ArticleReviewProviderAttempt Attempt);
