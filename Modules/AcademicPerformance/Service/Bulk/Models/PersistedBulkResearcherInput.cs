using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Bulk.Models;

public sealed class PersistedBulkResearcherInput
{
    public BulkResearcherInput? OriginalInput { get; set; } = null;
    public BulkResearcherInput Input { get; set; } = new();
    public List<string> Warnings { get; set; } = [];
}
