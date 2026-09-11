namespace ResearcherAnalysisService.Api.V1;

public sealed record ProviderStatusResponse(string Provider, string Health, IReadOnlyList<object> Quotas,
    ProviderSpendingResponse? Spending = null);
