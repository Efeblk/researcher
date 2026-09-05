using System.Text.Json.Serialization;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;

public sealed class OpenAlexWork
{
    [JsonIgnore]
    public int Id { get; set; }

    [JsonIgnore]
    public int OpenAlexProfileId { get; set; }

    [JsonIgnore]
    public OpenAlexProfile? OpenAlexProfile { get; set; } = null;

    public string OpenAlexWorkId { get; set; } = string.Empty;
    public string? Title { get; set; } = null;
    public int? PublicationYear { get; set; } = null;
    public DateTime? PublicationDate { get; set; } = null;
    public string? Doi { get; set; } = null;
    public string? WorkType { get; set; } = null;
    public int CitedByCount { get; set; }
    public string? Authors { get; set; } = null;
    public string? SourceName { get; set; } = null;
    public string? Url { get; set; } = null;
    public string? OpenAccessUrl { get; set; } = null;

    [JsonIgnore]
    public string? RawDataJson { get; set; } = null;
}
