using ResearcherAnalysisService.SourceData.Researchers;

namespace ResearcherAnalysisService.Products.Metrics;

public sealed class PublicationMetricSnapshot
{
    public long Id { get; set; }
    public string PersonelId { get; set; } = string.Empty;
    public Researcher? Researcher { get; set; } = null;
    public string CatalogVersion { get; set; } = string.Empty;
    public long SourceRevision { get; set; }
    public int ComputationYear { get; set; }
    public DateTime ComputedAt { get; set; }
    public string ResultJson { get; set; } = string.Empty;
    public int CanonicalWorkCount { get; set; }
    public int ProviderObservationCount { get; set; }
    public int UnmappedAcademicWorkCount { get; set; }
    public List<PublicationMetricProviderSnapshot> ProviderMetrics { get; set; } = [];
}

public sealed class PublicationMetricProviderSnapshot
{
    public long Id { get; set; }
    public long SnapshotId { get; set; }
    public PublicationMetricSnapshot? Snapshot { get; set; } = null;
    public string Provider { get; set; } = string.Empty;
    public DateTime? SourceUpdatedAt { get; set; } = null;
    public int? CitationCount { get; set; } = null;
    public int? HIndex { get; set; } = null;
    public int? DocumentCount { get; set; } = null;
    public int? I10Index { get; set; } = null;
    public int? CitationCountRecent { get; set; } = null;
    public int? HIndexRecent { get; set; } = null;
    public int? I10IndexRecent { get; set; } = null;
    public int? MetricsSinceYear { get; set; } = null;
    public decimal? TwoYearMeanCitedness { get; set; } = null;
    public bool HasInvalidValues { get; set; }
    public string FieldMetadataJson { get; set; } = string.Empty;
}
