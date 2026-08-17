using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.GoogleScholar;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Scopus;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;

public sealed class Researcher
{
    public int Id { get; set; }

    public string? UniversityPersonnelId { get; set; } = null;
    public string? FirstName { get; set; } = null;
    public string? LastName { get; set; } = null;
    public string? AcademicTitle { get; set; } = null;
    public string? Department { get; set; } = null;

    public string? WebOfScienceResearcherId { get; set; } = null;
    public string? ScopusAuthorId { get; set; } = null;
    public string? Orcid { get; set; } = null;
    public string? GoogleScholarId { get; set; } = null;
    public DateTime? LastUpdatedAt { get; set; } = null;

    public OpenAlexData? OpenAlex { get; set; } = null;
    public GoogleScholarData? GoogleScholar { get; set; } = null;
    public ScopusData? Scopus { get; set; } = null;
    public WebOfScienceData? WebOfScience { get; set; } = null;
}
