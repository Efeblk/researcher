namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class ProviderQuotaSummaryDto
{
    public decimal? Limit { get; set; } = null;
    public decimal? Remaining { get; set; } = null;
    public string Unit { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;
    public DateTime? ResetsAt { get; set; } = null;
}
