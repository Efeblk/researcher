namespace AcademicCollectorDemo.Modules.AcademicPerformance.Bulk.Models;

public static class BulkJobStatus
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string RetryWaiting = "RetryWaiting";
    public const string Succeeded = "Succeeded";
    public const string Partial = "Partial";
    public const string Failed = "Failed";
    public const string Rejected = "Rejected";
}
