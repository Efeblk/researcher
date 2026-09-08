using Serenity.Services;
using System.Text.Json.Serialization;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class AcademicPublicationListRequest : ServiceRequest
{
    public int? Id { get; set; } = null;
    [JsonPropertyName("PersonelID"), Newtonsoft.Json.JsonProperty("PersonelID")]
    public string? PersonelId { get; set; } = null;
    [JsonPropertyName("ORCID"), Newtonsoft.Json.JsonProperty("ORCID")]
    public string? Orcid { get; set; } = null;
    [JsonPropertyName("ScholarID"), Newtonsoft.Json.JsonProperty("ScholarID")]
    public string? GoogleScholarId { get; set; } = null;
    [JsonPropertyName("ResearcherID"), Newtonsoft.Json.JsonProperty("ResearcherID")]
    public string? WebOfScienceResearcherId { get; set; } = null;
    public bool ApprovedOnly { get; set; }
    public string? SearchText { get; set; } = null;
    public int Skip { get; set; }
    public int Take { get; set; } = 100;
}
