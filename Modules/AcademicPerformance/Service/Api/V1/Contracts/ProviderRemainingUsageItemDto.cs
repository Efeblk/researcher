namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class ProviderRemainingUsageItemDto
{
    public string Status { get; set; } = "Unknown";
    public decimal? Value { get; set; } = null;
    public string Unit { get; set; } = "unknown";
    public string Window { get; set; } = "unspecified";
    public string Source { get; set; } = string.Empty;
    public string? Scope { get; set; } = null;
    public DateTime? ObservedAt { get; set; } = null;
    public DateTime? ExpiresAt { get; set; } = null;
    public DateTime? ResetsAt { get; set; } = null;
    public string Reason { get; set; } = string.Empty;
}
