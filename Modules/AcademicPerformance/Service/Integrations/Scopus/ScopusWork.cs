using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Scopus;

public sealed class ScopusWork
{
    [JsonIgnore]
    public int Id { get; set; }

    [JsonIgnore]
    public int ScopusProfileId { get; set; }

    [JsonIgnore]
    public ScopusProfile? ScopusProfile { get; set; } = null;

    public string ScopusWorkId { get; set; } = string.Empty;
    public string? Eid { get; set; } = null;
    public string? Title { get; set; } = null;
    public int? PublicationYear { get; set; } = null;
    public DateTime? PublicationDate { get; set; } = null;
    public string? Doi { get; set; } = null;
    public string? WorkType { get; set; } = null;
    public int? CitedByCount { get; set; } = null;
    public string? Authors { get; set; } = null;
    public string? SourceName { get; set; } = null;
    public string? Url { get; set; } = null;
    public bool? IsOpenAccess { get; set; } = null;

    [NotMapped]
    public AcademicWorkCategory Category { get; set; } = AcademicWorkCategory.Unknown;

    [NotMapped]
    public AcademicWorkCategorySource CategorySource { get; set; } =
        AcademicWorkCategorySource.Unknown;

    [JsonIgnore]
    public string? RawDataJson { get; set; } = null;
}
