using System.Text.Json.Serialization;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;

public sealed class WebOfScienceProfile
{
    [JsonIgnore]
    public int Id { get; set; }

    [JsonIgnore]
    public int ResearcherId { get; set; }

    [JsonIgnore]
    public Researcher? Researcher { get; set; } = null;

    public string? DisplayName { get; set; } = null;
    public string? FirstName { get; set; } = null;
    public string? LastName { get; set; } = null;
    public string? Orcid { get; set; } = null;
    public bool IsClaimed { get; set; }
    public string? PrimaryOrganization { get; set; } = null;
    public string? PrimaryAddress { get; set; } = null;
    public string? PrimaryCountry { get; set; } = null;
    public string? Departments { get; set; } = null;
    public int? HIndex { get; set; } = null;
    public int DocumentsCount { get; set; }
    public int? TotalCitingPublications { get; set; } = null;
    public int? TotalCitingWithoutSelf { get; set; } = null;
    public int? TotalTimesCited { get; set; } = null;
    public int? TotalTimesCitedWithoutSelf { get; set; } = null;
    public int PeerReviewsCount { get; set; }
    public DateTime LastUpdatedAt { get; set; }

    [JsonIgnore]
    public string? AlternativeNamesJson { get; set; } = null;

    [JsonIgnore]
    public string? AffiliationsJson { get; set; } = null;

    [JsonIgnore]
    public string? AuthorPositionsJson { get; set; } = null;

    [JsonIgnore]
    public string? SubjectCategoriesJson { get; set; } = null;

    [JsonIgnore]
    public string? AwardsJson { get; set; } = null;

    [JsonIgnore]
    public string? RawDataJson { get; set; } = null;

    [JsonIgnore]
    public string? DocumentPagesJson { get; set; } = null;

    [JsonIgnore]
    public string? PeerReviewPagesJson { get; set; } = null;

    [JsonIgnore]
    public List<WebOfScienceWork>? Works { get; set; } = null;

    [JsonIgnore]
    public List<WebOfSciencePeerReview>? PeerReviews { get; set; } = null;
}
