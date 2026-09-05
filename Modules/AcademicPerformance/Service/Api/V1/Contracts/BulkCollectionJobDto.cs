namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class BulkCollectionJobDto
{
    public long Id { get; set; }
    public string SourceResearcherId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public int? ResearcherId { get; set; } = null;
    public DateTime NextAttemptAt { get; set; }
    public string? Message { get; set; } = null;
}
