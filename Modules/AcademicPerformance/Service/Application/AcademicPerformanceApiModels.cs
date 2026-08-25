using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Application;

public sealed class AcademicDataCollectRequest : ServiceRequest
{
    public string? Orcid { get; set; } = null;
    public string? WebOfScienceResearcherId { get; set; } = null;
}

public sealed class AcademicResearcherRequest : ServiceRequest
{
    public int? ResearcherId { get; set; } = null;
    public string? Orcid { get; set; } = null;
    public string? WebOfScienceResearcherId { get; set; } = null;
}

public sealed class AcademicPublicationListRequest : ServiceRequest
{
    public int? ResearcherId { get; set; } = null;
    public string? Orcid { get; set; } = null;
    public string? WebOfScienceResearcherId { get; set; } = null;
    public bool ApprovedOnly { get; set; }
    public string? SearchText { get; set; } = null;
    public int Skip { get; set; }
    public int Take { get; set; } = 100;
}

public sealed class AcademicDataResponse : ServiceResponse
{
    public AcademicResearcherDto? Researcher { get; set; } = null;
    public bool IsSaved { get; set; }
    public int PublicationCount { get; set; }
    public string? DatabaseProvider { get; set; } = null;
    public DateTime CollectedAt { get; set; }
    public List<string> Messages { get; set; } = [];
}

public sealed class AcademicResearcherDto
{
    public int Id { get; set; }
    public string? FirstName { get; set; } = null;
    public string? LastName { get; set; } = null;
    public string? AcademicTitle { get; set; } = null;
    public string? Department { get; set; } = null;
    public string? Orcid { get; set; } = null;
    public string? WebOfScienceResearcherId { get; set; } = null;
    public string? YoksisResearcherId { get; set; } = null;
    public DateTime? LastUpdatedAt { get; set; } = null;
    public OrcidProfileSummaryDto? OrcidProfile { get; set; } = null;
    public WebOfScienceProfileSummaryDto? WebOfScienceProfile { get; set; } = null;
}

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

public sealed class AcademicPublicationListResponse : ServiceResponse
{
    public int ResearcherId { get; set; }
    public List<AcademicPublicationDto> Entities { get; set; } = [];
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
}

public sealed class AcademicPublicationSelectionRequest : ServiceRequest
{
    public int ResearcherId { get; set; }
    public List<int> PublicationIds { get; set; } = [];
}

public sealed class AcademicPublicationSelectionResponse : ServiceResponse
{
    public int ResearcherId { get; set; }
    public List<int> PublicationIds { get; set; } = [];
    public int ApprovedCount { get; set; }
}

public sealed class AcademicPublicationDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int? PublicationYear { get; set; } = null;
    public DateTime? PublicationDate { get; set; } = null;
    public string? Doi { get; set; } = null;
    public string Category { get; set; } = string.Empty;
    public string? Authors { get; set; } = null;
    public string? Publication { get; set; } = null;
    public int? CitedByCount { get; set; } = null;
    public string? PublicationUrl { get; set; } = null;
    public string Sources { get; set; } = string.Empty;
    public bool IsApprovedForDisplay { get; set; }
}
