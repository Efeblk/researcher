using System.Net;
using System.Text;
using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;
using AcademicCollectorDemo.Tests.Infrastructure;
using Microsoft.Extensions.Options;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class ArticleSummaryWorkflowTests
{
    [Fact]
    public async Task AcquireAsync_MultipleCandidates_SharesHttpRequestBudget()
    {
        int requests = 0;
        ArticleSummaryOptions values = new() { MaximumSourceRequests = 2, MaximumDownloadBytes = 65536 };
        IOptions<ArticleSummaryOptions> options = Options.Create(values);
        SafeArticleFetcher fetcher = new(options, (uri, _) =>
        {
            requests++;
            HttpResponseMessage response = uri.AbsolutePath == "/first"
                ? new(HttpStatusCode.OK)
                {
                    Content = new StringContent("<html><a href='/missing.pdf'>PDF</a></html>",
                        Encoding.UTF8, "text/html")
                }
                : new(HttpStatusCode.NotFound);
            return Task.FromResult(new HttpClient(new StubHttpHandler(_ => response)));
        });
        ArticleSummaryWorkflow workflow = new(null!, fetcher, new ArticlePdfExtractor(options),
            new ArticleHtmlExtractor(options), null!, null!, options);

        ArticleSummaryWorkflow.Acquisition result = await workflow.AcquireAsync(
            [new("first", "https://example.org/first"), new("second", "https://example.org/second")],
            "en", [], CancellationToken.None, CancellationToken.None);

        Assert.Null(result.Snapshot);
        Assert.Equal(2, requests);
    }

    [Fact]
    public async Task AcquireAsync_HtmlAbstractThenBudgetCancellation_RetainsAbstract()
    {
        const string expected = "This semantic abstract contains enough specific study detail to qualify as article evidence when full text acquisition later reaches its bounded deadline.";
        using CancellationTokenSource acquisition = new();
        ArticleSummaryOptions values = new() { MaximumSourceRequests = 2, MaximumDownloadBytes = 65536 };
        IOptions<ArticleSummaryOptions> options = Options.Create(values);
        SafeArticleFetcher fetcher = new(options, (uri, _) =>
        {
            if (uri.AbsolutePath == "/first")
            {
                var handler = new StubHttpHandler(_ => new(HttpStatusCode.OK)
                {
                    Content = new StringContent($"<html><head><meta name='citation_abstract' content='{expected}'></head><body>Landing page</body></html>", Encoding.UTF8, "text/html")
                });
                return Task.FromResult(new HttpClient(handler));
            }
            acquisition.Cancel();
            return Task.FromResult(new HttpClient(new StubHttpHandler(_ => throw new OperationCanceledException(acquisition.Token))));
        });
        ArticleSummaryWorkflow workflow = new(null!, fetcher, new ArticlePdfExtractor(options),
            new ArticleHtmlExtractor(options), null!, null!, options);
        List<string> failures = [];

        ArticleSummaryWorkflow.Acquisition result = await workflow.AcquireAsync(
            [new("first", "https://example.org/first"), new("second", "https://example.org/second")],
            "en", failures, acquisition.Token, CancellationToken.None);

        Assert.Null(result.Snapshot);
        Assert.Equal(expected, result.Abstract);
        Assert.Contains(failures, value => value.Contains("budget expired", StringComparison.Ordinal));
    }
}
