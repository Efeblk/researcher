using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Tests.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class OpenAlexClientAuthenticationTests
{
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
}
