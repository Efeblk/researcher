using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

public sealed class SemanticScholarRequest
{
    [Required, JsonPropertyName("PersonelID"), Newtonsoft.Json.JsonProperty("PersonelID")]
    public string PersonelId { get; set; } = string.Empty;
    public int? AcademicWorkId { get; set; } = null;
    public int Skip { get; set; }
    [Range(1, 500)] public int Take { get; set; } = 100;
}

public sealed class SemanticScholarCitationRequest
{
    [Required, JsonPropertyName("PersonelID"), Newtonsoft.Json.JsonProperty("PersonelID")]
    public string PersonelId { get; set; } = string.Empty;
    [Range(1, int.MaxValue)] public int AcademicWorkId { get; set; }
    public int Skip { get; set; }
    [Range(1, 500)] public int Take { get; set; } = 100;
}

public sealed class SemanticScholarCollectRequest
{
    [Required, JsonPropertyName("PersonelID"), Newtonsoft.Json.JsonProperty("PersonelID")]
    public string PersonelId { get; set; } = string.Empty;
}

public sealed class SemanticScholarCollectResponse : ServiceResponse
{
    public int ProcessedDoiCount { get; set; }
    public bool HasPendingWork { get; set; }
    public string? Message { get; set; } = null;
}

public sealed class SemanticScholarResponse : ServiceResponse
{
    public int TotalPapers { get; set; }
    public List<SemanticScholarPaperDto> Papers { get; set; } = [];
}

public sealed class SemanticScholarPaperDto
{
    public int AcademicWorkId { get; set; }
    public string Doi { get; set; } = string.Empty;
    public string? PaperId { get; set; } = null;
    public bool Found { get; set; }
    public DateTime FetchedAt { get; set; }
    public int? CitationTotal { get; set; } = null;
    public int CitationsFetched { get; set; }
    public bool CitationsComplete { get; set; }
    public int CitationNextOffset { get; set; }
    public bool CitationsRefreshing { get; set; }
    public int? StoredCitationCount { get; set; } = null;
    public string? Title { get; set; } = null;
    public string? Abstract { get; set; } = null;
    public string? AuthorsJson { get; set; } = null;
    public int? Year { get; set; } = null;
    public string? Venue { get; set; } = null;
    public DateTime? PublicationDate { get; set; } = null;
    public string? JournalJson { get; set; } = null;
    public string? PublicationTypesJson { get; set; } = null;
    public string? FieldsOfStudyJson { get; set; } = null;
    public string? OpenAccessPdfJson { get; set; } = null;
    public int? CitationCount { get; set; } = null;
    public int? ReferenceCount { get; set; } = null;
    public int? InfluentialCitationCount { get; set; } = null;
    public string? Url { get; set; } = null;
    public string? TldrJson { get; set; } = null;
    public string? TextAvailability { get; set; } = null;
    public List<SemanticScholarCitationDto> Citations { get; set; } = [];
}

public sealed class SemanticScholarCitationDto
{
    public string CitingPaperId { get; set; } = string.Empty;
    public string? CitingDoi { get; set; } = null;
    public string? CitingTitle { get; set; } = null;
    public string? CitingAuthorsJson { get; set; } = null;
    public bool? IsInfluential { get; set; } = null;
    public string? IntentsJson { get; set; } = null;
    public List<SemanticScholarContextDto> Contexts { get; set; } = [];
}

public sealed class SemanticScholarContextDto
{
    public string Context { get; set; } = string.Empty;
    public string? IntentsJson { get; set; } = null;
}
