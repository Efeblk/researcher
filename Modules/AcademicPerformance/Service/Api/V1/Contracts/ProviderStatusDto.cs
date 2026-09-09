namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class ProviderStatusDto
{
    public string Provider { get; set; } = string.Empty;
    public string Status { get; set; } = "Unknown";
    public string CheckKind { get; set; } = "ApiRequest";
    public DateTime? CheckedAt { get; set; } = null;
    public int? HttpStatusCode { get; set; } = null;
    public long? LatencyMilliseconds { get; set; } = null;
    public string? Message { get; set; } = null;
    public DateTime? RetryAt { get; set; } = null;
    public ProviderTransportDto Transport { get; set; } = new();
    public ProviderReportedHealthDto? ReportedHealth { get; set; } = null;
    public string QuotaAvailability { get; set; } = "Unknown";
    public string? QuotaSource { get; set; } = null;
    public string CacheScope { get; set; } = "Process";
    public LocalProviderBudgetDto? LocalBudget { get; set; } = null;
    public List<ProviderQuotaDto> ProviderQuotas { get; set; } = [];
}
