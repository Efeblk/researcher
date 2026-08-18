using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works;

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

    [JsonIgnore]
    public string? RawDataJson { get; set; } = null;

    [JsonIgnore]
    public string? WorksResponsePagesJson { get; set; } = null;

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

    [JsonPropertyName("id")]
    public string? WorkId { get; set; } = null;

    [JsonPropertyName("title")]
    public string? Title { get; set; } = null;

    [JsonPropertyName("publication_year")]
    public int? PublicationYear { get; set; } = null;

    [JsonPropertyName("publication_date")]
    public DateTime? PublicationDate { get; set; } = null;

    [JsonPropertyName("doi")]
    public string? Doi { get; set; } = null;

    [JsonPropertyName("type")]
    public string? Type { get; set; } = null;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AcademicWorkCategory Category { get; set; } = AcademicWorkCategory.Unknown;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AcademicWorkCategorySource CategorySource { get; set; } =
        AcademicWorkCategorySource.Unknown;

    [JsonPropertyName("cited_by_count")]
    public int? CitedByCount { get; set; } = null;

    [JsonPropertyName("language")]
    public string? Language { get; set; } = null;

    [JsonIgnore]
    public string? Abstract { get; set; } = null;

    [JsonIgnore]
    public string? Authors { get; set; } = null;

    [JsonIgnore]
    public string? Institutions { get; set; } = null;

    [JsonIgnore]
    public string? Keywords { get; set; } = null;

    [JsonIgnore]
    public string? Topics { get; set; } = null;

    [JsonIgnore]
    public bool? IsOpenAccess { get; set; } = null;

    [JsonIgnore]
    public string? OpenAccessStatus { get; set; } = null;

    [JsonIgnore]
    public string? OpenAccessUrl { get; set; } = null;

    [JsonIgnore]
    public string? FullTextUrl { get; set; } = null;

    [JsonIgnore]
    public string? License { get; set; } = null;

    [JsonIgnore]
    public string? Version { get; set; } = null;

    [JsonIgnore]
    public string? Volume { get; set; } = null;

    [JsonIgnore]
    public string? Issue { get; set; } = null;

    [JsonIgnore]
    public string? FirstPage { get; set; } = null;

    [JsonIgnore]
    public string? LastPage { get; set; } = null;

    [JsonIgnore]
    public string? RawDataJson { get; set; } = null;

    [JsonPropertyName("is_retracted")]
    public bool? IsRetracted { get; set; } = null;

    [JsonPropertyName("has_fulltext")]
    public bool? HasFullText { get; set; } = null;

    [JsonPropertyName("referenced_works_count")]
    public int? ReferencedWorksCount { get; set; } = null;

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

    [NotMapped]
    [JsonPropertyName("best_oa_location")]
    public OpenAlexLocation? BestOpenAccessLocation { get; set; } = null;

    [NotMapped]
    [JsonPropertyName("open_access")]
    public OpenAlexOpenAccess? OpenAccess { get; set; } = null;

    [NotMapped]
    [JsonPropertyName("content_urls")]
    public OpenAlexContentUrls? ContentUrls { get; set; } = null;

    [NotMapped]
    [JsonPropertyName("abstract_inverted_index")]
    public Dictionary<string, List<int>>? AbstractInvertedIndex { get; set; } = null;

    [NotMapped]
    [JsonPropertyName("authorships")]
    public List<OpenAlexAuthorship>? Authorships { get; set; } = null;

    [NotMapped]
    [JsonPropertyName("keywords")]
    public List<OpenAlexNamedValue>? KeywordValues { get; set; } = null;

    [NotMapped]
    [JsonPropertyName("topics")]
    public List<OpenAlexNamedValue>? TopicValues { get; set; } = null;

    [NotMapped]
    [JsonPropertyName("biblio")]
    public OpenAlexBiblio? Biblio { get; set; } = null;
}

public sealed class OpenAlexLocation
{
    [JsonPropertyName("is_oa")]
    public bool? IsOpenAccess { get; set; } = null;

    [JsonPropertyName("landing_page_url")]
    public string? LandingPageUrl { get; set; } = null;

    [JsonPropertyName("pdf_url")]
    public string? PdfUrl { get; set; } = null;

    [JsonPropertyName("license")]
    public string? License { get; set; } = null;

    [JsonPropertyName("version")]
    public string? Version { get; set; } = null;

    [JsonPropertyName("source")]
    public OpenAlexSource? Source { get; set; } = null;
}

public sealed class OpenAlexOpenAccess
{
    [JsonPropertyName("is_oa")]
    public bool? IsOpenAccess { get; set; } = null;

    [JsonPropertyName("oa_status")]
    public string? Status { get; set; } = null;

    [JsonPropertyName("oa_url")]
    public string? Url { get; set; } = null;
}

public sealed class OpenAlexContentUrls
{
    [JsonPropertyName("pdf")]
    public string? Pdf { get; set; } = null;
}

public sealed class OpenAlexAuthorship
{
    [JsonPropertyName("author")]
    public OpenAlexAuthor? Author { get; set; } = null;

    [JsonPropertyName("institutions")]
    public List<OpenAlexInstitution>? Institutions { get; set; } = null;
}

public sealed class OpenAlexAuthor
{
    [JsonPropertyName("id")]
    public string? Id { get; set; } = null;

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; } = null;

    [JsonPropertyName("orcid")]
    public string? Orcid { get; set; } = null;
}

public sealed class OpenAlexInstitution
{
    [JsonPropertyName("id")]
    public string? Id { get; set; } = null;

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; } = null;
}

public sealed class OpenAlexNamedValue
{
    [JsonPropertyName("id")]
    public string? Id { get; set; } = null;

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; } = null;

    [JsonPropertyName("score")]
    public double? Score { get; set; } = null;
}

public sealed class OpenAlexBiblio
{
    [JsonPropertyName("volume")]
    public string? Volume { get; set; } = null;

    [JsonPropertyName("issue")]
    public string? Issue { get; set; } = null;

    [JsonPropertyName("first_page")]
    public string? FirstPage { get; set; } = null;

    [JsonPropertyName("last_page")]
    public string? LastPage { get; set; } = null;
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

internal sealed class OpenAlexWorksDownload
{
    public List<OpenAlexWork> Works { get; set; } = [];
    public string? ResponsePagesJson { get; set; } = null;
}
