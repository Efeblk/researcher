using Serenity.Services;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis;

public sealed class YoksisCollectRequest : ServiceRequest
{
    [Required, StringLength(200)]
    [JsonPropertyName("PersonelID"), Newtonsoft.Json.JsonProperty("PersonelID")]
    public string PersonelId { get; set; } = string.Empty;
    public string? TcKimlikNo { get; set; } = null;
    public DateTime? UpdatedAfter { get; set; } = null;
    public bool IncludeRecords { get; set; }
    public bool IncludeRawResponses { get; set; }
}
