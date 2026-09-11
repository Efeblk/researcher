using System.Net;
using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;
using Microsoft.Extensions.Options;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class SafeArticleFetchPipelineTests
{
    [Fact]
    public void DiscoverPdfLinks_ReorderedMetaAndPdfQuery_ReturnsAllSafeLinks()
    {
        const string html = """<meta content="/meta.pdf?x=1" NAME="citation_pdf_url"><a href="paper.pdf?download=1">PDF</a><a href="http://127.0.0.1/private.pdf">bad</a>""";

        IReadOnlyList<Uri> result = SafeArticleFetcher.DiscoverPdfLinks(new Uri("https://example.org/articles/page"), html);

        Assert.Equal(["https://example.org/meta.pdf?x=1", "https://example.org/articles/paper.pdf?download=1"], result.Select(x => x.AbsoluteUri));
    }

    [Fact]
    public void DiscoverPdfLinks_DergiparkDownloadWithoutExtension_ReturnsLink()
    {
        IReadOnlyList<Uri> result = SafeArticleFetcher.DiscoverPdfLinks(new Uri("https://dergipark.org.tr/article"),
            "<a href='/en/download/article-file/4317678'>download</a>");

        Assert.Equal("https://dergipark.org.tr/en/download/article-file/4317678", Assert.Single(result).AbsoluteUri);
    }

    [Fact]
    public async Task FetchPdfAsync_FirstDiscoveredPdfFails_TriesNextCandidate()
    {
        Queue<HttpResponseMessage> responses = new([
            Response(HttpStatusCode.OK, "text/html", "<a href='/first.pdf'>one</a><a href='/second.pdf'>two</a>"),
            Response(HttpStatusCode.Forbidden, "text/plain", "denied"),
            Response(HttpStatusCode.OK, "application/pdf", "%PDF-working")]);
        SafeArticleFetcher fetcher = Create(responses, 1024);

        var result = await fetcher.FetchPdfAsync(new Uri("https://example.org/article"), default);

        Assert.Equal("https://example.org/second.pdf", result.FinalUri.AbsoluteUri);
    }

    [Fact]
    public async Task FetchSourceAsync_PdfCandidatesFail_ReturnsOriginalHtml()
    {
        Queue<HttpResponseMessage> responses = new([
            Response(HttpStatusCode.OK, "text/html", "<article><a href='/paper.pdf'>PDF</a></article>"),
            Response(HttpStatusCode.Forbidden, "text/plain", "denied")]);
        SafeArticleFetcher fetcher = Create(responses, 1024);

        FetchedArticleSource result = await fetcher.FetchSourceAsync(new Uri("https://example.org/article"), default);

        Assert.Equal("text/html", result.MediaType);
        Assert.Equal("https://example.org/article", result.FinalUri.AbsoluteUri);
    }

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
    public async Task FetchPdfAsync_RedirectToSpecialAddress_RejectsBeforeCreatingAnotherClient()
    {
        int clientCreations = 0;
        Queue<HttpResponseMessage> responses = new([
            new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri("http://198.18.0.1/paper.pdf") }
            }]);
        SafeArticleFetcher fetcher = new(Options.Create(new ArticleSummaryOptions()), (_, _) =>
        {
            clientCreations++;
            return Task.FromResult(new HttpClient(new QueueHandler(responses)));
        });

        await Assert.ThrowsAsync<ArticleSourceException>(() =>
            fetcher.FetchPdfAsync(new Uri("https://example.org/article"), default));

        Assert.Equal(1, clientCreations);
    }

    [Fact]
    public async Task FetchPdfAsync_OversizedBody_Rejects()
    {
        Queue<HttpResponseMessage> responses = new([Response(HttpStatusCode.OK, "application/pdf", new string('x', 200))]);
        SafeArticleFetcher fetcher = Create(responses, 100);

        await Assert.ThrowsAsync<ArticleSourceException>(() => fetcher.FetchPdfAsync(new Uri("https://example.org/paper.pdf"), default));
    }

    [Fact]
    public async Task FetchPdfAsync_HttpFailure_ReportsStatusWithoutUrl()
    {
        Queue<HttpResponseMessage> responses = new([Response(HttpStatusCode.Forbidden, "text/plain", "denied")]);
        SafeArticleFetcher fetcher = Create(responses, 1024);

        ArticleSourceException exception = await Assert.ThrowsAsync<ArticleSourceException>(() =>
            fetcher.FetchPdfAsync(new Uri("https://example.org/paper.pdf?private=value"), default));

        Assert.Equal("The source returned HTTP status 403.", exception.Message);
        Assert.DoesNotContain("example.org", exception.Message);
        Assert.DoesNotContain("private", exception.Message);
    }

    [Fact]
    public async Task FetchPdfAsync_LandingWithoutPdf_ReportsSafeCategory()
    {
        Queue<HttpResponseMessage> responses = new([Response(HttpStatusCode.OK, "text/html", "<html><a href='/login?token=secret'>Sign in</a></html>")]);
        SafeArticleFetcher fetcher = Create(responses, 1024);

        ArticleSourceException exception = await Assert.ThrowsAsync<ArticleSourceException>(() =>
            fetcher.FetchPdfAsync(new Uri("https://example.org/article?id=private"), default));

        Assert.Equal("The saved URL is a landing page without an accessible PDF link.", exception.Message);
        Assert.DoesNotContain("secret", exception.Message);
        Assert.DoesNotContain("private", exception.Message);
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
