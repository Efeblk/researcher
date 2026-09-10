using Serenity.Services;
using System.Text.Json.Serialization;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.WebClient.Contracts;

public sealed class ResearcherCollectionRequest : ServiceRequest
{
    [JsonPropertyName("ORCID"), Newtonsoft.Json.JsonProperty("ORCID")]
    public string? Orcid { get; set; } = null;
    [JsonPropertyName("ScholarID"), Newtonsoft.Json.JsonProperty("ScholarID")]
    public string? GoogleScholarId { get; set; } = null;
    [JsonPropertyName("ResearcherID"), Newtonsoft.Json.JsonProperty("ResearcherID")]
    public string? WebOfScienceResearcherId { get; set; } = null;
}
