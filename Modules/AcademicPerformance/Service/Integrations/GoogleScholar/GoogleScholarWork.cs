using System.Text.Json.Serialization;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.GoogleScholar;

public sealed class GoogleScholarWork
{
    [JsonIgnore]
    public int Id { get; set; }

    [JsonIgnore]
    public int GoogleScholarProfileId { get; set; }

    [JsonIgnore]
    public GoogleScholarProfile? GoogleScholarProfile { get; set; } = null;

    public string CitationId { get; set; } = string.Empty;
    public string? Title { get; set; } = null;
    public string? Authors { get; set; } = null;
    public string? Publication { get; set; } = null;
    public int? PublicationYear { get; set; } = null;
    public int? CitedByCount { get; set; } = null;
    public string? Url { get; set; } = null;

    [JsonIgnore]
    public string? RawDataJson { get; set; } = null;
}
