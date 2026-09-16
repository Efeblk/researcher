namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis.Collection;

public sealed class YoksisCollectionProgress
{
    public string Type { get; set; } = "progress";
    public string Stage { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? CategoryName { get; set; }
    public string? OperationName { get; set; }
    public int? Current { get; set; }
    public int? Total { get; set; }
    public int? RecordCount { get; set; }
    public double? ElapsedSeconds { get; set; }
    public double? LastProgressElapsedSeconds { get; set; }
    public YoksisCollectResponse? Result { get; set; }
}
