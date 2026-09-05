namespace AcademicCollectorDemo.Modules.AcademicPerformance.Bulk.Models;

public sealed class BulkCollectionBatch
{
    public Guid Id { get; set; }
    public string InputHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
