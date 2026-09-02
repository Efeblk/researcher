using Serenity.Services;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Collection;

public sealed class ResearcherCollectResponse : ServiceResponse
{
    public Researcher? Researcher { get; set; } = null;
    public string? DatabaseProvider { get; set; } = null;
    public bool IsSaved { get; set; }
    public List<string> Messages { get; set; } = [];
}
