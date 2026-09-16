namespace AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Metrics;

public sealed class ResearcherMetricsProgress
{
    public string Type { get; set; } = "progress";
    public string Stage { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public double? ElapsedSeconds { get; set; }
    public double? LastProgressElapsedSeconds { get; set; }
    public Api.V1.Contracts.ResearcherMetricsResponse? Result { get; set; }
}
