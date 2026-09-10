using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.WebClient.Contracts;

public sealed class YoksisCollectionRequest : ServiceRequest
{
    public string? TcKimlikNo { get; set; } = null;
    public DateTime? UpdatedAfter { get; set; } = null;
    public bool IncludeRecords { get; set; }
    public bool IncludeRawResponses { get; set; }
}
