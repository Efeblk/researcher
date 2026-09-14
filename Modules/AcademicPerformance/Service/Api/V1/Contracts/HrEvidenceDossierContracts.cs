using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class CreateHrEvidenceDossierRequest
{
    [Required, StringLength(200)]
    [JsonPropertyName("PersonelID"), Newtonsoft.Json.JsonProperty("PersonelID")]
    public string PersonelId { get; set; } = string.Empty;
    public long? PublicationMetricSnapshotId { get; set; }
    [Required, MaxLength(200)] public List<int> CanonicalWorkIds { get; set; } = [];
    [RegularExpression("^(tr|en)$")] public string Language { get; set; } = "tr";
}

public class GetHrEvidenceDossierRequest
{
    [Range(1, long.MaxValue)] public long DossierId { get; set; }
    [Required, StringLength(200)]
    [JsonPropertyName("PersonelID"), Newtonsoft.Json.JsonProperty("PersonelID")]
    public string PersonelId { get; set; } = string.Empty;
}

public sealed class AppendHrDossierReviewActionRequest : GetHrEvidenceDossierRequest
{
    public Guid ClientRequestId { get; set; }
    [Required, StringLength(40)] public string ActionType { get; set; } = string.Empty;
    [StringLength(200)] public string? EvidenceReference { get; set; }
    [StringLength(4000)] public string? Note { get; set; }
}

public sealed class ListHrDossierReviewActionsRequest : GetHrEvidenceDossierRequest
{
    [Range(0, int.MaxValue)] public int Skip { get; set; }
    [Range(1, 200)] public int Take { get; set; } = 100;
}

public sealed record HrEvidenceDossierResponse(
    long DossierId, string PersonelID, DateTimeOffset CreatedAt, string PolicyVersion,
    string InputFingerprint, HrEvidenceDossierContent Dossier);

public sealed record HrEvidenceDossierContent(
    HrDossierSubject Subject,
    HrDossierMetricEvidence? PublicationMetrics,
    IReadOnlyList<HrDossierWork> Works,
    IReadOnlyList<string> Limitations);

public sealed record HrDossierSubject(string PersonelID, string? Name, string? AcademicTitle, string? Department);
public sealed record HrDossierMetricEvidence(ResearcherPublicationMetricsResponse Data, bool IsStale,
    IReadOnlyList<string> StaleReasons);
public sealed record HrDossierWork(int CanonicalWorkId, string? NormalizedDoi,
    IReadOnlyList<HrDossierObservedValue<int>> Years,
    IReadOnlyList<HrDossierObservedValue<string>> Categories,
    string YearStatus, string CategoryStatus, bool HasRetractionObservation,
    IReadOnlyList<HrDossierArticleReview> Reviews);
public sealed record HrDossierObservedValue<T>(T Value, string Provider, DateTime ObservedAt, bool IsValid);
public sealed record HrDossierArticleReview(long ReviewRunId, long BaseAnalysisRunId, long SourceSnapshotId,
    string SourceHash,
    DateTimeOffset ReviewedAt, string Role, string Kind, string Basis, string? Suggestion,
    IReadOnlyList<HrDossierEvidence> Evidence, string VerificationStatus, bool IsPartial,
    bool IsStale, IReadOnlyList<string> StaleReasons,
    string AnalysisFreshnessStatus = "Unknown",
    IReadOnlyList<string>? AnalysisFreshnessReasons = null);
public sealed record HrDossierEvidence(long SpanId, string SourceId, int? PageNumber,
    int StartOffset, int EndOffset, string ExactText);
public sealed record HrDossierReviewActionDto(long Id, string ActionType, string? EvidenceReference,
    string? Note, string ActorAuditId, DateTimeOffset RecordedAt);
public sealed record HrDossierReviewActionResponse(long DossierId, HrDossierReviewActionDto Action, bool Reused);
public sealed record HrDossierReviewActionListResponse(long DossierId, int TotalCount, int Skip, int Take,
    IReadOnlyList<HrDossierReviewActionDto> Actions);
