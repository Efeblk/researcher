using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.WebClient.Contracts;

public sealed class PublicationDisplayApprovalRequest : ServiceRequest
{
    public List<int> PublicationSummaryIds { get; set; } = [];
}
