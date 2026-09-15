using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class CanonicalPublicationListRequest : ServiceRequest
{
    [JsonPropertyName("PersonelID"), Newtonsoft.Json.JsonProperty("PersonelID")]
    public string? PersonelId { get; set; } = null;
    [JsonPropertyName("ORCID"), Newtonsoft.Json.JsonProperty("ORCID")]
    public string? Orcid { get; set; } = null;
    [JsonPropertyName("ScholarID"), Newtonsoft.Json.JsonProperty("ScholarID")]
    public string? GoogleScholarId { get; set; } = null;
    [JsonPropertyName("ResearcherID"), Newtonsoft.Json.JsonProperty("ResearcherID")]
    public string? WebOfScienceResearcherId { get; set; } = null;
    public string? SearchText { get; set; } = null;
    [Range(0, int.MaxValue)]
    public int Skip { get; set; }
    [Range(1, 500)]
    public int Take { get; set; } = 100;
}

public sealed class CanonicalPublicationListResponse : ServiceResponse
{
    [JsonPropertyName("PersonelID"), Newtonsoft.Json.JsonProperty("PersonelID")]
    public string PersonelId { get; set; } = string.Empty;
    public List<CanonicalPublicationDto> Entities { get; set; } = [];
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
}

public sealed class CanonicalPublicationDto
{
    public int Id { get; set; }
    public string IdentityKind { get; set; } = string.Empty;
    public string? NormalizedDoi { get; set; } = null;
    public string? Title { get; set; } = null;
    public int? PublicationYear { get; set; } = null;
    public DateTime? PublicationDate { get; set; } = null;
    public string Category { get; set; } = string.Empty;
    public string? AuthorsObserved { get; set; } = null;
    public string? Publication { get; set; } = null;
    public bool HasRetractionObservation { get; set; }
    public int KnownResearcherCount { get; set; }
    public List<CanonicalPublicationObservationDto> Observations { get; set; } = [];
}

public sealed class CanonicalPublicationObservationDto
{
    public int AcademicWorkId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string? ProviderWorkId { get; set; } = null;
    public string? TitleObserved { get; set; } = null;
    public string? DoiObserved { get; set; } = null;
    public int? PublicationYearObserved { get; set; } = null;
    public DateTime? PublicationDateObserved { get; set; } = null;
    public string CategoryObserved { get; set; } = string.Empty;
    public string? AuthorsObserved { get; set; } = null;
    public string? PublicationObserved { get; set; } = null;
    public string? SourceId { get; set; } = null;
    public string? SourceName { get; set; } = null;
    public string? SourceType { get; set; } = null;
    public string? Link { get; set; } = null;
    public string? FullTextUrl { get; set; } = null;
    public string? License { get; set; } = null;
    public string? Version { get; set; } = null;
    public bool? IsRetracted { get; set; } = null;
    public DateTime ObservedAt { get; set; }
    public List<AcademicWorkSourceDto> SourceUrls { get; set; } = [];
    public AcademicWorkResearchContextDto? ResearchContext { get; set; } = null;
}

public sealed class AcademicWorkResearchContextDto
{
    public string Provider { get; set; } = string.Empty;
    public string? SourceWorkId { get; set; } = null;
    public string ParserVersion { get; set; } = string.Empty;
    public string PayloadFingerprint { get; set; } = string.Empty;
    public DateTime SourceSyncedAt { get; set; }
    public DateTime? ProviderUpdatedAt { get; set; } = null;
    public int? SourcePublicationYear { get; set; } = null;
    public string? RawType { get; set; } = null;
    public string? PrimarySourceType { get; set; } = null;
    public ProviderDecimalMetricDto Fwci { get; set; } = new();
    public ProviderDecimalMetricDto CitationNormalizedPercentile { get; set; } = new();
    public ProviderBooleanMetricDto IsInTopOnePercent { get; set; } = new();
    public ProviderBooleanMetricDto IsInTopTenPercent { get; set; } = new();
    public string ParseQuality { get; set; } = string.Empty;
    public string? ParseQualityReason { get; set; } = null;
    public string PrimaryTopicQuality { get; set; } = string.Empty;
    public string? PrimaryTopicQualityReason { get; set; } = null;
    public List<AcademicWorkTopicDto> Topics { get; set; } = [];
}

public sealed class AcademicWorkTopicDto
{
    public string TopicId { get; set; } = string.Empty;
    public string? TopicName { get; set; } = null;
    public string? SubfieldId { get; set; } = null;
    public string? SubfieldName { get; set; } = null;
    public string? FieldId { get; set; } = null;
    public string? FieldName { get; set; } = null;
    public string? DomainId { get; set; } = null;
    public string? DomainName { get; set; } = null;
    public int OriginalRank { get; set; }
    public double? AssignmentScore { get; set; } = null;
    public string ScoreQuality { get; set; } = string.Empty;
    public string? ScoreQualityReason { get; set; } = null;
    public bool IsPrimary { get; set; }
}

public sealed class AcademicWorkSourceDto
{
    public string Url { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Origin { get; set; } = string.Empty;
    public bool? IsOpenAccess { get; set; } = null;
}
