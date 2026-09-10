using System.Net;
using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;
using Microsoft.Extensions.Options;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class SafeArticleFetchPipelineTests
{
    [Fact]
    public async Task FetchPdfAsync_LandingPdfLink_FollowsBoundedLink()
    {
        Queue<HttpResponseMessage> responses = new([
            Response(HttpStatusCode.OK, "text/html", "<meta name=\"citation_pdf_url\" content=\"/paper.pdf\">"),
            Response(HttpStatusCode.OK, "application/pdf", "%PDF-synthetic")]);
        SafeArticleFetcher fetcher = Create(responses, 1024);

        var result = await fetcher.FetchPdfAsync(new Uri("https://example.org/article"), default);

        Assert.Equal("https://example.org/paper.pdf", result.FinalUri.ToString());
        Assert.True(result.Bytes.AsSpan().StartsWith("%PDF-"u8));
    }

    [Fact]
    public async Task FetchPdfAsync_OversizedBody_Rejects()
    {
        Queue<HttpResponseMessage> responses = new([Response(HttpStatusCode.OK, "application/pdf", new string('x', 200))]);
        SafeArticleFetcher fetcher = Create(responses, 100);

        await Assert.ThrowsAsync<ArticleSourceException>(() => fetcher.FetchPdfAsync(new Uri("https://example.org/paper.pdf"), default));
    }

    private static SafeArticleFetcher Create(Queue<HttpResponseMessage> responses, int maximumBytes)
    {
        ArticleSummaryOptions settings = new() { MaximumDownloadBytes = maximumBytes };
        return new(Options.Create(settings), (_, _) => Task.FromResult(new HttpClient(new QueueHandler(responses))));
    }
    private static HttpResponseMessage Response(HttpStatusCode status, string mediaType, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, mediaType) };
    private sealed class QueueHandler(Queue<HttpResponseMessage> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(responses.Dequeue());
    }
}
