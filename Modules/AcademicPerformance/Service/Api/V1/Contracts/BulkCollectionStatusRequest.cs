using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class BulkCollectionStatusRequest : ServiceRequest
{
    public Guid BatchId { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; } = 100;
}
