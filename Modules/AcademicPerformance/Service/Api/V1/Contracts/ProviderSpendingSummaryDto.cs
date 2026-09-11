namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class ProviderSpendingSummaryDto
{
    public bool Available { get; set; }
    public string Currency { get; set; } = "USD";
    public string Kind { get; set; } = "paidStandardEstimate";
    public DateTime? Since { get; set; }
    public long RequestCount { get; set; }
    public long UnknownCount { get; set; }
    public decimal? EstimatedTotalUsd { get; set; }
    public List<ProviderSpendingItemSummaryDto> Last3 { get; set; } = [];
}
