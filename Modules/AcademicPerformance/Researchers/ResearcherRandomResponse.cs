using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;

public sealed class ResearcherRandomResponse : ServiceResponse
{
    public ResearcherSummary? Researcher { get; set; } = null;
}
public sealed class ResearcherSummary
{
    public int Id { get; set; }
    public string? UniversityPersonnelId { get; set; } = null;
    public string? FirstName { get; set; } = null;
    public string? LastName { get; set; } = null;
    public string? AcademicTitle { get; set; } = null;
    public string? Department { get; set; } = null;
    public string? Orcid { get; set; } = null;
    public DateTime? LastUpdatedAt { get; set; } = null;
    public OrcidSummary? OrcidProfile { get; set; } = null;
    public ResearcherMetricsSummary? Metrics { get; set; } = null;
}

public sealed class OrcidSummary
{
    public string? DisplayName { get; set; } = null;
    public int WorkCount { get; set; }
    public Dictionary<string, int> WorkCategories { get; set; } = [];
    public string? CurrentOrganization { get; set; } = null;
    public int EmploymentsCount { get; set; }
    public int EducationsCount { get; set; }
    public DateTime? LastUpdatedAt { get; set; } = null;
}

public sealed class ResearcherMetricsSummary
{
    public int? WorksCount { get; set; } = null;
    public int? CitedByCount { get; set; } = null;
    public int? HIndex { get; set; } = null;
    public int? I10Index { get; set; } = null;
    public string? Source { get; set; } = null;
    public DateTime? UpdatedAt { get; set; } = null;
}
