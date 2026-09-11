namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class ProviderSpendingItemSummaryDto
{
    public DateTime At { get; set; }
    public string Model { get; set; } = string.Empty;
    public decimal? EstimatedUsd { get; set; }
}
