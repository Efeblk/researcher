namespace AcademicCollector.Analysis.Contracts;

public sealed class ResearcherAnalysisReport
{
    public string SchemaVersion { get; init; } = "1";
    public int ResearcherId { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
    public string Model { get; init; } = string.Empty;
    public string PromptVersion { get; init; } = string.Empty;
    public required AnalysisFindings Findings { get; init; }
    public required PublicationActivity Activity { get; init; }
    public required List<ProviderMetrics> CitationMetrics { get; init; }
    public required EvidenceCoverage Coverage { get; init; }
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
