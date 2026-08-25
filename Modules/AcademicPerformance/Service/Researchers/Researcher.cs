using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Orcid;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works;
using System.Text.Json.Serialization;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;

public sealed class Researcher
{
    public int Id { get; set; }

    public string? UniversityPersonnelId { get; set; } = null;
    public string? FirstName { get; set; } = null;
    public string? LastName { get; set; } = null;
    public string? AcademicTitle { get; set; } = null;
    public string? Department { get; set; } = null;

    public string? Orcid { get; set; } = null;
    public string? WebOfScienceResearcherId { get; set; } = null;
    public string? YoksisResearcherId { get; set; } = null;
    public DateTime? LastUpdatedAt { get; set; } = null;

    public OrcidProfile? OrcidProfile { get; set; } = null;
    public WebOfScienceProfile? WebOfScienceProfile { get; set; } = null;

    [JsonIgnore]
    public List<YoksisRecord>? YoksisRecords { get; set; } = null;

    [JsonIgnore]
    public List<AcademicWork>? AcademicWorks { get; set; } = null;

    [JsonIgnore]
    public List<PublicationSummary>? PublicationSummaries { get; set; } = null;

    [JsonIgnore]
    public List<PublicationDisplayApproval>? PublicationDisplayApprovals { get; set; } = null;
}
