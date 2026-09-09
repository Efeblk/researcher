using Serenity.Services;
using System.Text.Json.Serialization;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class AcademicResearcherDto
{
    [JsonPropertyName("PersonelID"), Newtonsoft.Json.JsonProperty("PersonelID")]
    public string PersonelId { get; set; } = string.Empty;
    public string? FirstName { get; set; } = null;
    public string? LastName { get; set; } = null;
    public string? AcademicTitle { get; set; } = null;
    public string? Department { get; set; } = null;
    [JsonPropertyName("ORCID"), Newtonsoft.Json.JsonProperty("ORCID")]
    public string? Orcid { get; set; } = null;
    [JsonPropertyName("ScholarID"), Newtonsoft.Json.JsonProperty("ScholarID")]
    public string? GoogleScholarId { get; set; } = null;
    [JsonPropertyName("ResearcherID"), Newtonsoft.Json.JsonProperty("ResearcherID")]
    public string? WebOfScienceResearcherId { get; set; } = null;
    [JsonPropertyName("ScopusID"), Newtonsoft.Json.JsonProperty("ScopusID")]
    public string? ScopusId { get; set; } = null;
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
    public OrcidProfileSummaryDto? OrcidProfile { get; set; } = null;
    public GoogleScholarProfileSummaryDto? GoogleScholarProfile { get; set; } = null;
    public OpenAlexProfileSummaryDto? OpenAlexProfile { get; set; } = null;
    public WebOfScienceProfileSummaryDto? WebOfScienceProfile { get; set; } = null;
}
