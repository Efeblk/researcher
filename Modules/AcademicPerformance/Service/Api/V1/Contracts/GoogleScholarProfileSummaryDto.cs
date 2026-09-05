using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class GoogleScholarProfileSummaryDto
{
    public string? DisplayName { get; set; } = null;
    public string? Affiliations { get; set; } = null;
    public string? University { get; set; } = null;
    public string? ProfileUrl { get; set; } = null;
    public int? CitationCount { get; set; } = null;
    public int? CitationCountRecent { get; set; } = null;
    public int? HIndex { get; set; } = null;
    public int? HIndexRecent { get; set; } = null;
    public int? I10Index { get; set; } = null;
    public int? I10IndexRecent { get; set; } = null;
    public int? MetricsSinceYear { get; set; } = null;
    public int DocumentsCount { get; set; }
    public DateTime LastUpdatedAt { get; set; }
}
