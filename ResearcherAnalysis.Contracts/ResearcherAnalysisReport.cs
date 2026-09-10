namespace AcademicCollector.Analysis.Contracts;
using System.Text.Json.Serialization;

public sealed class ResearcherAnalysisReport
{
    public string SchemaVersion { get; init; } = "1";
    [JsonPropertyName("PersonelID")]
    public string PersonelId { get; init; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; init; }
    public string Model { get; init; } = string.Empty;
    public string PromptVersion { get; init; } = string.Empty;
    public required AnalysisFindings Findings { get; init; }
    public required PublicationActivity Activity { get; init; }
    public required List<ProviderMetrics> CitationMetrics { get; init; }
    public required EvidenceCoverage Coverage { get; init; }
    public ResearcherSourceCoverage? SourceCoverage { get; set; }
}

public sealed record PublicationActivity(
    int SubmittedPublicationCount,
    SortedDictionary<int, int> PublicationsByYear,
    SortedDictionary<string, int> PublicationsByCategory);

public sealed record EvidenceCoverage(
    DateTimeOffset SnapshotAt,
    int TotalPublicationCount,
    int SubmittedPublicationCount,
    int PublicationsWithAbstract,
    int PublicationsWithoutYear,
    bool IsPartial,
    List<string> Limitations);

public sealed record ResearcherSourceCoverage(
    int TotalPublications,
    int FullTextAvailable,
    int PdfAvailable,
    int OcrAvailable,
    int HtmlAvailable,
    int AbstractOnly,
    int MetadataOnly,
    int ExcludedFromModelInput,
    int SubmittedToModel,
    int AbstractsSubmittedToModel);
