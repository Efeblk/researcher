using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class AcademicPublicationDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int? PublicationYear { get; set; } = null;
    public string? Doi { get; set; } = null;
    public string Category { get; set; } = string.Empty;
    public string? Authors { get; set; } = null;
    public string? Publication { get; set; } = null;
    public string? PublicationUrl { get; set; } = null;
    public string Sources { get; set; } = string.Empty;
    public bool IsApprovedForDisplay { get; set; }
}
