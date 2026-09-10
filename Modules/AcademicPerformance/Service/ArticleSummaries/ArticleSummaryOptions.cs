using System.ComponentModel.DataAnnotations;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;

public sealed class ArticleSummaryOptions
{
    [Range(1, 10)] public int MaximumRedirects { get; set; } = 4;
    [Range(65536, 52428800)] public int MaximumDownloadBytes { get; set; } = 12 * 1024 * 1024;
    [Range(1, 200)] public int MaximumPages { get; set; } = 40;
    [Range(10000, 1000000)] public int MaximumExtractedCharacters { get; set; } = 160000;
    [Range(5, 120)] public int FetchTimeoutSeconds { get; set; } = 30;
    [Range(30, 1800)] public int TotalTimeoutSeconds { get; set; } = 600;
}
