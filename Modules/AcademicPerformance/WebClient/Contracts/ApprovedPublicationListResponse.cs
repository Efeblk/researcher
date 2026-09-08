using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using Serenity.Services;
using System.Text.Json.Serialization;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.WebClient.Contracts;

public sealed class ApprovedPublicationListResponse : ServiceResponse
{
    [JsonPropertyName("PersonelID"), Newtonsoft.Json.JsonProperty("PersonelID")]
    public string PersonelId { get; set; } = string.Empty;
    public List<PublicationSummary> Entities { get; set; } = [];
    public int TotalCount { get; set; }
}
