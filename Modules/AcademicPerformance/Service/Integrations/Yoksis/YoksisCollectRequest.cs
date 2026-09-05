using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis;

public sealed class YoksisCollectRequest : ServiceRequest
{
    public int? ResearcherId { get; set; } = null;
    public string? TcKimlikNo { get; set; } = null;
    public DateTime? UpdatedAfter { get; set; } = null;
    public bool IncludeRecords { get; set; }
    public bool IncludeRawResponses { get; set; }
}
