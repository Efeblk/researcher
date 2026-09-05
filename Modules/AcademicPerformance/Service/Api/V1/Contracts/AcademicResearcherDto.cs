using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class AcademicResearcherDto
{
    public int Id { get; set; }
    public string? FirstName { get; set; } = null;
    public string? LastName { get; set; } = null;
    public string? AcademicTitle { get; set; } = null;
    public string? Department { get; set; } = null;
    public string? Orcid { get; set; } = null;
    public string? GoogleScholarId { get; set; } = null;
    public string? WebOfScienceResearcherId { get; set; } = null;
    public string? YoksisResearcherId { get; set; } = null;
    public DateTime? LastUpdatedAt { get; set; } = null;
    public OrcidProfileSummaryDto? OrcidProfile { get; set; } = null;
    public GoogleScholarProfileSummaryDto? GoogleScholarProfile { get; set; } = null;
    public OpenAlexProfileSummaryDto? OpenAlexProfile { get; set; } = null;
    public WebOfScienceProfileSummaryDto? WebOfScienceProfile { get; set; } = null;
}
