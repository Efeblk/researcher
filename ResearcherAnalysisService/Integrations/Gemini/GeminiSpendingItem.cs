namespace ResearcherAnalysisService.Integrations.Gemini;

public sealed record GeminiSpendingItem(DateTime At, string Model, decimal? EstimatedUsd);
