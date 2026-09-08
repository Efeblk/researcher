using System.ComponentModel.DataAnnotations;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Analysis;

public sealed class AnalysisServiceOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5011/";
    public string? ApiKey { get; set; } = null;
    [RegularExpression("^(en|tr)$")]
    public string Language { get; set; } = "en";
    [Range(1, 100)]
    public int MaximumPublications { get; set; } = 10;
    [Range(200, 3000)]
    public int MaximumTextBytes { get; set; } = 2500;
    [Range(1, 600)]
    public int TimeoutSeconds { get; set; } = 240;
}
