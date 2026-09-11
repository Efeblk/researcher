namespace ResearcherAnalysisService.Integrations.Gemini;

public sealed record GeminiUsageCompletion
{
    public string Outcome { get; init; } = "Unknown";
    public int? HttpStatus { get; init; }
    public string? ReturnedModel { get; init; }
    public long? PromptTokenCount { get; init; }
    public long? CachedTokenCount { get; init; }
    public long? CandidateTokenCount { get; init; }
    public long? ThoughtTokenCount { get; init; }
    public long? TotalTokenCount { get; init; }
    public string? PricingVersion { get; init; }
    public decimal? EstimatedUsd { get; init; }
}
