using System.Net;
using System.Text;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.SemanticScholar;
using Microsoft.Extensions.Options;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class SemanticScholarClientTests
{
    [Fact]
    public void NormalizeDoi_UrlAndMixedCase_ReturnsCanonicalDoi()
    {
        Assert.Equal("10.1000/example", SemanticScholarClient.NormalizeDoi(" HTTPS://DOI.ORG/10.1000/Example "));
    }

    [Fact]
    public async Task GetAsync_NullMetadataAndDuplicatePages_PreservesNullsAndDeduplicates()
    {
        QueueHandler handler = new(
            Json("""{"paperId":"target","externalIds":{"DOI":"10.1000/example"},"title":"Target","abstract":null,"year":null,"citationCount":null}"""),
            Json("""{"total":3,"next":2,"data":[{"contexts":[],"contextsWithIntent":[{"context":"Evidence A","intents":["background"]}],"intents":[],"isInfluential":null,"citingPaper":{"paperId":"c1","title":"Citing","externalIds":null,"authors":null}},{"contexts":["Plain context"],"contextsWithIntent":[],"intents":null,"isInfluential":false,"citingPaper":{"paperId":"c1","title":"Duplicate","externalIds":null}}]}"""),
            Json("""{"total":3,"data":[{"contexts":[],"contextsWithIntent":[],"intents":[],"isInfluential":true,"citingPaper":{"paperId":"c2","title":null,"externalIds":{"DOI":"10.2/CITING"}}}]}"""));
        SemanticScholarClient client = Create(handler, maximum: 3, pageSize: 2);

        SemanticScholarSnapshot result = await client.GetAsync("10.1000/example");

        Assert.Null(result.Paper.Abstract);
        Assert.Null(result.Paper.Year);
        Assert.Null(result.Paper.CitationCount);
        Assert.True(result.Paper.CitationsComplete);
        Assert.Equal(2, result.Citations.Count);
        Assert.Equal("Evidence A", result.Citations[0].Contexts[0].Context);
        Assert.Equal("[\"background\"]", result.Citations[0].Contexts[0].IntentsJson);
        Assert.Equal("10.2/citing", result.Citations[1].CitingDoi);
    }

    [Fact]
    public async Task GetAsync_NotFound_ReturnsNegativeCacheWithoutCitations()
    {
        SemanticScholarSnapshot result = await Create(new QueueHandler(new HttpResponseMessage(HttpStatusCode.NotFound))).GetAsync("10.1/missing");
        Assert.False(result.Paper.Found);
        Assert.Empty(result.Citations);
    }

    [Fact]
    public async Task GetPaperAsync_RateLimited_PreservesStatusAndRetryAfter()
    {
        DateTime before = DateTime.UtcNow.AddMinutes(4);
        HttpResponseMessage response = new(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new(TimeSpan.FromMinutes(5));

        SemanticScholarRequestException exception = await Assert.ThrowsAsync<SemanticScholarRequestException>(
            () => Create(new QueueHandler(response)).GetPaperAsync("10.1/rate-limited"));

        Assert.Equal("RateLimited", exception.ErrorCode);
        Assert.Equal(429, exception.ProviderHttpStatusCode);
        Assert.True(exception.Retryable);
        Assert.InRange(exception.RetryAt!.Value, before, DateTime.UtcNow.AddMinutes(6));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task GetPaperAsync_AuthenticationFailure_IsNonRetryable(HttpStatusCode statusCode)
    {
        SemanticScholarRequestException exception = await Assert.ThrowsAsync<SemanticScholarRequestException>(
            () => Create(new QueueHandler(new HttpResponseMessage(statusCode))).GetPaperAsync("10.1/auth"));

        Assert.Equal("Unauthorized", exception.ErrorCode);
        Assert.Equal((int)statusCode, exception.ProviderHttpStatusCode);
        Assert.False(exception.Retryable);
    }

    [Fact]
    public async Task GetPaperAsync_UpstreamUnavailable_Preserves503()
    {
        SemanticScholarRequestException exception = await Assert.ThrowsAsync<SemanticScholarRequestException>(
            () => Create(new QueueHandler(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))
                .GetPaperAsync("10.1/unavailable"));

        Assert.Equal("Unavailable", exception.ErrorCode);
        Assert.Equal(503, exception.ProviderHttpStatusCode);
        Assert.True(exception.Retryable);
    }

    [Fact]
    public async Task GetCitationPageAsync_LocalDeferral_OmitsProviderStatusAndPreservesRetryAt()
    {
        DateTime retryAt = DateTime.UtcNow.AddMinutes(2);
        HttpResponseMessage response = new(HttpStatusCode.TooManyRequests);
        response.Headers.Add("X-Academic-Local-Deferral", "true");
        response.Headers.RetryAfter = new(new DateTimeOffset(retryAt));

        SemanticScholarRequestException exception = await Assert.ThrowsAsync<SemanticScholarRequestException>(
            () => Create(new QueueHandler(response)).GetCitationPageAsync("paper", 0, 10));

        Assert.Equal("LocallyLimited", exception.ErrorCode);
        Assert.Null(exception.ProviderHttpStatusCode);
        Assert.Equal(retryAt, exception.RetryAt!.Value, TimeSpan.FromSeconds(1));
        Assert.True(exception.Retryable);
    }

    [Fact]
    public async Task GetPaperAsync_TransportFailure_UsesSafeDiagnostics()
    {
        SemanticScholarClient client = new(new HttpClient(new ThrowingHandler(
            new HttpRequestException("https://secret.test/?api-key=secret"))),
            Options.Create(new SemanticScholarOptions { ApiBaseUrl = "https://example.test" }));

        SemanticScholarRequestException exception = await Assert.ThrowsAsync<SemanticScholarRequestException>(
            () => client.GetPaperAsync("10.1/transport"));

        Assert.Equal("TransportError", exception.ErrorCode);
        Assert.Null(exception.ProviderHttpStatusCode);
        Assert.True(exception.Retryable);
        Assert.DoesNotContain("secret", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetPaperAsync_Timeout_IsDistinctFromCallerCancellation()
    {
        SemanticScholarClient timedOut = new(new HttpClient(new ThrowingHandler(new TaskCanceledException("timeout"))),
            Options.Create(new SemanticScholarOptions { ApiBaseUrl = "https://example.test" }));
        SemanticScholarRequestException timeout = await Assert.ThrowsAsync<SemanticScholarRequestException>(
            () => timedOut.GetPaperAsync("10.1/timeout"));
        Assert.Equal("Timeout", timeout.ErrorCode);
        Assert.Null(timeout.ProviderHttpStatusCode);

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        SemanticScholarClient cancelled = new(new HttpClient(new CancellationHandler()),
            Options.Create(new SemanticScholarOptions { ApiBaseUrl = "https://example.test" }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cancelled.GetPaperAsync("10.1/cancelled", cancellation.Token));
    }

    private static SemanticScholarClient Create(QueueHandler handler, int maximum = 10, int pageSize = 10) =>
        new(new HttpClient(handler), Options.Create(new SemanticScholarOptions
        { ApiBaseUrl = "https://example.test/graph/v1", MaximumCitationsPerPaper = maximum, CitationPageSize = pageSize }));
    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
    { Content = new StringContent(value, Encoding.UTF8, "application/json") };

    private sealed class QueueHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_responses.Dequeue());
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(exception);
    }

    private sealed class CancellationHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromCanceled<HttpResponseMessage>(cancellationToken);
    }
}
