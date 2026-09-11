namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

using System.Text.Json.Serialization;

public sealed class ProviderStatusSummaryDto
{
    public string Provider { get; set; } = string.Empty;
    public string Health { get; set; } = "Unknown";
    public List<ProviderQuotaSummaryDto> Quotas { get; set; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProviderSpendingSummaryDto? Spending { get; set; } = null;
}
