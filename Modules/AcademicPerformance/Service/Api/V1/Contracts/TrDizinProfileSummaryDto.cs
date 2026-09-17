namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class TrDizinProfileSummaryDto
{
    public string Orcid { get; set; } = string.Empty;
    public long AuthorId { get; set; }
    public string? DisplayName { get; set; } = null;
    public int? PublicationCount { get; set; } = null;
    public int? CitationCount { get; set; } = null;
    public int ProjectCandidateCount { get; set; }
    public int ProjectMatchedCount { get; set; }
    public int ProjectUnmatchedCount { get; set; }
    public bool ProjectSearchComplete { get; set; }
    public List<TrDizinProjectDto> Projects { get; set; } = [];
    public DateTime LastUpdatedAt { get; set; }
}

public sealed class TrDizinProjectDto
{
    public string Id { get; set; } = string.Empty;
    public string? ProjectNumber { get; set; } = null;
    public string? Title { get; set; } = null;
    public string? StartedDate { get; set; } = null;
    public string? EndDate { get; set; } = null;
    public string? ProjectGroup { get; set; } = null;
    public string? ResearchersJson { get; set; } = null;
    public string? Duty { get; set; } = null;
    public string? AbstractsJson { get; set; } = null;
    public string? KeywordsJson { get; set; } = null;
    public string? OutputsJson { get; set; } = null;
    public string? AttachmentsJson { get; set; } = null;
}
