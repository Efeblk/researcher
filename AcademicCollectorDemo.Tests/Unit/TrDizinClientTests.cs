using System.Net;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.TrDizin;
using AcademicCollectorDemo.Tests.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class TrDizinClientTests
{
    [Fact]
    public async Task GetByOrcidAsync_RedirectDisabled_UsesCanonicalAuthorUrlAndMapsWork()
    {
        const string orcid = "0000-0002-1825-0097";
        List<string> requestedPaths = [];
        using HttpClient http = new(new StubHttpHandler(request =>
        {
            requestedPaths.Add(request.RequestUri!.AbsolutePath);
            return request.RequestUri.AbsolutePath switch
            {
                "/api/public/yazar/orcid" => new(HttpStatusCode.MovedPermanently),
                "/api/public/yazar/orcid/" => StubHttpHandler.Json(
                    """{"id":42,"orcid":"0000-0002-1825-0097","fullName":"Ada Test","orderPublicationCount":1}"""),
                "/api/authorPublicationsById/42" => StubHttpHandler.Json(
                    """{"hits":{"total":{"value":1},"hits":[{"_id":"7","fields":{"id":["7"]}}]}}"""),
                "/api/publicationById/7" => StubHttpHandler.Json(
                    """{"hits":{"hits":[{"_source":{"orderTitle":"A paper","doi":"10.1/test","publicationYear":"2024","journal":{"name":"Test Journal"},"authors":[{"inPublicationName":"Ada Test"}]}}]}}"""),
                _ => new(HttpStatusCode.NotFound)
            };
        }));
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["TrDizin:ApiBaseUrl"] = "https://tr.example" }).Build();

        TrDizinProfile? result = await new TrDizinClient(http, configuration).GetByOrcidAsync(orcid);

        Assert.Equal(42, result!.AuthorId);
        Assert.Equal("A paper", Assert.Single(result.Works!).Title);
        Assert.Equal("/api/public/yazar/orcid/", requestedPaths[0]);
        Assert.DoesNotContain("/api/public/yazar/orcid", requestedPaths);
    }
}
