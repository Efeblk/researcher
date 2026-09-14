using System.Text.Json.Serialization;

namespace ResearcherAnalysisService.SourceData.Works;

public sealed class CanonicalWorkObservation
{
    public int Id { get; set; }
    public int CanonicalWorkId { get; set; }

    [JsonIgnore]
    public CanonicalWork? CanonicalWork { get; set; } = null;

    public int AcademicWorkId { get; set; }

    [JsonIgnore]
    public AcademicWork? AcademicWork { get; set; } = null;

    public string PersonelId { get; set; } = string.Empty;
    public AcademicWorkProvider Provider { get; set; }
    public string? ProviderWorkId { get; set; } = null;
    public string? TitleObserved { get; set; } = null;
    public string? DoiObserved { get; set; } = null;
    public int? PublicationYearObserved { get; set; } = null;
    public DateTime? PublicationDateObserved { get; set; } = null;
    public AcademicWorkCategory CategoryObserved { get; set; } = AcademicWorkCategory.Unknown;
    public string? AuthorsObserved { get; set; } = null;
    public string? PublicationObserved { get; set; } = null;
    public string? SourceId { get; set; } = null;
    public string? SourceName { get; set; } = null;
    public string? SourceType { get; set; } = null;
    public string? Link { get; set; } = null;
    public string? FullTextUrl { get; set; } = null;
    public string? License { get; set; } = null;
    public string? Version { get; set; } = null;
    public bool? IsRetracted { get; set; } = null;
    public DateTime ObservedAt { get; set; }
}
