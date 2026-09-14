using System.Text.Json.Serialization;
using ResearcherAnalysisService.SourceData.Researchers;
using ResearcherAnalysisService.SourceData.Works;

namespace ResearcherAnalysisService.SourceData.Integrations.WebOfScience;

public sealed class WebOfScienceWork
{
    [JsonIgnore]
    public int Id { get; set; }

    [JsonIgnore]
    public int WebOfScienceProfileId { get; set; }

    [JsonIgnore]
    public WebOfScienceProfile? WebOfScienceProfile { get; set; } = null;

    public string? Uid { get; set; } = null;
    public string? Title { get; set; } = null;
    public string? WorkTypes { get; set; } = null;
    public int? PublicationYear { get; set; } = null;
    public DateTime? PublicationDate { get; set; } = null;
    public string? SourceTitle { get; set; } = null;
    public string? Volume { get; set; } = null;
    public string? Issue { get; set; } = null;
    public string? Collection { get; set; } = null;
    public string? Doi { get; set; } = null;
    public int? TimesCited { get; set; } = null;
    public AcademicWorkCategory Category { get; set; } = AcademicWorkCategory.Unknown;
    public AcademicWorkCategorySource CategorySource { get; set; } =
        AcademicWorkCategorySource.Unknown;

    [JsonIgnore]
    public string? CitationsJson { get; set; } = null;

    [JsonIgnore]
    public string? RawDataJson { get; set; } = null;
}
