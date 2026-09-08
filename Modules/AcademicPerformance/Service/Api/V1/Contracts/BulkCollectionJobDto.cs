namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using System.Text.Json.Serialization;

public sealed class BulkCollectionJobDto
{
    public long Id { get; set; }
    [JsonPropertyName("PersonelID"), Newtonsoft.Json.JsonProperty("PersonelID")]
    public string PersonelId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public string? Message { get; set; } = null;
    public List<string> Warnings { get; set; } = [];
}
