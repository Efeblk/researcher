using System.Text.Json.Serialization;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Scopus;

public sealed class ScopusProfile
{
    [JsonIgnore]
    public int Id { get; set; }

    [JsonIgnore]
    public string PersonelId { get; set; } = string.Empty;

    [JsonIgnore]
    public Researcher? Researcher { get; set; } = null;

    public string ScopusAuthorId { get; set; } = string.Empty;
    public string? DisplayName { get; set; } = null;
    public string? CurrentAffiliation { get; set; } = null;
    public int? DocumentsCount { get; set; } = null;
    public int? CitationCount { get; set; } = null;
    public int? CitedByCount { get; set; } = null;
    public int? HIndex { get; set; } = null;
    public DateTime LastUpdatedAt { get; set; }

    [JsonIgnore]
    public string? RawDataJson { get; set; } = null;

    [JsonIgnore]
    public string? SearchPagesJson { get; set; } = null;

    [JsonIgnore]
    public List<ScopusWork>? Works { get; set; } = null;
}
