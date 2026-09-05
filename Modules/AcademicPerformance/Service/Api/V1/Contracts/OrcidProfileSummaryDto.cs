using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class OrcidProfileSummaryDto
{
    public string? DisplayName { get; set; } = null;
    public string? CurrentOrganization { get; set; } = null;
    public string? CurrentDepartment { get; set; } = null;
    public string? CurrentRoleTitle { get; set; } = null;
    public int WorksCount { get; set; }
    public int EmploymentsCount { get; set; }
    public int EducationsCount { get; set; }
    public int FundingsCount { get; set; }
    public int PeerReviewsCount { get; set; }
    public DateTime? RecordLastModifiedAt { get; set; } = null;
    public DateTime LastUpdatedAt { get; set; }
}
