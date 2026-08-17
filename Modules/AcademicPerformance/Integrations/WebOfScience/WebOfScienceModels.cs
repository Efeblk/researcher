using System.Text.Json.Serialization;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;

public sealed class WebOfScienceData
{
    [JsonIgnore]
    public int Id { get; set; }

    [JsonIgnore]
    public int ResearcherId { get; set; }

    [JsonIgnore]
    public Researcher? Researcher { get; set; } = null;

    [JsonIgnore]
    public DateTime? LastUpdatedAt { get; set; } = null;

    public string? Rid { get; set; } = null;
    public string? FullName { get; set; } = null;
    public string? FirstName { get; set; } = null;
    public string? LastName { get; set; } = null;
    public string? PrimaryAffiliation { get; set; } = null;
    public string? Address { get; set; } = null;
    public string? Country { get; set; } = null;
    public bool? IsClaimed { get; set; } = null;
    public int? DocumentCount { get; set; } = null;
    public int? TotalTimesCited { get; set; } = null;
    public int? TotalCitingPublications { get; set; } = null;
    public int? HIndex { get; set; } = null;
}

internal sealed class WebOfScienceApiResponse
{
    [JsonPropertyName("ids")]
    public WebOfScienceIds? Ids { get; set; } = null;

    [JsonPropertyName("claimStatus")]
    public bool? ClaimStatus { get; set; } = null;

    [JsonPropertyName("name")]
    public WebOfScienceName? Name { get; set; } = null;

    [JsonPropertyName("metricsAllTime")]
    public WebOfScienceMetrics? MetricsAllTime { get; set; } = null;

    [JsonPropertyName("organization")]
    public WebOfScienceOrganization? Organization { get; set; } = null;
}

internal sealed class WebOfScienceIds
{
    [JsonPropertyName("rids")]
    public List<string>? Rids { get; set; } = null;
}

internal sealed class WebOfScienceName
{
    [JsonPropertyName("fullName")]
    public string? FullName { get; set; } = null;

    [JsonPropertyName("firstName")]
    public string? FirstName { get; set; } = null;

    [JsonPropertyName("lastName")]
    public string? LastName { get; set; } = null;
}

internal sealed class WebOfScienceMetrics
{
    [JsonPropertyName("hIndex")]
    public int? HIndex { get; set; } = null;

    [JsonPropertyName("documents")]
    public WebOfScienceDocuments? Documents { get; set; } = null;

    [JsonPropertyName("totalTimesCited")]
    public int? TotalTimesCited { get; set; } = null;

    [JsonPropertyName("totalCitingPublications")]
    public int? TotalCitingPublications { get; set; } = null;
}

internal sealed class WebOfScienceDocuments
{
    [JsonPropertyName("count")]
    public int? Count { get; set; } = null;
}

internal sealed class WebOfScienceOrganization
{
    [JsonPropertyName("primaryAffiliation")]
    public List<WebOfScienceAffiliation>? PrimaryAffiliations { get; set; } = null;
}

internal sealed class WebOfScienceAffiliation
{
    [JsonPropertyName("organizationEnhancedName")]
    public string? EnhancedName { get; set; } = null;

    [JsonPropertyName("organizationName")]
    public string? Name { get; set; } = null;

    [JsonPropertyName("address")]
    public string? Address { get; set; } = null;

    [JsonPropertyName("country")]
    public string? Country { get; set; } = null;
}
