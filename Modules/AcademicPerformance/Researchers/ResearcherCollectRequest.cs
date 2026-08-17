namespace AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;

public sealed class ResearcherCollectRequest
{
    public List<string>? Identifiers { get; set; } = null;
    public bool UseTestIdentifiers { get; set; }
}
