using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class AcademicPublicationSelectionRequest : ServiceRequest
{
    public int ResearcherId { get; set; }
    public List<int> PublicationIds { get; set; } = [];
}
