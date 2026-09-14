using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net;
using System.Net.Http.Json;
using AcademicCollector.Analysis.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Analysis;
using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleReviews;
using AcademicCollectorDemo.Tests.Infrastructure;
using Microsoft.Extensions.Options;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class ArticleReviewServiceClientTests
{
    [Fact]
    public async Task ReviewAsync_AnalysisFailurePreservesSafeDetail()
    {
        AnalysisErrorResponse upstream = new("provider-secret-raw-content", "invalid_provider_response")
        {
            Failure = new("output_limit", "verification", "quantitative")
        };
        StubHttpHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = JsonContent.Create(upstream)
        });
        ArticleReviewServiceClient client = new(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1/")
        }, Options.Create(new AnalysisServiceOptions()));

        ArticleReviewAnalysisException exception = await Assert.ThrowsAsync<ArticleReviewAnalysisException>(() =>
            client.ReviewAsync(Request(), default));

        Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
        Assert.DoesNotContain("provider-secret", exception.Message);
        Assert.Equal("invalid_provider_response", exception.ErrorCode);
        Assert.Equal("output_limit", exception.Failure!.Reason);
        Assert.Equal("verification", exception.Failure.Stage);
        Assert.Equal("quantitative", exception.Failure.Role);
    }

    [Theory]
    [InlineData("null-review")]
    [InlineData("null-finding")]
    [InlineData("null-evidence")]
    [InlineData("null-source-id")]
    [InlineData("wrong-quote")]
    [InlineData("oversized-counter")]
    public async Task ReviewAsync_MaliciousNestedReport_FailsClosed(string mode)
    {
        ReviewArticleRequest request = Request();
        JsonObject root = JsonSerializer.SerializeToNode(Report(request),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
        JsonArray reviews = root["reviews"]!.AsArray();
        JsonArray findings = reviews[0]!["findings"]!.AsArray();
        JsonArray evidence = findings[0]!["evidence"]!.AsArray();
        switch (mode)
        {
            case "null-review": reviews[0] = null; break;
            case "null-finding": findings[0] = null; break;
            case "null-evidence": evidence[0] = null; break;
            case "null-source-id": evidence[0]!["sourceId"] = null; break;
            case "wrong-quote": evidence[0]!["quote"] = "Invented quote."; break;
            case "oversized-counter": root["coverage"]!["candidateFindings"] = 1000000; break;
        }
        StubHttpHandler handler = new(_ => StubHttpHandler.Json(root.ToJsonString()));
        ArticleReviewServiceClient client = new(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1/")
        }, Options.Create(new AnalysisServiceOptions()));

        await Assert.ThrowsAsync<JsonException>(() => client.ReviewAsync(request, default));
    }

    private static ReviewArticleRequest Request()
    {
        IReadOnlyList<ArticlePage> pages = [new(1, "A directly reported method.")];
        return new("en", "pdf", "hash", "pdf-v1", "article-specialist-review-policy-v1",
            pages, 1, false, null) { SourceSpans = ArticleSourceCatalog.Create(pages) };
    }

    private static ArticleReviewReport Report(ReviewArticleRequest request)
    {
        ArticleSourceSpan span = request.SourceSpans!.Single();
        return new(request.Language, request.SourceKind, request.SourceHash, request.ExtractionVersion,
            request.PolicyVersion, "automatically_checked", new(1, 1, 1, false, null),
            new(4, 4, 1, 1, 1, 0, 0, 0, []),
            [
                new("method", "automatically_checked",
                    [new("method:F1", "method", "source_observation", "Reported method.", null,
                        [new(span.SourceId, span.PageNumber, span.StartOffset, span.EndOffset, span.Text)])]),
                new("quantitative", "no_supported_findings", []),
                new("claim_evidence", "no_supported_findings", []),
                new("teaching", "no_supported_findings", [])
            ],
            "synthetic", "article-specialist-review-v1",
            new("automatically_checked", "synthetic", "article-specialist-review-verification-v1", true,
                "Automatic checking has limitations."))
        {
            SourceFidelity = ArticleSourceFidelity.Create(request.SourceKind, request.ExtractionVersion)
        };
    }
}
