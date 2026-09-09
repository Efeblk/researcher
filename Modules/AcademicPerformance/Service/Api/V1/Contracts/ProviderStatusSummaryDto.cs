namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class ProviderStatusSummaryDto
{
    public string Provider { get; set; } = string.Empty;
    public string Health { get; set; } = "Unknown";
    public List<ProviderQuotaSummaryDto> Quotas { get; set; } = [];
}
