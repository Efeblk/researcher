using System.Text.Json.Serialization;

public sealed class OpenAlexData
{
    [JsonIgnore]
    public int Id { get; set; }

    [JsonIgnore]
    public int ResearcherId { get; set; }

    [JsonIgnore]
    public Researcher? Researcher { get; set; } = null;

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
}

internal sealed class OpenAlexWorksResponse
{
    [JsonPropertyName("results")]
    public List<OpenAlexWork>? Results { get; set; } = null;
}
