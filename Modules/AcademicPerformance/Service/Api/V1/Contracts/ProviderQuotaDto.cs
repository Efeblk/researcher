namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class ProviderQuotaDto
{
    public string Source { get; set; } = string.Empty;
    public string Window { get; set; } = string.Empty;
    public string Unit { get; set; } = "requests";
    public decimal? Limit { get; set; } = null;
    public decimal? Used { get; set; } = null;
    public decimal? Remaining { get; set; } = null;
}
