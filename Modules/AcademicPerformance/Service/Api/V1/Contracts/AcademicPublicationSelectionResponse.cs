using Serenity.Services;
using System.Text.Json.Serialization;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class AcademicPublicationSelectionResponse : ServiceResponse
{
    [JsonPropertyName("PersonelID"), Newtonsoft.Json.JsonProperty("PersonelID")]
    public string PersonelId { get; set; } = string.Empty;
    public List<int> PublicationIds { get; set; } = [];
    public int ApprovedCount { get; set; }
}
