using Serenity.Services;
using System.Text.Json.Serialization;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.WebClient.Contracts;

public sealed class PublicationDisplayApprovalRequest : ServiceRequest
{
    [JsonPropertyName("PersonelID"), Newtonsoft.Json.JsonProperty("PersonelID")]
    public string PersonelId { get; set; } = string.Empty;
    public List<int> PublicationSummaryIds { get; set; } = [];
}
