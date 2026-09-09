namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class ProviderQuotaDto
{
    public string Source { get; set; } = string.Empty;
    public string Window { get; set; } = string.Empty;
    public string Unit { get; set; } = "requests";
    public decimal? Limit { get; set; } = null;
    public decimal? Used { get; set; } = null;
    public decimal? Remaining { get; set; } = null;
    public string Availability { get; set; } = "Available";
    public string? Scope { get; set; } = null;
    public string? ValueKind { get; set; } = null;
    public string? SourceFields { get; set; } = null;
    public DateTime? ObservedAt { get; set; } = null;
    public DateTime? ExpiresAt { get; set; } = null;
    public DateTime? ResetsAt { get; set; } = null;
    public DateTime? SubscriptionPeriodEndsAt { get; set; } = null;
}
