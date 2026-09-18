using Serenity.Services;
using System.Text.Json.Serialization;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class AcademicResearcherRequest : ServiceRequest
{
    [JsonPropertyName("PersonelID"), Newtonsoft.Json.JsonProperty("PersonelID")]
    public string? PersonelId { get; set; } = null;
    [JsonPropertyName("ORCID"), Newtonsoft.Json.JsonProperty("ORCID")]
    public string? Orcid { get; set; } = null;
    [JsonPropertyName("ScholarID"), Newtonsoft.Json.JsonProperty("ScholarID")]
    public string? GoogleScholarId { get; set; } = null;
    [JsonPropertyName("ResearcherID"), Newtonsoft.Json.JsonProperty("ResearcherID")]
    public string? WebOfScienceResearcherId { get; set; } = null;
    [JsonPropertyName("ScopusID"), Newtonsoft.Json.JsonProperty("ScopusID")]
    public string? ScopusId { get; set; } = null;
    [JsonPropertyName("TcKimlikNo"), Newtonsoft.Json.JsonProperty("TcKimlikNo")]
    public string? TcKimlikNo { get; set; } = null;
    public bool IncludeProviderDetails { get; set; }
    public bool IncludeActivities { get; set; }
}
