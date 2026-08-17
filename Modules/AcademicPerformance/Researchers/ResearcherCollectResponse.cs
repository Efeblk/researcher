namespace AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;

public sealed class ResearcherCollectResponse
{
    public Researcher? Researcher { get; set; } = null;
    public string? DatabaseProvider { get; set; } = null;
    public bool IsSaved { get; set; }
    public List<string> Messages { get; set; } = [];
}
