using System.Text.Json.Serialization;

public sealed class OpenAlexAuthor
{
    [JsonPropertyName("id")]
    public string? Id { get; set; } = null;

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; } = null;

    [JsonPropertyName("works_count")]
    public int WorksCount { get; set; }
}

public sealed class OpenAlexWorksResponse
{
    [JsonPropertyName("results")]
    public List<OpenAlexWork>? Results { get; set; } = null;
}

public sealed class OpenAlexWork
{
    [JsonPropertyName("title")]
    public string? Title { get; set; } = null;

    [JsonPropertyName("publication_year")]
    public int? PublicationYear { get; set; }

    [JsonPropertyName("doi")]
    public string? Doi { get; set; } = null;

    [JsonPropertyName("type")]
    public string? Type { get; set; } = null;

    [JsonPropertyName("cited_by_count")]
    public int CitedByCount { get; set; }
}
