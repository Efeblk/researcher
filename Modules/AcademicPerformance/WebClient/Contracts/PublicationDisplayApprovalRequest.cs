using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.WebClient.Contracts;

public sealed class PublicationDisplayApprovalRequest : ServiceRequest
{
    public int ResearcherId { get; set; }
    public List<int> PublicationSummaryIds { get; set; } = [];
}
