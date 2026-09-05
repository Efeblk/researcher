using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class BulkCollectionStatusResponse : ServiceResponse
{
    public Guid BatchId { get; set; }
    public bool WorkerEnabled { get; set; }
    public bool IsComplete { get; set; }
    public Dictionary<string, int> Counts { get; set; } = [];
    public List<BulkCollectionJobDto> Jobs { get; set; } = [];
}
