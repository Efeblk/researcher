namespace ResearcherAnalysisService.Integrations.Gemini;

public sealed record GeminiSpendingStatus
{
    public bool Available { get; init; }
    public string Currency { get; init; } = "USD";
    public string Kind { get; init; } = "paidStandardEstimate";
    public DateTime? Since { get; init; }
    public long RequestCount { get; init; }
    public long UnknownCount { get; init; }
    public decimal? EstimatedTotalUsd { get; init; }
    public IReadOnlyList<GeminiSpendingItem> Last3 { get; init; } = [];
}
