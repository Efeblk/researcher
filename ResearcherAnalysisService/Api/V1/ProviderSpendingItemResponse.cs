namespace ResearcherAnalysisService.Api.V1;

public sealed record ProviderSpendingItemResponse(DateTime At, string Model, decimal? EstimatedUsd);
