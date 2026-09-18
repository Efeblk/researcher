namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class AcademicActivityDto
{
    public string Category { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string? Title { get; set; } = null;
    public string? Date { get; set; } = null;
    public string? Organization { get; set; } = null;
    public string? Role { get; set; } = null;
    public string? SourceId { get; set; } = null;
}
