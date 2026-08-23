using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;

public sealed class ResearcherCollectRequest : ServiceRequest
{
    public List<string>? Identifiers { get; set; } = null;
}
