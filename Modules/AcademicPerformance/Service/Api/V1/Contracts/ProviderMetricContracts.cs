namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class ProviderDecimalMetricDto
{
    public decimal? Value { get; set; }
    public string Quality { get; set; } = "Unknown";
    public string? QualityReason { get; set; }
}

public sealed class ProviderBooleanMetricDto
{
    public bool? Value { get; set; }
    public string Quality { get; set; } = "Unknown";
    public string? QualityReason { get; set; }
}