using System.Text.Json.Serialization;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Orcid;

public sealed class OrcidWork
{
    [JsonIgnore]
    public int Id { get; set; }

    [JsonIgnore]
    public int OrcidProfileId { get; set; }

    [JsonIgnore]
    public OrcidProfile? OrcidProfile { get; set; } = null;

    public long PutCode { get; set; }
    public string? Title { get; set; } = null;
    public string? Subtitle { get; set; } = null;
    public string? TranslatedTitle { get; set; } = null;
    public string? WorkType { get; set; } = null;
    public int? PublicationYear { get; set; } = null;
    public DateTime? PublicationDate { get; set; } = null;
    public string? JournalTitle { get; set; } = null;
    public string? Doi { get; set; } = null;
    public string? Url { get; set; } = null;
    public string? Authors { get; set; } = null;
    public string? LanguageCode { get; set; } = null;
    public string? CountryCode { get; set; } = null;
    public string? ShortDescription { get; set; } = null;
    public string? Citation { get; set; } = null;
    public string? SourceName { get; set; } = null;
    public string? Visibility { get; set; } = null;
    public DateTime? RecordLastModifiedAt { get; set; } = null;
    public AcademicWorkCategory Category { get; set; } = AcademicWorkCategory.Unknown;
    public AcademicWorkCategorySource CategorySource { get; set; } =
        AcademicWorkCategorySource.Unknown;

    [JsonIgnore]
    public string? ExternalIdentifiersJson { get; set; } = null;

    [JsonIgnore]
    public string? ContributorsJson { get; set; } = null;

    [JsonIgnore]
    public string? RawDataJson { get; set; } = null;
}
