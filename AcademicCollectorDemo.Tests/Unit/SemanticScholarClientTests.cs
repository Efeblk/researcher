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
}
