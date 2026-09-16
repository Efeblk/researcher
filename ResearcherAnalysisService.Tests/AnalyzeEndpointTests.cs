using AcademicCollector.Analysis.Contracts;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Tests.Infrastructure;

namespace ResearcherAnalysisService.Tests;

public sealed class AnalyzeEndpointTests
{
    [Fact]
    public async Task AnalyzeAsync_PartialSnapshot_ReturnsCalculatedCountsAndSeparateProviderMetrics()
    {
        StubReportGenerator generator = new();
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(generator);
        ResearcherAnalysisReport report = await AnalyzeAsync(host, AnalysisSamples.Request());
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
    [InlineData("unknown", "title", "Coastal water monitoring", false, AnalysisFailure.UnknownPublication)]
    [InlineData("p1", "title", "This quotation was invented", false, AnalysisFailure.QuoteMismatch)]
    [InlineData("p1", "title", "Coastal water monitoring", true, AnalysisFailure.WritingEvidenceNotAbstract)]
    [InlineData("p2", "abstract", "This paper has no abstract", true, AnalysisFailure.QuoteMismatch)]
    public async Task AnalyzeAsync_UnsupportedEvidence_FailsClosed(string id, string field, string quote,
        bool writing, AnalysisFailure reason)
    {
        StubReportGenerator generator = new() { Result = AnalysisSamples.Findings(id, field, quote, writing) };
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(generator);
        InvalidAnalysisException exception = await Assert.ThrowsAsync<InvalidAnalysisException>(() =>
            AnalyzeAsync(host, AnalysisSamples.Request()));
        Assert.Equal(reason, exception.Reason);
    }

    [Theory]
    [InlineData(AnalysisFailure.OutputLimit)]
    [InlineData(AnalysisFailure.IncompleteOutput)]
    [InlineData(AnalysisFailure.InvalidJson)]
    public async Task AnalyzeAsync_InvalidModelOutput_PreservesReason(AnalysisFailure reason)
    {
        StubReportGenerator generator = new() { Error = new InvalidAnalysisException(reason) };
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(generator);
        InvalidAnalysisException exception = await Assert.ThrowsAsync<InvalidAnalysisException>(() =>
            AnalyzeAsync(host, AnalysisSamples.Request()));
        Assert.Equal(reason, exception.Reason);
    }

    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(600, true)]
    [InlineData(601, false)]
    public async Task AnalyzeAsync_VerbatimQuote_EnforcesLengthBounds(int length, bool valid)
    {
        string quote = new('a', length);
        AnalyzeResearcherRequest request = AnalysisSamples.Request();
        request.Publications[0].Abstract = new string('a', 700);
        StubReportGenerator generator = new()
        { Result = AnalysisSamples.Findings("p1", "abstract", quote, writing: true) };
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(generator);
        Task<ResearcherAnalysisReport> action = AnalyzeAsync(host, request);
        if (valid) Assert.NotNull(await action);
        else Assert.Equal(AnalysisFailure.InvalidEvidence,
            (await Assert.ThrowsAsync<InvalidAnalysisException>(() => action)).Reason);
    }

    [Theory]
    [InlineData(2000, true)]
    [InlineData(2001, false)]
    public async Task AnalyzeAsync_ObservationLength_EnforcesBackendLimit(int length, bool valid)
    {
        GeneratedFindings generated = AnalysisSamples.Findings();
        generated.Findings.ResearchFocus[0] = new AnalysisObservation
        { Observation = new string('a', length), Evidence = generated.Findings.ResearchFocus[0].Evidence };
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(
            new StubReportGenerator { Result = generated });
        Task<ResearcherAnalysisReport> action = AnalyzeAsync(host, AnalysisSamples.Request());
        if (valid) Assert.NotNull(await action);
        else Assert.Equal(AnalysisFailure.InvalidObservation,
            (await Assert.ThrowsAsync<InvalidAnalysisException>(() => action)).Reason);
    }

    [Fact]
    public async Task AnalyzeAsync_VerbatimAbstractEvidence_ReturnsWritingObservation()
    {
        StubReportGenerator generator = new()
        { Result = AnalysisSamples.Findings("p1", "abstract", "Further validation is needed.", writing: true) };
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(generator);
        ResearcherAnalysisReport report = await AnalyzeAsync(host, AnalysisSamples.Request());
        Assert.Single(report.Findings.WritingObservations);
    }

    [Fact]
    public async Task AnalyzeAsync_WithoutAbstracts_ReturnsLimitedReport()
    {
        AnalyzeResearcherRequest request = AnalysisSamples.Request();
        request.Language = "tr";
        request.TotalPublicationCount = request.Publications.Count;
        request.Publications.ForEach(publication => publication.Abstract = null);
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(new StubReportGenerator());
        ResearcherAnalysisReport report = await AnalyzeAsync(host, request);
        Assert.False(report.Coverage.IsPartial);
        Assert.Equal(0, report.Coverage.PublicationsWithAbstract);
        Assert.Empty(report.Findings.WritingObservations);
        Assert.Contains(report.Coverage.Limitations, limitation => limitation.Contains("Tam metin"));
    }

    private static Task<ResearcherAnalysisReport> AnalyzeAsync(AnalysisTestHost host,
        AnalyzeResearcherRequest request) => host.InvokeAsync<ResearcherAnalysis, ResearcherAnalysisReport>(
        engine => engine.AnalyzeAsync(request, default));
}
