using System.ComponentModel.DataAnnotations;

namespace ResearcherAnalysisService.Products.Metrics;

public sealed class PublicationMetricsOptions
{
    public bool WorkerEnabled { get; set; } = true;

    [Range(1, 60)]
    public int PollSeconds { get; set; } = 5;

    [Range(1, 3600)]
    public int RetrySeconds { get; set; } = 30;

    [Range(1, 10)]
    public int MaximumAttempts { get; set; } = 3;

    [Range(1, 100)]
    public int BatchSize { get; set; } = 20;

    [Required, StringLength(100, MinimumLength = 1)]
    public string CatalogVersion { get; set; } = PublicationMetricCatalog.Version;
}
