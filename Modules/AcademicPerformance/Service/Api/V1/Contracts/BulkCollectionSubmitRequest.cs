using AcademicCollectorDemo.Modules.AcademicPerformance.Bulk.Models;
using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class BulkCollectionSubmitRequest : ServiceRequest
{
    public Guid BatchId { get; set; }
    public List<BulkResearcherInput> Researchers { get; set; } = [];
}
