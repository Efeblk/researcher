using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Collection;

public sealed class ResearcherCollectRequest : ServiceRequest
{
    public string? PersonelId { get; set; } = null;
    public string? ScopusId { get; set; } = null;
    public List<string>? Identifiers { get; set; } = null;
}
