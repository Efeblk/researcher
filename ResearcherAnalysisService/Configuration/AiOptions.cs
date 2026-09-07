using System.ComponentModel.DataAnnotations;

namespace ResearcherAnalysisService.Configuration;

public sealed class AiOptions
{
    public string? ApiKey { get; set; } = null;
    public string? Model { get; set; } = null;

    [Range(5, 300)]
    public int TimeoutSeconds { get; set; } = 90;

    [Range(512, 16000)]
    public int MaxOutputTokens { get; set; } = 4000;
}
