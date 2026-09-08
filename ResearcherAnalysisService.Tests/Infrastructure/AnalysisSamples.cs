using ResearcherAnalysisService.Analysis;
using AcademicCollector.Analysis.Contracts;

namespace ResearcherAnalysisService.Tests.Infrastructure;

internal static class AnalysisSamples
{
    public static AnalyzeResearcherRequest Request() => new()
    {
        ResearcherId = 42,
        ResearcherName = "Synthetic Researcher",
        SnapshotAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
        TotalPublicationCount = 4,
        Publications =
        [
            new() { Id = "p1", Title = "Coastal water monitoring", Year = 2024, Category = "Article",
                Abstract = "We compare two monitoring methods. Further validation is needed.", Sources = "ORCID" },
            new() { Id = "p2", Title = "Monitoring water quality", Year = 2024, Category = "Article", Sources = "WebOfScience" },
            new() { Id = "p3", Title = "Water monitoring handbook", Category = "Book", Sources = "ORCID" }
        ],
        CitationMetrics =
        [
            new() { Provider = "GoogleScholar", CitationCount = 12, HIndex = 2 },
            new() { Provider = "WebOfScience", CitationCount = 0, HIndex = null }
        ]
    };

    public static GeneratedFindings Findings(string id = "p1", string field = "title",
        string quote = "Coastal water monitoring", bool writing = false)
    {
        AnalysisObservation observation = new()
        {
            Observation = "The supplied text discusses water monitoring.",
            Evidence = [new() { PublicationId = id, Field = field, Quote = quote }]
        };
        return new GeneratedFindings(new AnalysisFindings
        {
            ResearchFocus = writing ? [] : [observation],
            WritingObservations = writing ? [observation] : []
        }, "synthetic-model", "test-v1");
    }
}

internal sealed class StubReportGenerator : IResearcherReportGenerator
{
    public int Calls { get; private set; }
    public GeneratedFindings Result { get; set; } = AnalysisSamples.Findings();
    public Exception? Error { get; set; } = null;

    public Task<GeneratedFindings> GenerateAsync(AnalyzeResearcherRequest request, CancellationToken cancellationToken)
    {
        Calls++;
        cancellationToken.ThrowIfCancellationRequested();
        return Error is null ? Task.FromResult(Result) : Task.FromException<GeneratedFindings>(Error);
    }
}
