namespace ResearcherAnalysisService.Api.V1;

public sealed record ProviderSpendingResponse(bool Available, string Currency, string Kind, DateTime? Since,
    long RequestCount, long UnknownCount, decimal? EstimatedTotalUsd,
    IReadOnlyList<ProviderSpendingItemResponse> Last3);
