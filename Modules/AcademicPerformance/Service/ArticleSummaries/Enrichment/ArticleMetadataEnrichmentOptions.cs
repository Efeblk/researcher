using System.ComponentModel.DataAnnotations;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries.Enrichment;

public sealed class ArticleMetadataEnrichmentOptions
{
    [Range(1, 10_080)]
    public int PositiveCacheMinutes { get; set; } = 1_440;

    [Range(1, 1_440)]
    public int NegativeCacheMinutes { get; set; } = 60;

    [Range(1, 60)]
    public int RequestTimeoutSeconds { get; set; } = 15;

    [Range(1_024, 16 * 1_024 * 1_024)]
    public long MaximumResponseBytes { get; set; } = 4L * 1_024 * 1_024;

    [Range(1, 32)]
    public int MaximumCandidates { get; set; } = 16;
}
