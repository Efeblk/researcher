using System.Text.Json.Serialization;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;

public sealed class AcademicWork
{
    public int Id { get; set; }
    public int ResearcherId { get; set; }

    [JsonIgnore]
    public Researcher? Researcher { get; set; } = null;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AcademicWorkProvider Provider { get; set; } = AcademicWorkProvider.Orcid;

    public string? ProviderWorkId { get; set; } = null;
    public string? Title { get; set; } = null;
    public int? PublicationYear { get; set; } = null;
    public DateTime? PublicationDate { get; set; } = null;
    public string? Doi { get; set; } = null;
    public string? RawType { get; set; } = null;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AcademicWorkCategory Category { get; set; } = AcademicWorkCategory.Unknown;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AcademicWorkCategorySource CategorySource { get; set; } =
        AcademicWorkCategorySource.Unknown;

    public int? CitedByCount { get; set; } = null;
    public int? ReferencedWorksCount { get; set; } = null;
    public string? Authors { get; set; } = null;
    public string? Institutions { get; set; } = null;
    public string? Abstract { get; set; } = null;
    public string? Keywords { get; set; } = null;
    public string? Topics { get; set; } = null;
    public string? Language { get; set; } = null;
    public string? Publication { get; set; } = null;
    public string? Volume { get; set; } = null;
    public string? Issue { get; set; } = null;
    public string? FirstPage { get; set; } = null;
    public string? LastPage { get; set; } = null;
    public string? Link { get; set; } = null;
    public string? SourceId { get; set; } = null;
    public string? SourceName { get; set; } = null;
    public string? SourceType { get; set; } = null;
    public string? SourceUrl { get; set; } = null;
    public bool? IsOpenAccess { get; set; } = null;
    public string? OpenAccessStatus { get; set; } = null;
    public string? OpenAccessUrl { get; set; } = null;
    public bool? HasFullText { get; set; } = null;
    public string? FullTextUrl { get; set; } = null;
    public string? License { get; set; } = null;
    public string? Version { get; set; } = null;
    public bool? IsRetracted { get; set; } = null;
    public string? ProviderPayload { get; set; } = null;
    public DateTime SyncedAt { get; set; }
}
