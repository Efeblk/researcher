namespace AcademicCollectorDemo.Modules.AcademicPerformance.Bulk;

public sealed class BulkRequestException(string message) : ArgumentException(message);
