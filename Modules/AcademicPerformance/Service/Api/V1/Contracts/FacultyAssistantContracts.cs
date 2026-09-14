using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using AcademicCollector.Analysis.Contracts;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class FacultyPrivateContext : IValidatableObject
{
    [RegularExpression("^(tr|en)$")] public string Language { get; set; } = "tr";
    [Required, MaxLength(20)] public List<string> ResearchGoals { get; set; } = [];
    [Required, MaxLength(20)] public List<string> Courses { get; set; } = [];
    [StringLength(500)] public string? TeachingAudience { get; set; }
    [StringLength(2000)] public string? Preferences { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ResearchGoals is null || Courses is null)
        {
            yield return new("Research goals and courses are required.",
                [nameof(ResearchGoals), nameof(Courses)]);
            yield break;
        }
        if (ResearchGoals.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 300))
            yield return new("Each research goal must contain 1 to 300 characters.", [nameof(ResearchGoals)]);
        if (Courses.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 300))
            yield return new("Each course must contain 1 to 300 characters.", [nameof(Courses)]);
    }
}

public sealed class SaveFacultyAssistantContextRequest
{
    [Required, StringLength(200)]
    [JsonPropertyName("PersonelID"), Newtonsoft.Json.JsonProperty("PersonelID")]
    public string PersonelId { get; set; } = string.Empty;
    [Range(0, int.MaxValue)] public int ExpectedVersion { get; set; }
    [Required] public FacultyPrivateContext Context { get; set; } = new();
}

public sealed record FacultyAssistantContextResponse(string PersonelID, int Version,
    string Fingerprint, DateTimeOffset CreatedAt, FacultyPrivateContext Context);

public sealed class StartFacultyAssistantRequest
{
    [Required, StringLength(200)]
    [JsonPropertyName("PersonelID"), Newtonsoft.Json.JsonProperty("PersonelID")]
    public string PersonelId { get; set; } = string.Empty;
    public Guid ClientRequestId { get; set; }
    [Required, RegularExpression("^(OwnPaperMethods|OwnPaperIssues|TeachingHelp|RelatedWorks|ExploreOwnRecord)$")]
    public string Mode { get; set; } = string.Empty;
    [Required, RegularExpression("^(tr|en)$")] public string Language { get; set; } = "tr";
    [Required, StringLength(2000)] public string Query { get; set; } = string.Empty;
    [Required, MaxLength(20)] public List<int> CanonicalWorkIds { get; set; } = [];
    [Range(1, 20)] public int Take { get; set; } = 10;
    [Range(1, int.MaxValue)] public int? ContextVersion { get; set; }
}

public sealed class GetFacultyAssistantRunRequest
{
    [Required, StringLength(200)]
    [JsonPropertyName("PersonelID"), Newtonsoft.Json.JsonProperty("PersonelID")]
    public string PersonelId { get; set; } = string.Empty;
    public Guid RunId { get; set; }
}

public sealed record FacultyAssistantPinnedContext(
    long ContextVersionId, int Version, string Fingerprint);

public sealed record FacultyAssistantRetrievalCoverage(
    string Value, int CanonicalWorkCount);

public sealed record FacultyAssistantSourceCoverage(
    int ProcessedChunks, int TotalChunks, int ProcessedPages,
    int TextBearingPages, int TotalPages, string? ScopeReason);

public sealed record FacultyAssistantMatchProvenance(
    string Kind, long AnalysisRunId, string Language,
    long? CanonicalArticleClaimId, string? Section);

public sealed record FacultyAssistantRetrievedEvidence(
    string EvidenceId, int CanonicalWorkId, long ArticleSourceSnapshotId,
    long ArticleSourceSpanId, long AnalysisRunId, string SourceId, int? PageNumber,
    int StartOffset, int EndOffset, string AnalysisLanguage, string SourceKind,
    string ExtractionVersion, string ExtractedTextHash, bool IsPartial,
    FacultyAssistantSourceCoverage SourceCoverage,
    IReadOnlyList<FacultyAssistantMatchProvenance>? MatchProvenance = null,
    bool? IsMatchProvenanceTruncated = null,
    string? PinnedFreshnessStatus = null,
    IReadOnlyList<string>? PinnedFreshnessReasons = null,
    string? CurrentFreshnessStatus = null,
    IReadOnlyList<string>? CurrentFreshnessReasons = null);

public sealed record FacultyAssistantRetrievalSummary(
    string Catalog, string CatalogVersion, string QueryHash, string CorpusHash,
    string InputHash, string CorpusFreshness, int RequestedCanonicalWorkCount,
    int EligibleCanonicalWorkCount, int CoveredCanonicalWorkCount,
    int MissingSourceCanonicalWorkCount, int CandidateSpanCount,
    bool IsCorpusTruncated,
    IReadOnlyList<FacultyAssistantRetrievalCoverage> AnalysisLanguageCoverage,
    IReadOnlyList<FacultyAssistantRetrievalCoverage> SourceKindCoverage,
    IReadOnlyList<FacultyAssistantRetrievedEvidence> Evidence,
    string? QueryPlanHash = null, string? ClaimBridgeHash = null,
    int? ClaimBridgeEligibleRunCount = null,
    int? ClaimBridgeCoveredCanonicalWorkCount = null,
    int? ClaimBridgeCandidateClaimCount = null,
    int? ClaimBridgeCandidateSpanCount = null,
    bool? IsClaimBridgeTruncated = null,
    IReadOnlyList<FacultyAssistantRetrievalCoverage>? ClaimBridgeLanguageCoverage = null,
    bool? IsDirectCorpusTruncated = null,
    string? PinnedFreshnessHash = null,
    int? PinnedCurrentAnalysisRunCount = null,
    int? PinnedStaleAnalysisRunCount = null,
    int? PinnedUnknownAnalysisRunCount = null,
    string? CurrentFreshnessHash = null,
    int? CurrentAnalysisRunCount = null,
    int? StaleAnalysisRunCount = null,
    int? UnknownAnalysisRunCount = null);

public sealed record FacultyAssistantRunResponse(Guid RunId, string PersonelID, string Status,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, int AttemptCount,
    string RetrievalPolicyVersion, FacultyAssistantPinnedContext? Context,
    FacultyAssistantRetrievalSummary Retrieval, string? InputFingerprint,
    FacultyAssistantAnalysisReport? Report,
    string? ErrorCode, string? ErrorMessage, bool Reused);
