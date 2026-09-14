using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ResearcherAnalysisService.Products.Api.Contracts;

public sealed class ResearcherPublicationMetricsRequest
{
    [Required, StringLength(200)]
    [JsonPropertyName("PersonelID")]
    public string PersonelId { get; set; } = string.Empty;
}

public sealed class ResearcherPublicationMetricsResponse
{
    [JsonPropertyName("PersonelID")]
    public string PersonelId { get; set; } = string.Empty;
    public string Catalog { get; set; } = string.Empty;
    public string CatalogVersion { get; set; } = string.Empty;
    public string ResultLabel { get; set; } = string.Empty;
    public DateTime ComputedAt { get; set; }
    public int ValidYearUpperBound { get; set; }
    public int CanonicalWorkCount { get; set; }
    public int ProviderObservationCount { get; set; }
    public int UnmappedAcademicWorkCount { get; set; }
    public List<PublicationMetricDefinitionDto> Definitions { get; set; } = [];
    public PublicationYearMetricsDto PublicationYears { get; set; } = new();
    public PublicationCategoryMetricsDto Categories { get; set; } = new();
    public PublicationCoverageMetricsDto Coverage { get; set; } = new();
    public PublicationProviderMetricsDto ProviderMetrics { get; set; } = new();
    public PublicationContextualMetricsDto ContextualMetrics { get; set; } = new();
    public PublicationEligibilityPolicyDto EligibilityPolicy { get; set; } = new();
    public ReferencePopulationReadinessDto ReferencePopulation { get; set; } = new();
    public CrossProviderComparabilityDto CrossProviderComparability { get; set; } = new();
}

public sealed class PublicationEligibilityPolicyDto
{
    public string PolicyVersion { get; set; } = "collected-publication-description-v1";
    public string Purpose { get; set; } = "Descriptive reporting of the current collected canonical record.";
    public List<string> IncludedCategories { get; set; } = [];
    public string ProviderTotalsInEvaluatedOutputs { get; set; } = "Excluded";
    public string Reason { get; set; } = string.Empty;
}

