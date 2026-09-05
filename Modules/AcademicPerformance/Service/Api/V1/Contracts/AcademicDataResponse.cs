using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class AcademicDataResponse : ServiceResponse
{
    public AcademicResearcherDto? Researcher { get; set; } = null;
    public bool IsSaved { get; set; }
    public int PublicationCount { get; set; }
    public string? DatabaseProvider { get; set; } = null;
    public DateTime CollectedAt { get; set; }
    public List<string> Messages { get; set; } = [];
}
