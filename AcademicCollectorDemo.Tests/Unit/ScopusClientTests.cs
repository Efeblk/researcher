using System.Net;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Scopus;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Tests.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class ScopusClientTests
{
    [Fact]
    public async Task FillResearcherAsync_AuthenticatedCursorPages_MapsProfileAndWorks()
    {
        List<HttpRequestMessage> requests = [];
        Queue<string> responses = new([
            AuthorResponse,
            SearchResponse("2", "next-token", Entry("SCOPUS_ID:11", "2-s2.0-11", "First")),
            SearchResponse("2", null, Entry("SCOPUS_ID:12", "2-s2.0-12", "Second"))
        ]);
        StubHttpHandler handler = new(request =>
        {
            HttpRequestMessage copy = new(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
                copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
            requests.Add(copy);
            return StubHttpHandler.Json(responses.Dequeue());
        });
        Researcher researcher = new() { PersonelId = "P-1", ScopusId = " 57200000001 " };

        await Client(handler).FillResearcherAsync(researcher, researcher.ScopusId);

        Assert.Equal(3, requests.Count);
        Assert.All(requests, request =>
        {
            Assert.Equal("synthetic-api-key", request.Headers.GetValues("X-ELS-APIKey").Single());
            Assert.Equal("synthetic-inst-token", request.Headers.GetValues("X-ELS-Insttoken").Single());
            Assert.DoesNotContain("synthetic", request.RequestUri!.AbsoluteUri);
        });
        Assert.Contains("cursor=%2A", requests[1].RequestUri!.Query);
        Assert.Contains("cursor=next-token", requests[2].RequestUri!.Query);
        Assert.Equal("Synthetic Researcher", researcher.ScopusProfile!.DisplayName);
        Assert.Equal("Synthetic University", researcher.ScopusProfile.CurrentAffiliation);
        Assert.Equal(" 57200000001 ", researcher.ScopusId);
        Assert.Equal(17, researcher.ScopusProfile.HIndex);
        Assert.Equal(2, researcher.ScopusProfile.Works!.Count);
        ScopusWork work = researcher.ScopusProfile.Works[0];
        Assert.Equal("10.1234/example", work.Doi);
        Assert.Equal("Ada Example, Turing Alan", work.Authors);
        Assert.DoesNotContain("secret-echo", work.RawDataJson);
        Assert.DoesNotContain("secret-echo", work.Url);
    }

    [Fact]
    public async Task FillResearcherAsync_MalformedSecondPage_RetainsCompleteSavedProfile()
    {
        ScopusProfile saved = new()
        {
            ScopusAuthorId = "57200000001", LastUpdatedAt = DateTime.UtcNow,
            RawDataJson = "{}", SearchPagesJson = "[]", Works = [new() { ScopusWorkId = "old" }]
        };
        Researcher researcher = new() { ScopusId = "57200000001", ScopusProfile = saved };
        Queue<string> responses = new([
            AuthorResponse,
            SearchResponse("2", "same", Entry("SCOPUS_ID:11", "2-s2.0-11", "First")),
            "{\"search-results\":{\"entry\":[]}}"
        ]);
        StubHttpHandler handler = new(_ => StubHttpHandler.Json(responses.Dequeue()));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Client(handler).FillResearcherAsync(researcher, "57200000001"));

        Assert.Same(saved, researcher.ScopusProfile);
        Assert.Equal("old", researcher.ScopusProfile.Works!.Single().ScopusWorkId);
    }

    [Fact]
    public async Task FillResearcherAsync_HttpError_DoesNotExposeProviderBody()
    {
        StubHttpHandler handler = new(_ => new(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"error\":\"raw-secret-provider-error\"}")
        });

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            Client(handler).FillResearcherAsync(new(), "57200000001"));

        Assert.DoesNotContain("raw-secret", exception.Message);
        Assert.Contains("HTTP 401", exception.Message);
    }

    private static ScopusClient Client(HttpMessageHandler handler)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Scopus:ApiBaseUrl"] = "https://scopus.test/content/",
                ["Scopus:ApiKey"] = "synthetic-api-key",
                ["Scopus:InstToken"] = "synthetic-inst-token",
                ["Scopus:MaximumPages"] = "3"
            }).Build();
        return new(new HttpClient(handler), configuration);
    }

    private static string SearchResponse(string total, string? next, string entry) =>
        "{\"search-results\":{\"opensearch:totalResults\":\"" + total +
        "\",\"cursor\":" + (next is null ? "{}" : "{\"@next\":\"" + next + "\"}") +
        ",\"entry\":[" + entry + "]}}";

    private static string Entry(string id, string eid, string title) => $$"""
        {"dc:identifier":"{{id}}","eid":"{{eid}}","dc:title":"{{title}}",
         "prism:coverDate":"2025-03-04","prism:doi":"10.1234/example",
         "prism:publicationName":"Synthetic Journal","subtypeDescription":"Article",
         "citedby-count":"9","openaccess":"1",
         "author":[{"authname":"Ada Example"},{"given-name":"Turing","surname":"Alan"}],
         "link":[{"@ref":"scopus","@href":"https://www.scopus.com/record?eid={{eid}}&apiKey=secret-echo"}]}
        """;

    private const string AuthorResponse = """
        {"author-retrieval-response":{"coredata":{"dc:identifier":"AUTHOR_ID:57200000001",
          "document-count":"2","citation-count":"30","cited-by-count":"20"},
          "h-index":"17","author-profile":{"preferred-name":{"indexed-name":"Synthetic Researcher"},
          "affiliation-current":{"affiliation":{"affiliation-name":"Synthetic University"}}}}}
        """;
}
