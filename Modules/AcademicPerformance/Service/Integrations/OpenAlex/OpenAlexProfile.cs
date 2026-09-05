using System.Text.Json.Serialization;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;

public sealed class OpenAlexProfile
{
    [JsonIgnore]
    public int Id { get; set; }

    [JsonIgnore]
    public int ResearcherId { get; set; }

    [JsonIgnore]
    public Researcher? Researcher { get; set; } = null;

    public string OpenAlexAuthorId { get; set; } = string.Empty;
    public string? DisplayName { get; set; } = null;
    public string? LastKnownInstitution { get; set; } = null;
    public int WorksCount { get; set; }
    public int CitedByCount { get; set; }
    public int? HIndex { get; set; } = null;
    public int? I10Index { get; set; } = null;
    public decimal? TwoYearMeanCitedness { get; set; } = null;
    public DateTime LastUpdatedAt { get; set; }

    [JsonIgnore]
    public string? CountsByYearJson { get; set; } = null;

    [JsonIgnore]
    public string? RawDataJson { get; set; } = null;

    [JsonIgnore]
    public string? WorksPagesJson { get; set; } = null;

    [JsonIgnore]
    public List<OpenAlexWork>? Works { get; set; } = null;
}
