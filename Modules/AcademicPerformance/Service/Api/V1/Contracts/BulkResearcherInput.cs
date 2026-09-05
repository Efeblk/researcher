namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class BulkResearcherInput
{
    public string SourceResearcherId { get; set; } = string.Empty;
    public string? Orcid { get; set; } = null;
    public string? GoogleScholarId { get; set; } = null;
    public string? WebOfScienceId { get; set; } = null;
}
