using System.Text.Json.Serialization;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.GoogleScholar;

public sealed class GoogleScholarProfile
{
    [JsonIgnore]
    public int Id { get; set; }

    [JsonIgnore]
    public int ResearcherId { get; set; }

    [JsonIgnore]
    public Researcher? Researcher { get; set; } = null;

    public string? DisplayName { get; set; } = null;
    public string? Affiliations { get; set; } = null;
    public string? University { get; set; } = null;
    public string? VerifiedEmail { get; set; } = null;
    public string? ProfileUrl { get; set; } = null;
    public int? CitationCount { get; set; } = null;
    public int? CitationCountRecent { get; set; } = null;
    public int? HIndex { get; set; } = null;
    public int? HIndexRecent { get; set; } = null;
    public int? I10Index { get; set; } = null;
    public int? I10IndexRecent { get; set; } = null;
    public int? MetricsSinceYear { get; set; } = null;
    public int DocumentsCount { get; set; }
    public DateTime LastUpdatedAt { get; set; }

    [JsonIgnore]
    public string? InterestsJson { get; set; } = null;

    [JsonIgnore]
    public string? CitationHistogramJson { get; set; } = null;

    [JsonIgnore]
    public string? RawDataJson { get; set; } = null;

    [JsonIgnore]
    public List<GoogleScholarWork>? Works { get; set; } = null;
}
