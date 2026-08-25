using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Works;

public sealed class PublicationDisplayApprovalRequest : ServiceRequest
{
    public int ResearcherId { get; set; }
    public List<int> PublicationSummaryIds { get; set; } = [];
}

public sealed class PublicationDisplayApprovalResponse : ServiceResponse
{
    public int ResearcherId { get; set; }
    public List<int> PublicationSummaryIds { get; set; } = [];
    public int ApprovedCount { get; set; }
}

public sealed class ApprovedPublicationListResponse : ServiceResponse
{
    public int ResearcherId { get; set; }
    public List<PublicationSummary> Entities { get; set; } = [];
    public int TotalCount { get; set; }
}
