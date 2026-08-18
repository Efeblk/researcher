using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.GoogleScholar;

public sealed class GoogleScholarData
{
    [JsonIgnore]
    public int Id { get; set; }

    [JsonIgnore]
    public int ResearcherId { get; set; }

    [JsonIgnore]
    public Researcher? Researcher { get; set; } = null;

    [JsonIgnore]
    public DateTime? LastUpdatedAt { get; set; } = null;

    [JsonIgnore]
    public string? RawDataJson { get; set; } = null;

    [JsonIgnore]
    public string? ResponsePagesJson { get; set; } = null;

    public string? ScholarId { get; set; } = null;
    public string? Name { get; set; } = null;
    public string? Affiliations { get; set; } = null;
    public string? Email { get; set; } = null;
    public int? CitationCount { get; set; } = null;
    public int? HIndex { get; set; } = null;
    public int? I10Index { get; set; } = null;
    public List<GoogleScholarInterest>? Interests { get; set; } = null;
    public List<GoogleScholarWork>? Works { get; set; } = null;
}

public sealed class GoogleScholarInterest
{
    [JsonIgnore]
    public int Id { get; set; }

    [JsonIgnore]
    public int GoogleScholarDataId { get; set; }

    [JsonIgnore]
    public GoogleScholarData? GoogleScholarData { get; set; } = null;

    [JsonPropertyName("title")]
    public string? Title { get; set; } = null;
}

public sealed class GoogleScholarWork
{
    [JsonIgnore]
    public int Id { get; set; }

    [JsonIgnore]
    public int GoogleScholarDataId { get; set; }

    [JsonIgnore]
    public GoogleScholarData? GoogleScholarData { get; set; } = null;

    [JsonPropertyName("title")]
    public string? Title { get; set; } = null;

    [JsonPropertyName("link")]
    public string? Link { get; set; } = null;

    [JsonPropertyName("citation_id")]
    public string? CitationId { get; set; } = null;

    [JsonPropertyName("authors")]
    public string? Authors { get; set; } = null;

    [JsonPropertyName("publication")]
    public string? Publication { get; set; } = null;

    [JsonPropertyName("year")]
    public string? Year { get; set; } = null;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AcademicWorkCategory Category { get; set; } = AcademicWorkCategory.Unknown;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AcademicWorkCategorySource CategorySource { get; set; } =
        AcademicWorkCategorySource.Unknown;

    [JsonIgnore]
    public int? CitedByCount { get; set; } = null;

    [JsonIgnore]
    public string? CitedByUrl { get; set; } = null;

    [JsonIgnore]
    public string? CitedBySerpApiUrl { get; set; } = null;

    [JsonIgnore]
    public string? CitesId { get; set; } = null;

    [JsonIgnore]
    public string? RawDataJson { get; set; } = null;

    [JsonIgnore]
    public string? DetailRawDataJson { get; set; } = null;

    [NotMapped]
    [JsonPropertyName("cited_by")]
    public GoogleScholarCitedBy? CitedBy { get; set; } = null;
}

public sealed class GoogleScholarCitedBy
{
    [JsonPropertyName("value")]
    public int? Value { get; set; } = null;

    [JsonPropertyName("link")]
    public string? Link { get; set; } = null;

    [JsonPropertyName("serpapi_link")]
    public string? SerpApiLink { get; set; } = null;

    [JsonPropertyName("cites_id")]
    public string? CitesId { get; set; } = null;

    [JsonPropertyName("table")]
    public List<GoogleScholarMetricRow>? Table { get; set; } = null;
}

public sealed class GoogleScholarMetricRow
{
    [JsonPropertyName("citations")]
    public GoogleScholarMetricValue? Citations { get; set; } = null;

    [JsonPropertyName("h_index")]
    public GoogleScholarMetricValue? HIndex { get; set; } = null;

    [JsonPropertyName("i10_index")]
    public GoogleScholarMetricValue? I10Index { get; set; } = null;
}

public sealed class GoogleScholarMetricValue
{
    [JsonPropertyName("all")]
    public int? All { get; set; } = null;
}

internal sealed class GoogleScholarAuthorResponse
{
    [JsonPropertyName("author")]
    public GoogleScholarAuthor? Author { get; set; } = null;

    [JsonPropertyName("articles")]
    public List<GoogleScholarWork>? Articles { get; set; } = null;

    [JsonPropertyName("cited_by")]
    public GoogleScholarCitedBy? CitedBy { get; set; } = null;
}

internal sealed class GoogleScholarAuthor
{
    [JsonPropertyName("name")]
    public string? Name { get; set; } = null;

    [JsonPropertyName("affiliations")]
    public string? Affiliations { get; set; } = null;

    [JsonPropertyName("email")]
    public string? Email { get; set; } = null;

    [JsonPropertyName("interests")]
    public List<GoogleScholarInterest>? Interests { get; set; } = null;
}
