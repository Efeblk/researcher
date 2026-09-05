using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.WebClient.Contracts;

public sealed class ApprovedPublicationListResponse : ServiceResponse
{
    public int ResearcherId { get; set; }
    public List<PublicationSummary> Entities { get; set; } = [];
    public int TotalCount { get; set; }
}
