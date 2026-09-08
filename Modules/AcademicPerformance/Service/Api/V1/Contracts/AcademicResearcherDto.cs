using Serenity.Services;
using System.Text.Json.Serialization;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class AcademicResearcherDto
{
    public int Id { get; set; }
    [JsonPropertyName("PersonelID"), Newtonsoft.Json.JsonProperty("PersonelID")]
    public string? PersonelId { get; set; } = null;
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
    public string? YoksisResearcherId { get; set; } = null;
    public DateTime? LastUpdatedAt { get; set; } = null;
    public OrcidProfileSummaryDto? OrcidProfile { get; set; } = null;
    public GoogleScholarProfileSummaryDto? GoogleScholarProfile { get; set; } = null;
    public OpenAlexProfileSummaryDto? OpenAlexProfile { get; set; } = null;
    public WebOfScienceProfileSummaryDto? WebOfScienceProfile { get; set; } = null;
}
