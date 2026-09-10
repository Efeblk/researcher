namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.SemanticScholar;

public sealed class SemanticScholarPaper
{
    public int Id { get; set; }
    public string NormalizedDoi { get; set; } = string.Empty;
    public string? PaperId { get; set; } = null;
    public bool Found { get; set; }
    public DateTime FetchedAt { get; set; }
    public int? CitationTotal { get; set; } = null;
    public int CitationsFetched { get; set; }
    public bool CitationsComplete { get; set; }
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
    public string? RawDataJson { get; set; } = null;
    public List<SemanticScholarCitation> Citations { get; set; } = [];
}
