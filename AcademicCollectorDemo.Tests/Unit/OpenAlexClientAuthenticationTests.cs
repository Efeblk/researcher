using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Tests.Infrastructure;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class OpenAlexClientAuthenticationTests
{
    private const int TwoMegabytes = 2 * 1024 * 1024;

    [Fact]
    public async Task FillResearcherAsync_ApiKey_UsesBearerOnEveryRequestWithoutQuerySecret()
    {
        List<HttpRequestMessage> requests = [];
        StubHttpHandler handler = new(request =>
        {
            HttpRequestMessage copy = new(request.Method, request.RequestUri);
            copy.Headers.Authorization = request.Headers.Authorization;
            requests.Add(copy);
            return request.RequestUri!.AbsolutePath.EndsWith("/authors")
                ? StubHttpHandler.Json("""
                    {"results":[{"id":"https://openalex.org/A1","display_name":"Synthetic",
                    "works_count":0,"cited_by_count":0,"summary_stats":{"h_index":0,"i10_index":0}}]}
                    """)
                : StubHttpHandler.Json("""{"results":[],"meta":{"next_cursor":null}}""");
        });
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["OpenAlex:ApiBaseUrl"] = "https://openalex.test",
                ["OpenAlex:ApiKey"] = "synthetic-openalex-key"
            }).Build();
        Researcher researcher = new() { Orcid = "0000-0002-1825-009X" };

        await new OpenAlexClient(new HttpClient(handler), configuration).FillResearcherAsync(researcher);

        Assert.Equal(2, requests.Count);
        Assert.All(requests, request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("synthetic-openalex-key", request.Headers.Authorization.Parameter);
            Assert.DoesNotContain("api_key", request.RequestUri!.Query, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("synthetic-openalex-key", request.RequestUri.AbsoluteUri);
        });
    }

    [Fact]
    public async Task FillResearcherAsync_ResponseAboveSharedLimit_PreservesCompletePayload()
    {
        string padding = new('x', TwoMegabytes);
        string firstWorksPage = $$$"""
            {"results":[{"id":"https://openalex.org/W1","display_name":"First work",
            "padding":"{{{padding}}}"}],"meta":{"next_cursor":"page-2"}}
            """;
        int worksRequestCount = 0;
        StubHttpHandler handler = new(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/authors")
                ? StubHttpHandler.Json("""
                    {"results":[{"id":"https://openalex.org/A1","display_name":"Synthetic",
                    "works_count":2,"cited_by_count":0,"summary_stats":{"h_index":0,"i10_index":0}}]}
                    """)
                : StubHttpHandler.Json(worksRequestCount++ == 0
                    ? firstWorksPage
                    : """{"results":[{"id":"https://openalex.org/W2","display_name":"Second work"}],"meta":{"next_cursor":null}}"""));
        IConfiguration configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["ProviderResponses:MaximumResponseBytes"] = TwoMegabytes.ToString()
        });
        Researcher researcher = new() { Orcid = "0000-0002-1825-009X" };

        await new OpenAlexClient(new HttpClient(handler), configuration).FillResearcherAsync(researcher);

        Assert.NotNull(researcher.OpenAlexProfile);
        Assert.Equal(2, researcher.OpenAlexProfile.Works!.Count);
        Assert.Equal(["https://openalex.org/W1", "https://openalex.org/W2"],
            researcher.OpenAlexProfile.Works.Select(work => work.OpenAlexWorkId));
        Assert.Contains(padding, researcher.OpenAlexProfile.WorksPagesJson);
        using JsonDocument pages = JsonDocument.Parse(researcher.OpenAlexProfile.WorksPagesJson!);
        Assert.Equal(2, pages.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task FillResearcherAsync_ResponseAboveOpenAlexLimit_FailsWithoutPartialData()
    {
        const int configuredLimit = 1024;
        StubHttpHandler handler = new(_ => StubHttpHandler.Json(
            $$"""{"results":[],"padding":"{{new string('x', configuredLimit)}}"}"""));
        IConfiguration configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["OpenAlex:MaximumResponseBytes"] = configuredLimit.ToString()
        });
        Researcher researcher = new() { Orcid = "0000-0002-1825-009X" };

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            new OpenAlexClient(new HttpClient(handler), configuration).FillResearcherAsync(researcher));

        Assert.Contains("OpenAlex yanıt boyutu yapılandırılmış 1024 bayt sınırını aştı", exception.Message);
        Assert.Null(researcher.OpenAlexProfile);
    }

    [Fact]
    public async Task FillResearcherAsync_StreamedResponseAboveLimit_ReportsOpenAlexLimit()
    {
        const int configuredLimit = 1024;
        StubHttpHandler handler = new(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new UnknownLengthContent(new byte[configuredLimit + 1])
        });
        IConfiguration configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["OpenAlex:MaximumResponseBytes"] = configuredLimit.ToString()
        });
        Researcher researcher = new() { Orcid = "0000-0002-1825-009X" };

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            new OpenAlexClient(new HttpClient(handler), configuration).FillResearcherAsync(researcher));

        Assert.Contains("OpenAlex yanıt boyutu yapılandırılmış 1024 bayt sınırını aştı", exception.Message);
        Assert.Equal(HttpRequestError.Unknown, exception.HttpRequestError);
        Assert.Equal(HttpRequestError.ConfigurationLimitExceeded,
            Assert.IsType<HttpRequestException>(exception.InnerException).HttpRequestError);
    }

    [Fact]
    public async Task FillResearcherAsync_HandlerBufferLimitFailure_ReportsOpenAlexLimit()
    {
        const int configuredLimit = 1024;
        IConfiguration configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["OpenAlex:MaximumResponseBytes"] = configuredLimit.ToString()
        });
        Researcher researcher = new() { Orcid = "0000-0002-1825-009X" };

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            new OpenAlexClient(new HttpClient(new BufferLimitThrowingHandler()), configuration)
                .FillResearcherAsync(researcher));

        Assert.Contains("OpenAlex yanıt boyutu yapılandırılmış 1024 bayt sınırını aştı", exception.Message);
        Assert.Equal(HttpRequestError.ConfigurationLimitExceeded,
            Assert.IsType<HttpRequestException>(exception.InnerException).HttpRequestError);
    }

    private static IConfiguration CreateConfiguration(
        Dictionary<string, string?>? settings = null)
    {
        settings ??= [];
        settings["OpenAlex:ApiBaseUrl"] = "https://openalex.test";
        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    private sealed class UnknownLengthContent(byte[] content) : HttpContent
    {
        protected override Task SerializeToStreamAsync(
            Stream stream,
            System.Net.TransportContext? context)
        {
            return stream.WriteAsync(content).AsTask();
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class BufferLimitThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new HttpRequestException(HttpRequestError.ConfigurationLimitExceeded);
        }
    }
}
