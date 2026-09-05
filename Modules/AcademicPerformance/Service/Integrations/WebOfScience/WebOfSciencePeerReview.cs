using System.Text.Json.Serialization;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;

public sealed class WebOfSciencePeerReview
{
    [JsonIgnore]
    public int Id { get; set; }

    [JsonIgnore]
    public int WebOfScienceProfileId { get; set; }

    [JsonIgnore]
    public WebOfScienceProfile? WebOfScienceProfile { get; set; } = null;

    public string? Journal { get; set; } = null;
    public string? Publisher { get; set; } = null;
    public string? DateOfReview { get; set; } = null;
    public string? Verified { get; set; } = null;
    public string? ArticleTitle { get; set; } = null;
    public string? ArticleDoi { get; set; } = null;

    [JsonIgnore]
    public string? RawDataJson { get; set; } = null;
}
