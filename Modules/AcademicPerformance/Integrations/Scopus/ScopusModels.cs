using System.Text.Json;
using System.Text.Json.Serialization;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Scopus;

public sealed class ScopusData
{
    [JsonIgnore]
    public int Id { get; set; }

    [JsonIgnore]
    public int ResearcherId { get; set; }

    [JsonIgnore]
    public Researcher? Researcher { get; set; } = null;

    [JsonIgnore]
    public DateTime? LastUpdatedAt { get; set; } = null;

    public string? AuthorId { get; set; } = null;
    public string? GivenName { get; set; } = null;
    public string? Surname { get; set; } = null;
    public string? AffiliationName { get; set; } = null;
    public string? AffiliationCity { get; set; } = null;
    public string? AffiliationCountry { get; set; } = null;
    public int? DocumentCount { get; set; } = null;
    public int? CitedByCount { get; set; } = null;
    public int? CitationCount { get; set; } = null;
    public int? HIndex { get; set; } = null;
}

internal sealed class ScopusApiResponse
{
    [JsonPropertyName("author-retrieval-response")]
    public JsonElement Author { get; set; }
}

internal sealed class ScopusAuthorResponse
{
    [JsonPropertyName("coredata")]
    public ScopusCoreData? CoreData { get; set; } = null;

    [JsonPropertyName("author-profile")]
    public ScopusAuthorProfile? AuthorProfile { get; set; } = null;

    [JsonPropertyName("h-index")]
    public string? HIndex { get; set; } = null;
}

internal sealed class ScopusCoreData
{
    [JsonPropertyName("document-count")]
    public string? DocumentCount { get; set; } = null;

    [JsonPropertyName("cited-by-count")]
    public string? CitedByCount { get; set; } = null;

    [JsonPropertyName("citation-count")]
    public string? CitationCount { get; set; } = null;
}

internal sealed class ScopusAuthorProfile
{
    [JsonPropertyName("preferred-name")]
    public ScopusPreferredName? PreferredName { get; set; } = null;

    [JsonPropertyName("affiliation-current")]
    public ScopusCurrentAffiliation? CurrentAffiliation { get; set; } = null;
}

internal sealed class ScopusPreferredName
{
    [JsonPropertyName("given-name")]
    public string? GivenName { get; set; } = null;

    [JsonPropertyName("surname")]
    public string? Surname { get; set; } = null;
}

internal sealed class ScopusCurrentAffiliation
{
    [JsonPropertyName("affiliation")]
    public ScopusAffiliation? Affiliation { get; set; } = null;
}

internal sealed class ScopusAffiliation
{
    [JsonPropertyName("ip-doc")]
    public ScopusInstitution? Institution { get; set; } = null;
}

internal sealed class ScopusInstitution
{
    [JsonPropertyName("afdispname")]
    public string? DisplayName { get; set; } = null;

    [JsonPropertyName("address")]
    public ScopusAddress? Address { get; set; } = null;
}

internal sealed class ScopusAddress
{
    [JsonPropertyName("city")]
    public string? City { get; set; } = null;

    [JsonPropertyName("country")]
    public string? Country { get; set; } = null;
}
