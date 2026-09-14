using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class AcademicEvidenceSearchRequest
{
    [Required, StringLength(200)]
    [JsonPropertyName("PersonelID"), Newtonsoft.Json.JsonProperty("PersonelID")]
    public string PersonelId { get; set; } = string.Empty;

    [Required, StringLength(500, MinimumLength = 1)]
    public string Query { get; set; } = string.Empty;

    [Required]
    public List<int> CanonicalWorkIds { get; set; } = [];

    [Range(1, 20)]
    public int Take { get; set; } = 10;
}

public sealed class AcademicEvidenceSearchResponse
{
    public string Catalog { get; set; } = "academic-evidence-search";
    public string CatalogVersion { get; set; } = "academic-evidence-search-v1";
    public string NormalizedQuery { get; set; } = string.Empty;
    public string QueryHash { get; set; } = string.Empty;
    public string? QueryPlanHash { get; set; }
    public string CorpusHash { get; set; } = string.Empty;
    public string? ClaimBridgeHash { get; set; }
    public string InputHash { get; set; } = string.Empty;
    public string? FreshnessHash { get; set; }
    public string CorpusFreshness { get; set; } =
        "Latest successful saved analysis per canonical work and language; a source can predate a later provider refresh.";
    public int RequestedCanonicalWorkCount { get; set; }
    public int EligibleCanonicalWorkCount { get; set; }
    public int CoveredCanonicalWorkCount { get; set; }
    public int MissingSourceCanonicalWorkCount { get; set; }
    public int CandidateSpanCount { get; set; }
    public int? CurrentAnalysisRunCount { get; set; }
    public int? StaleAnalysisRunCount { get; set; }
    public int? UnknownAnalysisRunCount { get; set; }
    public bool IsCorpusTruncated { get; set; }
    public bool? IsDirectCorpusTruncated { get; set; }
    public int? ClaimBridgeEligibleRunCount { get; set; }
    public int? ClaimBridgeCoveredCanonicalWorkCount { get; set; }
    public int? ClaimBridgeCandidateClaimCount { get; set; }
    public int? ClaimBridgeCandidateSpanCount { get; set; }
    public bool? IsClaimBridgeTruncated { get; set; }
    public List<AcademicEvidenceCoverageBucketDto> AnalysisLanguageCoverage { get; set; } = [];
    public List<AcademicEvidenceCoverageBucketDto>? ClaimBridgeLanguageCoverage { get; set; }
    public List<AcademicEvidenceCoverageBucketDto> SourceKindCoverage { get; set; } = [];
    public List<AcademicEvidenceSearchHitDto> Hits { get; set; } = [];
}

public sealed class AcademicEvidenceCoverageBucketDto
{
    public string Value { get; set; } = string.Empty;
    public int CanonicalWorkCount { get; set; }
}

public sealed class AcademicEvidenceSearchHitDto
{
    public int Rank { get; set; }
    public decimal Score { get; set; }
    public string EvidenceId { get; set; } = string.Empty;
    public int CanonicalWorkId { get; set; }
    public long ArticleSourceSnapshotId { get; set; }
    public long ArticleSourceSpanId { get; set; }
    public long AnalysisRunId { get; set; }
    public string SourceId { get; set; } = string.Empty;
    public int? PageNumber { get; set; }
    public int StartOffset { get; set; }
    public int EndOffset { get; set; }
    public string ExactText { get; set; } = string.Empty;
    public string AnalysisLanguage { get; set; } = string.Empty;
    public string SourceKind { get; set; } = string.Empty;
    public string ExtractionVersion { get; set; } = string.Empty;
    public string ExtractedTextHash { get; set; } = string.Empty;
    public bool IsPartial { get; set; }
    public string AnalysisFreshnessStatus { get; set; } = "Unknown";
    public List<string> AnalysisFreshnessReasons { get; set; } = ["FreshnessNotRecorded"];
    public List<AcademicEvidenceMatchProvenanceDto>? MatchProvenance { get; set; }
    public bool? IsMatchProvenanceTruncated { get; set; }
    public AcademicEvidenceSourceCoverageDto SourceCoverage { get; set; } = new();
}

public sealed class AcademicEvidenceMatchProvenanceDto
{
    public string Kind { get; set; } = string.Empty;
    public long AnalysisRunId { get; set; }
    public string Language { get; set; } = string.Empty;
    public long? CanonicalArticleClaimId { get; set; }
    public string? Section { get; set; }
}

public sealed class AcademicEvidenceSourceCoverageDto
{
    public int ProcessedChunks { get; set; }
    public int TotalChunks { get; set; }
    public int ProcessedPages { get; set; }
    public int TextBearingPages { get; set; }
    public int TotalPages { get; set; }
    public string? ScopeReason { get; set; }
}

public sealed class AcademicEvidenceSearchEvaluation
{
    public int CaseCount { get; set; }
    public int EligibleCaseCount { get; set; }
    public int MissingSourceCaseCount { get; set; }
    public int RelevantEvidenceCount { get; set; }
    public int RetrievedRelevantAtK { get; set; }
    public decimal? RecallAtK { get; set; }
    public decimal? MeanReciprocalRank { get; set; }
}
