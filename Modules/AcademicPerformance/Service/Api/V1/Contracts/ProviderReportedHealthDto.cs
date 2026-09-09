namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class ProviderReportedHealthDto
{
    public string Status { get; set; } = "Unknown";
    public string Source { get; set; } = string.Empty;
    public bool? OverallOk { get; set; } = null;
    public bool? TomcatUp { get; set; } = null;
    public bool? DbConnectionOk { get; set; } = null;
    public bool? ReadOnlyDbConnectionOk { get; set; } = null;
    public DateTime? ObservedAt { get; set; } = null;
    public DateTime? ExpiresAt { get; set; } = null;
}
