using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Orcid;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.GoogleScholar;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis.Persistence;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.TrDizin;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using System.Text.Json.Serialization;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;

public sealed class Researcher
{
    public string PersonelId { get; set; } = string.Empty;
    public string? FirstName { get; set; } = null;
    public string? LastName { get; set; } = null;
    public string? AcademicTitle { get; set; } = null;
    public string? Department { get; set; } = null;

    public string? Orcid { get; set; } = null;
    public string? ScopusId { get; set; } = null;
    public string? GoogleScholarId { get; set; } = null;
    public string? WebOfScienceResearcherId { get; set; } = null;
    public string? TcKimlikNo { get; set; } = null;
    public DateTime? LastUpdatedAt { get; set; } = null;

    public int? WosCitationCount { get; set; } = null;
    public int? WosHIndex { get; set; } = null;
    public int? WosDocumentsCount { get; set; } = null;
    public DateTime? WosMetricsUpdatedAt { get; set; } = null;

    public int? OpenAlexCitationCount { get; set; } = null;
    public int? OpenAlexHIndex { get; set; } = null;
    public int? OpenAlexI10Index { get; set; } = null;
    public int? OpenAlexDocumentsCount { get; set; } = null;
    public decimal? OpenAlexTwoYearMeanCitedness { get; set; } = null;
    public DateTime? OpenAlexMetricsUpdatedAt { get; set; } = null;

    public int? ScholarCitationCount { get; set; } = null;
    public int? ScholarHIndex { get; set; } = null;
    public int? ScholarI10Index { get; set; } = null;
    public int? ScholarDocumentsCount { get; set; } = null;
    public int? ScholarCitationCountRecent { get; set; } = null;
    public int? ScholarHIndexRecent { get; set; } = null;
    public int? ScholarI10IndexRecent { get; set; } = null;
    public int? ScholarMetricsSinceYear { get; set; } = null;
    public DateTime? ScholarMetricsUpdatedAt { get; set; } = null;

    public OrcidProfile? OrcidProfile { get; set; } = null;
    public GoogleScholarProfile? GoogleScholarProfile { get; set; } = null;
    public OpenAlexProfile? OpenAlexProfile { get; set; } = null;
    public WebOfScienceProfile? WebOfScienceProfile { get; set; } = null;
    public TrDizinProfile? TrDizinProfile { get; set; } = null;

    [JsonIgnore]
    public List<YoksisRecord>? YoksisRecords { get; set; } = null;

    [JsonIgnore]
    public List<AcademicWork>? AcademicWorks { get; set; } = null;

    [JsonIgnore]
    public List<PublicationSummary>? PublicationSummaries { get; set; } = null;

    [JsonIgnore]
    public List<PublicationDisplayApproval>? PublicationDisplayApprovals { get; set; } = null;
}
