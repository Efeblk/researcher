using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class AcademicPublicationListResponse : ServiceResponse
{
    public int ResearcherId { get; set; }
    public List<AcademicPublicationDto> Entities { get; set; } = [];
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
}
