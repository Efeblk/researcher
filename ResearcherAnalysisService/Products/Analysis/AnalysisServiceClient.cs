using AcademicCollector.Analysis.Contracts;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;

namespace ResearcherAnalysisService.Products.Analysis;

public sealed class AnalysisServiceClient(ResearcherAnalysis analysis, IOptions<AnalysisServiceOptions> options)
{
    public async Task<ResearcherAnalysisReport> AnalyzeAsync(
        AnalyzeResearcherRequest snapshot, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
        ResearcherAnalysisReport report = await analysis.AnalyzeAsync(snapshot, timeout.Token);
        if (!report.PersonelId.Equals(snapshot.PersonelId, StringComparison.Ordinal) || report.Coverage is null ||
            report.Coverage.SnapshotAt != snapshot.SnapshotAt || report.Findings is null ||
            report.Activity is null || report.CitationMetrics is null ||
            string.IsNullOrWhiteSpace(report.Model) || string.IsNullOrWhiteSpace(report.PromptVersion))
            throw new System.Text.Json.JsonException("The analysis report does not match its snapshot.");
        return report;
    }
}
