namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class TrDizinProfileSummaryDto
{
    public string Orcid { get; set; } = string.Empty;
    public long AuthorId { get; set; }
    public string? DisplayName { get; set; } = null;
    public int? PublicationCount { get; set; } = null;
    public int? CitationCount { get; set; } = null;
    public DateTime LastUpdatedAt { get; set; }
}
