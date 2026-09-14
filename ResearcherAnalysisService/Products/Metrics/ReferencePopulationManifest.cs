namespace ResearcherAnalysisService.Products.Metrics;

public sealed class ReferencePopulationManifest
{
    public long Id { get; set; }
    public string ManifestVersion { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public string CohortDefinition { get; set; } = string.Empty;
    public string EligibilityPolicyVersion { get; set; } = string.Empty;
    public string Provenance { get; set; } = string.Empty;
    public string SamplingAndCoverage { get; set; } = string.Empty;
    public int MemberCount { get; set; }
    public DateTimeOffset ImportedAt { get; set; }
    public string ImportedByActorAuditId { get; set; } = string.Empty;
    public string? Reviewer { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewMethod { get; set; }
    public bool ApprovedForInternalNormalization { get; set; }
    public List<ReferencePopulationMember> Members { get; set; } = [];
}

public sealed class ReferencePopulationMember
{
    public long Id { get; set; }
    public long ReferencePopulationManifestId { get; set; }
    public ReferencePopulationManifest? Manifest { get; set; }
    public string StableMemberId { get; set; } = string.Empty;
    public string ClassificationId { get; set; } = string.Empty;
    public int PublicationYear { get; set; }
    public string WorkType { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int CitationCount { get; set; }
}
