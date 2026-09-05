using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class AcademicPublicationListRequest : ServiceRequest
{
    public int? ResearcherId { get; set; } = null;
    public string? Orcid { get; set; } = null;
    public string? GoogleScholarId { get; set; } = null;
    public string? WebOfScienceResearcherId { get; set; } = null;
    public bool ApprovedOnly { get; set; }
    public string? SearchText { get; set; } = null;
    public int Skip { get; set; }
    public int Take { get; set; } = 100;
}
