namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class ScopusProfileSummaryDto
{
    public string ScopusAuthorId { get; set; } = string.Empty;
    public string? DisplayName { get; set; } = null;
    public string? CurrentAffiliation { get; set; } = null;
    public int? DocumentsCount { get; set; } = null;
    public int? CollectedWorksCount { get; set; } = null;
    public int? CitationCount { get; set; } = null;
    public int? CitedByCount { get; set; } = null;
    public int? HIndex { get; set; } = null;
    public DateTime LastUpdatedAt { get; set; }
}
