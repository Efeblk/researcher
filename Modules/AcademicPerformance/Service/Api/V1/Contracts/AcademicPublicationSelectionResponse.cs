using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class AcademicPublicationSelectionResponse : ServiceResponse
{
    public int ResearcherId { get; set; }
    public List<int> PublicationIds { get; set; } = [];
    public int ApprovedCount { get; set; }
}
