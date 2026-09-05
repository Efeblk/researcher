using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class AcademicResearcherRequest : ServiceRequest
{
    public int? ResearcherId { get; set; } = null;
    public string? Orcid { get; set; } = null;
    public string? GoogleScholarId { get; set; } = null;
    public string? WebOfScienceResearcherId { get; set; } = null;
}
