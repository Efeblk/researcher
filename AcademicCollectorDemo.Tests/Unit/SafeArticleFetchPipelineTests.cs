using System.Net;
using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;
using Microsoft.Extensions.Options;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class SafeArticleFetchPipelineTests
{
    [Theory]
    [InlineData("/paper.pdf", "https://example.org/paper.pdf")]
    [InlineData("paper.pdf", "https://example.org/articles/paper.pdf")]
    [InlineData("//cdn.example.org/paper.pdf", "https://cdn.example.org/paper.pdf")]
    [InlineData("http://cdn.example.org/paper.pdf", "http://cdn.example.org/paper.pdf")]
    public async Task FetchPdfAsync_LandingPdfLink_FollowsBoundedLink(string link, string expectedUri)
    {
        Queue<HttpResponseMessage> responses = new([
            Response(HttpStatusCode.OK, "text/html", $"<meta name=\"citation_pdf_url\" content=\"{link}\">"),
            Response(HttpStatusCode.OK, "application/pdf", "%PDF-synthetic")]);
        SafeArticleFetcher fetcher = Create(responses, 1024);

        var result = await fetcher.FetchPdfAsync(new Uri("https://example.org/articles/landing"), default);

        Assert.Equal(expectedUri, result.FinalUri.ToString());
        Assert.True(result.Bytes.AsSpan().StartsWith("%PDF-"u8));
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("http://127.0.0.1/paper.pdf")]
    public async Task FetchPdfAsync_LandingUnsafeAbsoluteLink_Rejects(string link)
    {
        Queue<HttpResponseMessage> responses = new([
            Response(HttpStatusCode.OK, "text/html", $"<a href=\"{link}\">PDF</a>")]);
        SafeArticleFetcher fetcher = Create(responses, 1024);

        await Assert.ThrowsAsync<ArticleSourceException>(() =>
            fetcher.FetchPdfAsync(new Uri("https://example.org/articles/landing"), default));
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
