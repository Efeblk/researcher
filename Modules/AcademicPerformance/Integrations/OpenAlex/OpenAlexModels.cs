using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;

public sealed class OpenAlexData
{
    [JsonIgnore]
    public int Id { get; set; }

    [JsonIgnore]
    public int ResearcherId { get; set; }

    [JsonIgnore]
    public Researcher? Researcher { get; set; } = null;

    [JsonIgnore]
    public DateTime? LastUpdatedAt { get; set; } = null;

    [JsonPropertyName("id")]
    public string? AuthorId { get; set; } = null;

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; } = null;

    [JsonPropertyName("works_count")]
    public int? WorksCount { get; set; } = null;

    [JsonIgnore]
    public List<OpenAlexWork>? Works { get; set; } = null;
}

public sealed class OpenAlexWork
{
    [JsonIgnore]
    public int Id { get; set; }

    [JsonIgnore]
    public int OpenAlexDataId { get; set; }

    [JsonIgnore]
    public OpenAlexData? OpenAlexData { get; set; } = null;

    [JsonPropertyName("title")]
    public string? Title { get; set; } = null;

    [JsonPropertyName("publication_year")]
    public int? PublicationYear { get; set; } = null;

    [JsonPropertyName("doi")]
    public string? Doi { get; set; } = null;

    [JsonPropertyName("type")]
    public string? Type { get; set; } = null;

    [JsonPropertyName("cited_by_count")]
    public int? CitedByCount { get; set; } = null;

    [JsonIgnore]
    public string? SourceId { get; set; } = null;

    [JsonIgnore]
    public string? SourceName { get; set; } = null;

    [JsonIgnore]
    public string? SourceType { get; set; } = null;

    [JsonIgnore]
    public string? SourceUrl { get; set; } = null;

    [NotMapped]
    [JsonPropertyName("primary_location")]
    public OpenAlexLocation? PrimaryLocation { get; set; } = null;
}

public sealed class OpenAlexLocation
{
    [JsonPropertyName("landing_page_url")]
    public string? LandingPageUrl { get; set; } = null;

    [JsonPropertyName("source")]
    public OpenAlexSource? Source { get; set; } = null;
}

public sealed class OpenAlexSource
{
    [JsonPropertyName("id")]
    public string? Id { get; set; } = null;

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; } = null;

    [JsonPropertyName("type")]
    public string? Type { get; set; } = null;
}

internal sealed class OpenAlexWorksResponse
{
    [JsonPropertyName("meta")]
    public OpenAlexWorksMeta? Meta { get; set; } = null;

    [JsonPropertyName("results")]
    public List<OpenAlexWork>? Results { get; set; } = null;
}

internal sealed class OpenAlexWorksMeta
{
    [JsonPropertyName("next_cursor")]
    public string? NextCursor { get; set; } = null;
}
