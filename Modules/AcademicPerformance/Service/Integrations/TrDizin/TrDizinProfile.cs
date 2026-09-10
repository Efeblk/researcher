using System.Text.Json.Serialization;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.TrDizin;

public sealed class TrDizinProfile
{
    [JsonIgnore]
    public int Id { get; set; }

    [JsonIgnore]
    public string PersonelId { get; set; } = string.Empty;

    [JsonIgnore]
    public Researcher? Researcher { get; set; } = null;

    public string Orcid { get; set; } = string.Empty;
    public long AuthorId { get; set; }
    public string? DisplayName { get; set; } = null;
    public int? PublicationCount { get; set; } = null;
    public int? CitationCount { get; set; } = null;
    public DateTime LastUpdatedAt { get; set; }
    [JsonIgnore]
    public string RawAuthorJson { get; set; } = string.Empty;

    [JsonIgnore]
    public string RawPublicationsJson { get; set; } = string.Empty;

    [JsonIgnore]
    public List<TrDizinWork>? Works { get; set; } = null;
}
