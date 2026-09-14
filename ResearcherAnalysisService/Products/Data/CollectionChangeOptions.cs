using System.ComponentModel.DataAnnotations;

namespace ResearcherAnalysisService.Products.Data;

public sealed class CollectionChangeOptions
{
    public bool WorkerEnabled { get; set; } = true;

    [Range(1, 60)]
    public int PollSeconds { get; set; } = 5;

    [Range(1, 500)]
    public int BatchSize { get; set; } = 100;
}
