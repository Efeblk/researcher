using System.Net;
using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries.Enrichment;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Crossref;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using AcademicCollectorDemo.Tests.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class ArticleMetadataEnricherTests
{
    [Fact]
    public async Task EnrichAsync_AllProviders_ReturnsAbstractAndUnpaywallPdfAndLanding()
    {
        using StubHttpHandler handler = new(request => request.RequestUri!.Host switch
        {
            "openalex.test" => StubHttpHandler.Json("""
                {"results":[{"id":"https://openalex.org/W1","doi":"https://doi.org/10.1234/test",
                "abstract_inverted_index":{"Measured":[0],"result.":[1]},
                "primary_location":{"landing_page_url":"https://journal.test/article"}}]}
                """),
            "crossref.test" => StubHttpHandler.Json("""
                {"message":{"DOI":"10.1234/test","abstract":"<jats:p>Crossref fallback.</jats:p>"}}
                """),
            "unpaywall.test" => StubHttpHandler.Json("""
                {"doi":"10.1234/test","best_oa_location":{"url_for_pdf":"https://repo.test/paper.pdf",
                "url_for_landing_page":"https://repo.test/paper"},"oa_locations":[]}
                """),
            _ => throw new InvalidOperationException(request.RequestUri.ToString())
        });
        using HttpClient http = new(handler);
        using MemoryCache cache = new(new MemoryCacheOptions());
        IConfiguration configuration = Configuration(includeEmail: true, includeApiKey: true);
        ArticleMetadataEnricher enricher = Create(http, configuration, cache);

        ArticleMetadataResult result = await enricher.EnrichAsync("p1", "DOI:10.1234/TEST");

        Assert.Equal("Measured result.", result.Abstract);
        Assert.Contains(result.Sources, source => source.Url == "https://repo.test/paper.pdf" && source.Kind == "Pdf");
        Assert.Contains(result.Sources, source => source.Url == "https://repo.test/paper" && source.Kind == "Landing");
        Assert.Contains("OpenAlex", result.Status);
        Assert.Equal(3, handler.RequestCount);
    }

    [Fact]
    public async Task EnrichAsync_MissingUnpaywallEmail_SkipsProviderAndCachesNegativeResult()
    {
        using StubHttpHandler handler = new(request => request.RequestUri!.Host switch
        {
            "openalex.test" => StubHttpHandler.Json("{\"results\":[]}"),
            "crossref.test" => new(HttpStatusCode.NotFound),
            "unpaywall.test" => throw new InvalidOperationException("Unpaywall must not be called."),
            _ => throw new InvalidOperationException(request.RequestUri.ToString())
        });
        using HttpClient http = new(handler);
        using MemoryCache cache = new(new MemoryCacheOptions());
        IConfiguration configuration = Configuration(includeEmail: false);
        ArticleMetadataEnricher enricher = Create(http, configuration, cache);

        ArticleMetadataResult first = await enricher.EnrichAsync("p1", "10.1234/missing");
        ArticleMetadataResult second = await enricher.EnrichAsync("p2", "10.1234/missing");

        Assert.Equal("NotFound;UnpaywallNotConfigured", first.Status);
        Assert.Equal(first, second);
        Assert.NotSame(first, second);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task EnrichAsync_DoiMismatch_RejectsMetadataAndDoesNotCacheFailure()
    {
        using StubHttpHandler handler = new(request => request.RequestUri!.Host switch
        {
            "openalex.test" => StubHttpHandler.Json("""
                {"results":[{"id":"W1","doi":"10.1234/other","abstract_inverted_index":{"Wrong":[0]}}]}
                """),
            "crossref.test" => StubHttpHandler.Json("{\"message\":{\"DOI\":\"10.1234/other\"}}"),
            "unpaywall.test" => StubHttpHandler.Json("{\"doi\":\"10.1234/other\"}"),
            _ => throw new InvalidOperationException(request.RequestUri.ToString())
        });
        using HttpClient http = new(handler);
        using MemoryCache cache = new(new MemoryCacheOptions());
        IConfiguration configuration = Configuration(includeEmail: true);
        ArticleMetadataEnricher enricher = Create(http, configuration, cache);

        ArticleMetadataResult first = await enricher.EnrichAsync("p1", "10.1234/requested");
        ArticleMetadataResult second = await enricher.EnrichAsync("p1", "10.1234/requested");

        Assert.Equal("Unavailable", first.Status);
        Assert.Equal("Unavailable", second.Status);
        Assert.Null(first.Abstract);
        Assert.Empty(first.Sources);
        Assert.Equal(6, handler.RequestCount);
    }

    [Fact]
    public async Task EnrichAsync_TooManyRequests_DoesNotCacheFailure()
    {
        using StubHttpHandler handler = new(_ => new(HttpStatusCode.TooManyRequests));
        using HttpClient http = new(handler);
        using MemoryCache cache = new(new MemoryCacheOptions());
        IConfiguration configuration = Configuration(includeEmail: true);
        ArticleMetadataEnricher enricher = Create(http, configuration, cache);

        Assert.Equal("Unavailable", (await enricher.EnrichAsync("p1", "10.1234/rate-limited")).Status);
        Assert.Equal("Unavailable", (await enricher.EnrichAsync("p1", "10.1234/rate-limited")).Status);

        Assert.Equal(6, handler.RequestCount);
    }

    [Fact]
    public async Task EnrichAsync_PreCancelledToken_DoesNotDispatchProviderRequest()
    {
        using StubHttpHandler handler = new(_ => throw new InvalidOperationException("Must not dispatch."));
        using HttpClient http = new(handler);
        using MemoryCache cache = new(new MemoryCacheOptions());
        ArticleMetadataEnricher enricher = Create(http, Configuration(includeEmail: true), cache);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            enricher.EnrichAsync("p1", "10.1234/cancelled", cancellation.Token));
        Assert.Equal(0, handler.RequestCount);
    }

    private static ArticleMetadataEnricher Create(
        HttpClient http,
        IConfiguration configuration,
        IMemoryCache cache)
    {
        ArticleMetadataEnrichmentOptions options = new()
        {
            PositiveCacheMinutes = 60,
            NegativeCacheMinutes = 5,
            RequestTimeoutSeconds = 5,
            MaximumResponseBytes = 1024 * 1024,
            MaximumCandidates = 16
        };
        return new(
            new OpenAlexClient(http, configuration),
            new CrossrefClient(http, configuration),
            new SingleClientFactory(http),
            configuration,
            cache,
            Options.Create(options));
    }

    private static IConfiguration Configuration(bool includeEmail, bool includeApiKey = false)
    {
        Dictionary<string, string?> settings = new()
        {
            ["OpenAlex:ApiBaseUrl"] = "https://openalex.test",
            ["Crossref:ApiBaseUrl"] = "https://crossref.test",
            ["Unpaywall:ApiBaseUrl"] = "https://unpaywall.test"
        };
        if (includeEmail)
        {
            settings["Unpaywall:Email"] = "synthetic@example.test";
        }

        if (includeApiKey)
        {
            settings["OpenAlex:ApiKey"] = "synthetic-key";
        }

        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
