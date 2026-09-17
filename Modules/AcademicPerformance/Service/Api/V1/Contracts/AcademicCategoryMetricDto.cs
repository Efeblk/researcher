namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class AcademicCategoryMetricDto
{
    public string CategoryName { get; set; } = string.Empty;
    public string OperationName { get; set; } = string.Empty;
    public int RecordCount { get; set; }
}
