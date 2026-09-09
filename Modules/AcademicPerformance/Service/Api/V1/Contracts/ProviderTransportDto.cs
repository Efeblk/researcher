namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class ProviderTransportDto
{
    public string Status { get; set; } = "Unknown";
    public DateTime? ObservedAt { get; set; } = null;
    public DateTime? ExpiresAt { get; set; } = null;
    public int? HttpStatusCode { get; set; } = null;
    public long? LatencyMilliseconds { get; set; } = null;
}
