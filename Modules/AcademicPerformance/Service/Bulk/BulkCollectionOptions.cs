namespace AcademicCollectorDemo.Modules.AcademicPerformance.Bulk;

public sealed class BulkCollectionOptions
{
    public bool WorkerEnabled { get; set; }
    public int MaximumBatchSize { get; set; } = 10000;
    public int MaximumAttempts { get; set; } = 3;
    public int PollSeconds { get; set; } = 5;
    public int RetrySeconds { get; set; } = 60;
}
