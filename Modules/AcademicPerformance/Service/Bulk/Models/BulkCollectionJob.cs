namespace AcademicCollectorDemo.Modules.AcademicPerformance.Bulk.Models;

public sealed class BulkCollectionJob
{
    public long Id { get; set; }
    public Guid BatchId { get; set; }
    public string SourceResearcherId { get; set; } = string.Empty;
    public string InputJson { get; set; } = string.Empty;
    public string Status { get; set; } = BulkJobStatus.Pending;
    public int Attempts { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public DateTime? StartedAt { get; set; } = null;
    public DateTime? CompletedAt { get; set; } = null;
    public int? ResearcherId { get; set; } = null;
    public string? ResultMessage { get; set; } = null;
}
