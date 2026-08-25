namespace AcademicCollectorDemo.Modules.AcademicPerformance.Background;

public sealed class AcademicCollectionSchedulerOptions
{
    public bool Enabled { get; set; }
    public int InitialDelaySeconds { get; set; } = 60;
    public int IntervalMinutes { get; set; } = 1440;
    public int BatchSize { get; set; } = 100;
}
