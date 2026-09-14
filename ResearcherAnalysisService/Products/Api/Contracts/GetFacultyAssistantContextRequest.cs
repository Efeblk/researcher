using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ResearcherAnalysisService.Products.Api.Contracts;

public sealed class GetFacultyAssistantContextRequest
{
    [Required, StringLength(200)]
    [JsonPropertyName("PersonelID")]
    public string PersonelId { get; set; } = string.Empty;
    [Range(1, int.MaxValue)] public int? Version { get; set; }
}
