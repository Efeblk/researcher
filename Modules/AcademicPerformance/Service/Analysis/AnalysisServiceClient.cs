using Microsoft.Extensions.Options;
using AcademicCollector.Analysis.Contracts;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Analysis;

public sealed class AnalysisServiceClient(HttpClient client, IOptions<AnalysisServiceOptions> options)
{
    public async Task<ResearcherAnalysisReport> AnalyzeAsync(
        AnalyzeResearcherRequest snapshot, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "api/v1/analyze")
        {
            Content = JsonContent.Create(snapshot)
        };
        if (!string.IsNullOrWhiteSpace(options.Value.ApiKey))
            request.Headers.Add("X-Analysis-Key", options.Value.ApiKey);
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("The analysis service could not generate a report.", null, response.StatusCode);
        ResearcherAnalysisReport report = await response.Content.ReadFromJsonAsync<ResearcherAnalysisReport>(cancellationToken)
            ?? throw new System.Text.Json.JsonException("The analysis service returned an empty report.");
        if (!report.PersonelId.Equals(snapshot.PersonelId, StringComparison.Ordinal) || report.Coverage is null ||
            report.Coverage.SnapshotAt != snapshot.SnapshotAt || report.Findings is null ||
            report.Activity is null || report.CitationMetrics is null ||
            string.IsNullOrWhiteSpace(report.Model) || string.IsNullOrWhiteSpace(report.PromptVersion))
            throw new System.Text.Json.JsonException("The analysis report does not match its snapshot.");
        return report;
    }
}
