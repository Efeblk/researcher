using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ResearcherAnalysisService.Products.Api.Contracts;

public sealed class ReferencePopulationImportRequest
{
    [Required, StringLength(200)]
    [JsonPropertyName("PersonelID")]
    public string PersonelId { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string ManifestVersion { get; set; } = string.Empty;

    [Required, StringLength(4000)]
    public string CohortDefinition { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string EligibilityPolicyVersion { get; set; } = string.Empty;

    [Required, StringLength(4000)]
    public string Provenance { get; set; } = string.Empty;

    [Required, StringLength(4000)]
    public string SamplingAndCoverage { get; set; } = string.Empty;

    public ReferencePopulationReviewDto? Review { get; set; }

    [Required, MinLength(1), MaxLength(100000)]
    public List<ReferencePopulationMemberDto> Members { get; set; } = [];
}

public sealed class ReferencePopulationManifestRequest
{
    [Required, StringLength(200)]
    [JsonPropertyName("PersonelID")]
    public string PersonelId { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string ManifestVersion { get; set; } = string.Empty;
}

public sealed class ReferencePopulationReviewDto
{
    [Required, StringLength(200)]
    public string Reviewer { get; set; } = string.Empty;

    public DateTimeOffset ReviewedAt { get; set; }

    [Required, StringLength(4000)]
    public string Method { get; set; } = string.Empty;

    public bool ApprovedForInternalNormalization { get; set; }
}

public sealed class ReferencePopulationMemberDto
{
    [Required, StringLength(200)]
    public string StableMemberId { get; set; } = string.Empty;

    [Required, StringLength(200)]
    public string ClassificationId { get; set; } = string.Empty;

    [Range(1, 9999)]
    public int PublicationYear { get; set; }

    [Required, StringLength(100)]
    public string WorkType { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string Category { get; set; } = string.Empty;

    [Range(0, int.MaxValue)]
    public int CitationCount { get; set; }
}

public sealed class ReferencePopulationManifestResponse
{
    public long Id { get; set; }
    public string ManifestVersion { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public int MemberCount { get; set; }
    public string MechanicalValidationStatus { get; set; } = string.Empty;
    public string ReviewStatus { get; set; } = string.Empty;
    public bool ReadyForInternalNormalization { get; set; }
    public string ScientificValidationStatus { get; set; } = "NotEstablishedBySoftware";
    public string Reason { get; set; } = string.Empty;
}
