using Serenity.Services;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Collection;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class AcademicDataResponse : ServiceResponse
{
    public AcademicResearcherDto? Researcher { get; set; } = null;
    public bool IsSaved { get; set; }
    public string? FailureCode { get; set; } = null;
    public int YoksisFailedCategoryCount { get; set; }
    public int PublicationCount { get; set; }
    public string? DatabaseProvider { get; set; } = null;
    public DateTime CollectedAt { get; set; }
    public List<string> Messages { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public List<ProviderCollectionFeedback> ProviderFeedback { get; set; } = [];
}
