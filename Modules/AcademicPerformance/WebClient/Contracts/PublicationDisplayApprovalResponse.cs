using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.WebClient.Contracts;

public sealed class PublicationDisplayApprovalResponse : ServiceResponse
{
    public int ResearcherId { get; set; }
    public List<int> PublicationSummaryIds { get; set; } = [];
    public int ApprovedCount { get; set; }
}
