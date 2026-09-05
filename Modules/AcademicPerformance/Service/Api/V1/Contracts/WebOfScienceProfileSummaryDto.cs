using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class WebOfScienceProfileSummaryDto
{
    public string? DisplayName { get; set; } = null;
    public string? PrimaryOrganization { get; set; } = null;
    public int? HIndex { get; set; } = null;
    public int DocumentsCount { get; set; }
    public int? TotalTimesCited { get; set; } = null;
    public int? TotalCitingPublications { get; set; } = null;
    public int PeerReviewsCount { get; set; }
    public DateTime LastUpdatedAt { get; set; }
}
