using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Api.V1.Contracts;
using ResearcherAnalysisService.Tests.Infrastructure;

namespace ResearcherAnalysisService.Tests;

public sealed class AnalyzeEndpointTests
{
    [Fact]
    public async Task Analyze_PartialSnapshot_ReturnsCalculatedCountsAndSeparateProviderMetrics()
    {
        StubReportGenerator generator = new();
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(generator);
        using HttpResponseMessage response = await host.Client.PostAsJsonAsync("/api/v1/analyze", AnalysisSamples.Request());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ResearcherAnalysisReport report = (await response.Content.ReadFromJsonAsync<ResearcherAnalysisReport>())!;
        Assert.Equal(3, report.Activity.SubmittedPublicationCount);
        Assert.Equal(2, report.Activity.PublicationsByYear[2024]);
        Assert.Equal(1, report.Activity.PublicationsByCategory["Book"]);
        Assert.True(report.Coverage.IsPartial);
        Assert.Equal(4, report.Coverage.TotalPublicationCount);
        Assert.Equal(1, report.Coverage.PublicationsWithAbstract);
        Assert.Equal(1, report.Coverage.PublicationsWithoutYear);
        Assert.Equal(12, report.CitationMetrics[0].CitationCount);
        Assert.Equal(0, report.CitationMetrics[1].CitationCount);
        Assert.Null(report.CitationMetrics[1].HIndex);
        Assert.Equal("p1", report.Findings.ResearchFocus.Single().Evidence.Single().PublicationId);
        Assert.Equal(1, generator.Calls);
    }

    [Theory]
    [InlineData("duplicate-id")]
    [InlineData("missing-title")]
    [InlineData("null-publication")]
    [InlineData("null-metric")]
    [InlineData("empty-publications")]
    [InlineData("too-many-publications")]
    [InlineData("too-much-text")]
    [InlineData("negative-citations")]
    [InlineData("missing-snapshot-date")]
    [InlineData("inconsistent-total")]
    public async Task Analyze_InvalidSnapshot_RejectsBeforeCallingAi(string problem)
    {
        AnalyzeResearcherRequest request = AnalysisSamples.Request();
        switch (problem)
        {
            case "duplicate-id": request.Publications[1].Id = "p1"; break;
            case "missing-title": request.Publications[0].Title = " "; break;
            case "null-publication": request.Publications.Add(null!); break;
            case "null-metric": request.CitationMetrics.Add(null!); break;
            case "empty-publications": request.Publications.Clear(); break;
            case "too-many-publications":
                request.Publications = Enumerable.Range(0, 101).Select(index => new AnalysisPublication
                { Id = $"p{index}", Title = "Synthetic title", Sources = "ORCID" }).ToList();
                request.TotalPublicationCount = 101;
                break;
            case "too-much-text":
                request.Publications = Enumerable.Range(0, 6).Select(index => new AnalysisPublication
                { Id = $"p{index}", Title = "Synthetic title", Sources = "ORCID", Abstract = new string('a', 11000) }).ToList();
                request.TotalPublicationCount = 6;
                break;
            case "negative-citations": request.CitationMetrics[0].CitationCount = -1; break;
            case "missing-snapshot-date": request.SnapshotAt = default; break;
            case "inconsistent-total": request.TotalPublicationCount = 1; break;
        }
        StubReportGenerator generator = new();
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(generator);
        using HttpResponseMessage response = await host.Client.PostAsJsonAsync("/api/v1/analyze", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, generator.Calls);
    }

