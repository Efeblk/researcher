using System.ComponentModel.DataAnnotations;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.SemanticScholar;

public sealed class SemanticScholarOptions
{
    public string ApiBaseUrl { get; set; } = "https://api.semanticscholar.org/graph/v1";
    public string? ApiKey { get; set; } = null;
    [Range(1, 1000)] public int CitationPageSize { get; set; } = 100;
    [Range(0, 10000)] public int MaximumCitationsPerPaper { get; set; } = 500;
    [Range(1, 100)] public int MaximumPapersPerRun { get; set; } = 10;
    [Range(1, 8760)] public int CacheMaxAgeHours { get; set; } = 720;
}