public sealed class ReferencePopulationReadinessDto
{
    public string Status { get; set; } = "Unavailable";
    public string? ManifestVersion { get; set; } = null;
    public string? ManifestFingerprint { get; set; } = null;
    public bool IsReviewed { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class CrossProviderComparabilityDto
{
    public string Label { get; set; } = "ObservedCrossProviderConsistency";
    public string Scope { get; set; } = string.Empty;
    public List<CrossProviderPairDto> ProviderPairs { get; set; } = [];
}

public sealed class CrossProviderPairDto
{
    public string FirstProvider { get; set; } = string.Empty;
    public string SecondProvider { get; set; } = string.Empty;
    public int CanonicalWorkOverlapDenominator { get; set; }
    public CrossProviderFieldComparisonDto PublicationYear { get; set; } = new();
    public CrossProviderFieldComparisonDto Category { get; set; } = new();
    public CrossProviderCitationComparisonDto CitationCount { get; set; } = new();
}

public class CrossProviderFieldComparisonDto
{
    public int AvailablePairCount { get; set; }
    public int MissingPairCount { get; set; }
    public int ConflictPairCount { get; set; }
    public int ExactAgreementCount { get; set; }
    public decimal? ExactAgreementProportion { get; set; }
}

public sealed class CrossProviderCitationComparisonDto : CrossProviderFieldComparisonDto
{
    public long AbsoluteDifferenceSum { get; set; }
    public decimal? MeanAbsoluteDifference { get; set; }
}

public sealed class PublicationContextualMetricsDto
{
    public string Scope { get; set; } = string.Empty;
    public int EligibleCanonicalWorkCount { get; set; }
    public PublicationCoverageValueDto OpenAlexContextCoverage { get; set; } = new();
    public PublicationCoverageValueDto PrimaryTopicCoverage { get; set; } = new();
    public PublicationCoverageValueDto PrimarySubfieldGroupCoverage { get; set; } = new();
    public PublicationCoverageValueDto PrimaryFieldGroupCoverage { get; set; } = new();
    public int MissingContextCanonicalWorkCount { get; set; }
    public int InvalidContextCanonicalWorkCount { get; set; }
    public int PrimaryTopicConflictCanonicalWorkCount { get; set; }
    public int PrimaryGroupConflictCanonicalWorkCount { get; set; }
    public List<PublicationPrimaryTopicBucketDto> PrimaryTopics { get; set; } = [];
    public List<PublicationResearchGroupDto> PrimarySubfieldGroups { get; set; } = [];
    public List<PublicationResearchGroupDto> PrimaryFieldGroups { get; set; } = [];
    public ProviderNormalizationSummaryDto ProviderReportedNormalization { get; set; } = new();
    public decimal? InternalNormalizedScore { get; set; } = null;
    public string InternalNormalizedScoreStatus { get; set; } = "ReferencePopulationUnavailable";
    public string InternalNormalizedScoreReason { get; set; } =
        "No evaluated reference population is available.";
}

public sealed class PublicationPrimaryTopicBucketDto
{
    public string TopicId { get; set; } = string.Empty;
    public string? TopicName { get; set; } = null;
    public int CanonicalWorkCount { get; set; }
}

public sealed class PublicationResearchGroupDto
{
    public string ClassificationId { get; set; } = string.Empty;
    public string? ClassificationName { get; set; } = null;
    public int PublicationYear { get; set; }
    public string RawType { get; set; } = string.Empty;
    public string? ArticlePrimarySourceType { get; set; } = null;
    public string Label { get; set; } = string.Empty;
    public int CanonicalWorkCount { get; set; }
    public ProviderNormalizedValueSummaryDto MeanFwci { get; set; } = new();
}

public sealed class ProviderNormalizationSummaryDto
{
    public string Label { get; set; } = "ProviderReportedNormalization";
    public string Scope { get; set; } = string.Empty;
    public ProviderNormalizedValueSummaryDto Fwci { get; set; } = new();
    public ProviderNormalizedValueSummaryDto CitationNormalizedPercentile { get; set; } = new();
}

public sealed class ProviderNormalizedValueSummaryDto
{
    public int EligibleDenominator { get; set; }
    public int AvailableValueCount { get; set; }
    public int MissingValueCount { get; set; }
    public int InvalidValueCount { get; set; }
    public int ConflictValueCount { get; set; }
    public decimal? AvailableValueSum { get; set; } = null;
    public decimal? MeanValue { get; set; } = null;
}

public sealed class PublicationProviderMetricsDto
{
    public OpenAlexProviderMetricsDto OpenAlex { get; set; } = new();
    public GoogleScholarProviderMetricsDto GoogleScholar { get; set; } = new();
    public WebOfScienceProviderMetricsDto WebOfScience { get; set; } = new();
}

public sealed class OpenAlexProviderMetricsDto
{
    public string Provider { get; set; } = "OpenAlex";
    public string Scope { get; set; } = string.Empty;
    public DateTime? SourceUpdatedAt { get; set; } = null;
    public ProviderCountMetricDto CitationCount { get; set; } = new();
    public ProviderCountMetricDto HIndex { get; set; } = new();
    public ProviderCountMetricDto I10Index { get; set; } = new();
    public ProviderCountMetricDto DocumentCount { get; set; } = new();
    public ProviderDecimalMetricDto TwoYearMeanCitedness { get; set; } = new();
    public List<ProviderMetricFieldDefinitionDto> FieldDefinitions { get; set; } = [];
}

public sealed class GoogleScholarProviderMetricsDto
{
    public string Provider { get; set; } = "GoogleScholar";
    public string Scope { get; set; } = string.Empty;
    public DateTime? SourceUpdatedAt { get; set; } = null;
    public ProviderCountMetricDto CitationCount { get; set; } = new();
    public ProviderCountMetricDto HIndex { get; set; } = new();
    public ProviderCountMetricDto I10Index { get; set; } = new();
    public ProviderCountMetricDto DocumentCount { get; set; } = new();
    public ProviderCountMetricDto CitationCountRecent { get; set; } = new();
    public ProviderCountMetricDto HIndexRecent { get; set; } = new();
    public ProviderCountMetricDto I10IndexRecent { get; set; } = new();
    public ProviderCountMetricDto MetricsSinceYear { get; set; } = new();
    public List<ProviderMetricFieldDefinitionDto> FieldDefinitions { get; set; } = [];
}

public sealed class WebOfScienceProviderMetricsDto
{
    public string Provider { get; set; } = "WebOfScience";
    public string Scope { get; set; } = string.Empty;
    public DateTime? SourceUpdatedAt { get; set; } = null;
    public ProviderCountMetricDto CitationCount { get; set; } = new();
    public ProviderCountMetricDto HIndex { get; set; } = new();
    public ProviderCountMetricDto DocumentCount { get; set; } = new();
    public List<ProviderMetricFieldDefinitionDto> FieldDefinitions { get; set; } = [];
}

public sealed class ProviderMetricFieldDefinitionDto
{
    public string Field { get; set; } = string.Empty;
    public string Origin { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string? Period { get; set; } = null;
}

public sealed class ProviderCountMetricDto
{
    public int? Value { get; set; } = null;
    public string Quality { get; set; } = "Unknown";
    public string? QualityReason { get; set; } = null;
}

public sealed class ProviderDecimalMetricDto
{
    public decimal? Value { get; set; } = null;
    public string Quality { get; set; } = "Unknown";
    public string? QualityReason { get; set; } = null;
}

public sealed class ProviderBooleanMetricDto
{
    public bool? Value { get; set; } = null;
    public string Quality { get; set; } = "Unknown";
    public string? QualityReason { get; set; } = null;
}

public sealed class ResearcherPublicationMetricsStatusResponse
{
    [JsonPropertyName("PersonelID")]
    public string PersonelId { get; set; } = string.Empty;
    public string CurrentCatalogVersion { get; set; } = string.Empty;
    public int CurrentComputationYear { get; set; }
    public string? RequestedCatalogVersion { get; set; } = null;
    public int? RequestedComputationYear { get; set; } = null;
    public long RequestedRevision { get; set; }
    public long ComputedRevision { get; set; }
    public long? SnapshotId { get; set; } = null;
    public string? SnapshotCatalogVersion { get; set; } = null;
    public int? SnapshotComputationYear { get; set; } = null;
    public string Status { get; set; } = string.Empty;
    public bool IsStale { get; set; }
    public DateTime? ComputedAt { get; set; } = null;
    public PublicationMetricsRefreshOutcomeDto RefreshOutcome { get; set; } = new();
    public ResearcherPublicationMetricsResponse? Data { get; set; } = null;
}

public sealed class PublicationMetricsRefreshOutcomeDto
{
    public int Attempts { get; set; }
    public DateTime? NextAttemptAt { get; set; } = null;
    public DateTime? LastSuccessAt { get; set; } = null;
    public string? Code { get; set; } = null;
    public string? Message { get; set; } = null;
}

public sealed class PublicationMetricDefinitionDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string Formula { get; set; } = string.Empty;
    public string Denominator { get; set; } = string.Empty;
}

public sealed class PublicationYearMetricsDto
{
    public List<PublicationYearBucketDto> Histogram { get; set; } = [];
    public int ResolvedCanonicalWorkCount { get; set; }
    public int ConflictCanonicalWorkCount { get; set; }
    public int UnknownCanonicalWorkCount { get; set; }
    public int InvalidYearObservationCount { get; set; }
    public int InvalidYearCanonicalWorkCount { get; set; }
}

public sealed class PublicationYearBucketDto
{
    public int Year { get; set; }
    public int CanonicalWorkCount { get; set; }
}

public sealed class PublicationCategoryMetricsDto
{
    public List<PublicationCategoryBucketDto> Histogram { get; set; } = [];
    public int ResolvedCanonicalWorkCount { get; set; }
    public int ConflictCanonicalWorkCount { get; set; }
    public int UnknownCanonicalWorkCount { get; set; }
}

public sealed class PublicationCategoryBucketDto
{
    public string Category { get; set; } = string.Empty;
    public int CanonicalWorkCount { get; set; }
}

public sealed class PublicationCoverageMetricsDto
{
    public PublicationCoverageValueDto NormalizedCanonicalDoi { get; set; } = new();
    public PublicationCoverageValueDto SavedAbstract { get; set; } = new();
    public PublicationCoverageValueDto RecordedSourceUrl { get; set; } = new();
}

public sealed class PublicationCoverageValueDto
{
    public int Numerator { get; set; }
    public int EligibleDenominator { get; set; }
    public int Missing { get; set; }
    public decimal? Proportion { get; set; } = null;
}
