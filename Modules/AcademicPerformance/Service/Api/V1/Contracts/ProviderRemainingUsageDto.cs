namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class ProviderRemainingUsageDto
{
    public string Status { get; set; } = "Unknown";
    public string Reason { get; set; } = "No verified current provider remaining balance is available.";
    public List<ProviderRemainingUsageItemDto> Items { get; set; } = [];
}
