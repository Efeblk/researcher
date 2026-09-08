namespace AcademicCollectorDemo.Modules.AcademicPerformance.Analysis;

public sealed class SavedResearcherAnalysis
{
    public long Id { get; set; }
    public int ResearcherId { get; set; }
    public DateTimeOffset SavedAt { get; set; }
    public string SnapshotJson { get; set; } = string.Empty;
    public string ReportJson { get; set; } = string.Empty;
}
