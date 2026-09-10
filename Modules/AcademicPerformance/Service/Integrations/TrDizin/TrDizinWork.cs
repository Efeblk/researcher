using System.Text.Json.Serialization;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.TrDizin;

public sealed class TrDizinWork
{
    [JsonIgnore]
    public int Id { get; set; }

    [JsonIgnore]
    public int TrDizinProfileId { get; set; }

    [JsonIgnore]
    public TrDizinProfile? TrDizinProfile { get; set; } = null;

    public string PublicationId { get; set; } = string.Empty;
    public string? Title { get; set; } = null;
    public string? Doi { get; set; } = null;
    public int? PublicationYear { get; set; } = null;
    public string? PublicationType { get; set; } = null;
    public string? Authors { get; set; } = null;
    public string? Journal { get; set; } = null;
    public int? CitationCount { get; set; } = null;
    [JsonIgnore]
    public string RawDataJson { get; set; } = string.Empty;
}