    [Theory]
    [InlineData("unknown", "title", "Coastal water monitoring", false, "UnknownPublication")]
    [InlineData("p1", "title", "This quotation was invented", false, "QuoteMismatch")]
    [InlineData("p1", "title", "Coastal water monitoring", true, "WritingEvidenceNotAbstract")]
    [InlineData("p2", "abstract", "This paper has no abstract", true, "QuoteMismatch")]
    public async Task Analyze_UnsupportedEvidence_DoesNotReturnReport(string id, string field, string quote, bool writing, string reason)
    {
        StubReportGenerator generator = new() { Result = AnalysisSamples.Findings(id, field, quote, writing) };
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(generator);
        using HttpResponseMessage response = await host.Client.PostAsJsonAsync("/api/v1/analyze", AnalysisSamples.Request());
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(quote, body);
        using JsonDocument problem = JsonDocument.Parse(body);
        Assert.Equal(reason, problem.RootElement.GetProperty("reason").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("detail").GetString()));
    }

    [Theory]
    [InlineData(AnalysisFailure.OutputLimit)]
    [InlineData(AnalysisFailure.IncompleteOutput)]
    [InlineData(AnalysisFailure.InvalidJson)]
    public async Task Analyze_InvalidModelOutput_ReturnsDiagnosticWithoutResearchText(AnalysisFailure reason)
    {
        StubReportGenerator generator = new() { Error = new InvalidAnalysisException(reason) };
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(generator);
        using HttpResponseMessage response = await host.Client.PostAsJsonAsync("/api/v1/analyze", AnalysisSamples.Request());
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        using JsonDocument problem = JsonDocument.Parse(body);
        Assert.Equal(reason.ToString(), problem.RootElement.GetProperty("reason").GetString());
        Assert.Equal(new InvalidAnalysisException(reason).Message, problem.RootElement.GetProperty("detail").GetString());
        Assert.DoesNotContain("Synthetic Researcher", body);
        Assert.DoesNotContain("Coastal water monitoring", body);
    }

    [Fact]
    public async Task Analyze_VerbatimAbstractEvidence_ReturnsWritingObservation()
    {
        StubReportGenerator generator = new()
        { Result = AnalysisSamples.Findings("p1", "abstract", "Further validation is needed.", writing: true) };
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(generator);
        using HttpResponseMessage response = await host.Client.PostAsJsonAsync("/api/v1/analyze", AnalysisSamples.Request());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ResearcherAnalysisReport report = (await response.Content.ReadFromJsonAsync<ResearcherAnalysisReport>())!;
        Assert.Single(report.Findings.WritingObservations);
    }

    [Fact]
    public async Task Analyze_MissingProviderConfiguration_ReturnsServiceUnavailable()
    {
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync();
        using HttpResponseMessage response = await host.Client.PostAsJsonAsync("/api/v1/analyze", AnalysisSamples.Request());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("Ai:Model", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Analyze_InvalidAccessKey_DoesNotCallAi()
    {
        StubReportGenerator generator = new();
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(generator);
        host.Client.DefaultRequestHeaders.Remove("X-Analysis-Key");
        using HttpResponseMessage response = await host.Client.PostAsJsonAsync("/api/v1/analyze", AnalysisSamples.Request());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, generator.Calls);
    }

    [Fact]
    public async Task Analyze_ProductionWithoutAccessKey_DoesNotCallAi()
    {
        StubReportGenerator generator = new();
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(generator, configureAccessKey: false, environment: "Production");
        using HttpResponseMessage response = await host.Client.PostAsJsonAsync("/api/v1/analyze", AnalysisSamples.Request());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, generator.Calls);
    }

    [Theory]
    [InlineData(false, HttpStatusCode.BadGateway)]
    [InlineData(true, HttpStatusCode.GatewayTimeout)]
    public async Task Analyze_ProviderFailure_ReturnsSanitizedError(bool timeout, HttpStatusCode expected)
    {
        StubReportGenerator generator = new()
        { Error = timeout ? new TaskCanceledException("sensitive provider body") : new HttpRequestException("sensitive provider body") };
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(generator);
        using HttpResponseMessage response = await host.Client.PostAsJsonAsync("/api/v1/analyze", AnalysisSamples.Request());
        Assert.Equal(expected, response.StatusCode);
        Assert.DoesNotContain("sensitive", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Analyze_LocalDevelopmentWithoutKeyAndWithoutAbstracts_ReturnsLimitedReport()
    {
        AnalyzeResearcherRequest request = AnalysisSamples.Request();
        request.Language = "tr";
        request.TotalPublicationCount = request.Publications.Count;
        request.Publications.ForEach(publication => publication.Abstract = null);
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(new StubReportGenerator(),
            configureAccessKey: false, environment: "Development");
        host.Client.DefaultRequestHeaders.Remove("X-Analysis-Key");
        using HttpResponseMessage response = await host.Client.PostAsJsonAsync("/api/v1/analyze", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ResearcherAnalysisReport report = (await response.Content.ReadFromJsonAsync<ResearcherAnalysisReport>())!;
        Assert.False(report.Coverage.IsPartial);
        Assert.Equal(0, report.Coverage.PublicationsWithAbstract);
        Assert.Empty(report.Findings.WritingObservations);
        Assert.Contains(report.Coverage.Limitations, limitation => limitation.Contains("Tam metin"));
    }

    [Fact]
    public async Task Analyze_OversizedBody_RejectsBeforeCallingAi()
    {
        StubReportGenerator generator = new();
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(generator);
        using StringContent content = new("{\"researcherName\":\"" + new string('a', 520000) + "\"}",
            System.Text.Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await host.Client.PostAsync("/api/v1/analyze", content);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(0, generator.Calls);
    }

    [Fact]
    public async Task Health_WithoutDatabaseOrAiCredentials_ReturnsRunning()
    {
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync();
        using HttpResponseMessage response = await host.Client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
