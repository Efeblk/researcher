using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis;

public sealed class YoksisCollectResponse : ServiceResponse
{
    public int? ResearcherId { get; set; } = null;
    public string? ResearcherDisplayName { get; set; } = null;
    public bool IsSaved { get; set; }
    public int YoksisRecordCount { get; set; }
    public int YoksisPublicationCount { get; set; }
    public int PublicationSummaryCount { get; set; }
    public DateTime CollectedAt { get; set; }
    public int SuccessfulCategoryCount { get; set; }
    public int FailedCategoryCount { get; set; }
    public int TotalRecordCount { get; set; }
    public List<string> Messages { get; set; } = [];
    public List<YoksisOperationResult> Categories { get; set; } = [];
}
