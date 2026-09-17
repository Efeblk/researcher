using System.Net;
using System.Text.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.TrDizin;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Collection;
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
                    """{"hits":{"hits":[{"_id":"7","_source":{"orderTitle":"A paper","doi":"10.1/test","publicationYear":"2024","journal":{"name":"Test Journal"},"authors":[{"inPublicationName":"Ada Test"}]}}]}}"""),
                "/api/defaultSearch/publication/" => StubHttpHandler.Json(
                    """{"hits":{"total":{"value":0,"relation":"eq"},"hits":[]}}"""),
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

    [Fact]
    public async Task GetByOrcidAsync_ProjectPages_DeduplicatesMapsAndRejectsNameOnlyCandidate()
    {
        const string orcid = "0000-0002-1825-0097";
        List<Uri> requests = [];
        using HttpClient http = new(new StubHttpHandler(request =>
        {
            Uri uri = request.RequestUri!;
            requests.Add(uri);
            if (uri.AbsolutePath == "/api/public/yazar/orcid/")
                return StubHttpHandler.Json(
                    """{"id":42,"orcid":"0000-0002-1825-0097","fullName":"Ada Test","orderPublicationCount":0}""");
            if (uri.AbsolutePath == "/api/authorPublicationsById/42")
                return StubHttpHandler.Json(
                    """{"hits":{"total":{"value":0},"hits":[]}}""");
            if (uri.AbsolutePath == "/api/defaultSearch/publication/")
            {
                string[] ids = uri.Query.Contains("page=1", StringComparison.Ordinal)
                    ? Enumerable.Range(0, 100).Select(index => $"P{index}").ToArray()
                    : ["P99", "P100"];
                return StubHttpHandler.Json(JsonSerializer.Serialize(new
                {
                    hits = new
                    {
                        total = new { value = 100, relation = "gte" },
                        hits = ids.Select(id => new { _id = id }).ToArray()
                    }
                }));
            }
            if (uri.AbsolutePath.StartsWith("/api/publicationById/P", StringComparison.Ordinal))
            {
                string id = uri.Segments[^1];
                object[] researchers = id == "P100"
                    ? [new { fullName = "Ada Test" }]
                    : id == "P0"
                        ? [new { authorId = 42, duty = "Coordinator" }]
                        : [new { orcid }];
                object source = id == "P0"
                    ? new
                    {
                        documentType = "PROJECT",
                        projectNumber = "SYN-1",
                        title = "Synthetic project",
                        startedDate = "2024-01-02",
                        endDate = "2025-03-04",
                        projectGroup = "Synthetic group",
                        researchers,
                        abstracts = new[] { new { language = "eng", text = "Abstract" } },
                        keywords = new[] { "safe", "synthetic" },
                        publicationProduct = new[] { new { id = "OUTPUT-1" } },
                        attachments = new[] { new { id = "FILE-1" } }
                    }
                    : new { documentType = "PROJECT", researchers };
                return StubHttpHandler.Json(JsonSerializer.Serialize(new
                {
                    hits = new { hits = new[] { new { _id = id, _source = source } } }
                }));
            }
            throw new InvalidOperationException(uri.ToString());
        }));

        TrDizinProfile result = (await new TrDizinClient(http, Config())
            .GetByOrcidAsync(orcid))!;

        Assert.Equal(101, result.ProjectCandidateCount);
        Assert.Equal(100, result.ProjectMatchedCount);
        Assert.Equal(1, result.ProjectUnmatchedCount);
        Assert.True(result.ProjectSearchComplete);
        Assert.Equal(100, result.Projects!.Count);
        TrDizinProject project = result.Projects.Single(item => item.ProjectId == "P0");
        Assert.Equal("SYN-1", project.ProjectNumber);
        Assert.Equal("Synthetic project", project.Title);
        Assert.Equal("2024-01-02", project.StartedDate);
        Assert.Equal("2025-03-04", project.EndDate);
        Assert.Equal("Synthetic group", project.ProjectGroup);
        Assert.Equal("Coordinator", project.Duty);
        Assert.Contains("OUTPUT-1", project.OutputsJson);
        Assert.Contains("FILE-1", project.AttachmentsJson);
        using JsonDocument rawPages = JsonDocument.Parse(result.RawProjectsJson);
        Assert.Equal(2, rawPages.RootElement.GetArrayLength());
        Assert.Contains(requests, uri => uri.Query.Contains("facet-documentType=PROJECT") &&
            uri.Query.Contains("facet-authorName=Ada%20Test"));
    }

    [Fact]
    public async Task GetByOrcidAsync_ProjectInAuthorPublications_DoesNotCreateWork()
    {
        const string orcid = "0000-0002-1825-0097";
        using HttpClient http = new(new StubHttpHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/public/yazar/orcid/" => StubHttpHandler.Json(
                """{"id":42,"orcid":"0000-0002-1825-0097","fullName":"Ada Test"}"""),
            "/api/authorPublicationsById/42" => StubHttpHandler.Json(
                """{"hits":{"total":{"value":1},"hits":[{"_id":"P1"}]}}"""),
            "/api/defaultSearch/publication/" => StubHttpHandler.Json(
                """{"hits":{"total":{"value":1,"relation":"eq"},"hits":[{"_id":"P1"}]}}"""),
            "/api/publicationById/P1" => StubHttpHandler.Json(
                """{"hits":{"hits":[{"_id":"P1","_source":{"documentType":"PROJECT","title":"Project","researchers":[{"authorId":42}]}}]}}"""),
            _ => throw new InvalidOperationException(request.RequestUri.ToString())
        }));

        TrDizinProfile result = (await new TrDizinClient(http, Config())
            .GetByOrcidAsync(orcid))!;

        Assert.Empty(result.Works!);
        Assert.Equal("P1", Assert.Single(result.Projects!).ProjectId);
    }

    [Fact]
    public async Task GetByOrcidAsync_MissingResolvedName_ReportsIncompleteInsteadOfEmptySnapshot()
    {
        const string orcid = "0000-0002-1825-0097";
        using HttpClient http = new(new StubHttpHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/public/yazar/orcid/" => StubHttpHandler.Json(
                """{"id":42,"orcid":"0000-0002-1825-0097"}"""),
            "/api/authorPublicationsById/42" => StubHttpHandler.Json(
                """{"hits":{"total":{"value":0},"hits":[]}}"""),
            _ => throw new InvalidOperationException(request.RequestUri.ToString())
        }));

        ProviderCollectionException exception = await Assert.ThrowsAsync<ProviderCollectionException>(() =>
            new TrDizinClient(http, Config()).GetByOrcidAsync(orcid));

        Assert.Equal("ProjectIdentityIncomplete", exception.Code);
        Assert.Equal(0, exception.RetrievedCount);
        Assert.Null(exception.ExpectedCount);
    }

    [Fact]
    public async Task GetByOrcidAsync_ProjectLowerBoundNotMet_RejectsIncompleteSnapshot()
    {
        const string orcid = "0000-0002-1825-0097";
        using HttpClient http = new(new StubHttpHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/public/yazar/orcid/" => StubHttpHandler.Json(
                """{"id":42,"orcid":"0000-0002-1825-0097","fullName":"Ada Test"}"""),
            "/api/authorPublicationsById/42" => StubHttpHandler.Json(
                """{"hits":{"total":{"value":0},"hits":[]}}"""),
            "/api/defaultSearch/publication/" => StubHttpHandler.Json(
                """{"hits":{"total":{"value":3,"relation":"gte"},"hits":[{"_id":"P1"},{"_id":"P2"}]}}"""),
            _ => throw new InvalidOperationException(request.RequestUri.ToString())
        }));

        ProviderCollectionException exception = await Assert.ThrowsAsync<ProviderCollectionException>(() =>
            new TrDizinClient(http, Config()).GetByOrcidAsync(orcid));

        Assert.Equal("ProjectPageFailure", exception.Code);
        Assert.Equal(3, exception.ExpectedCount);
    }

    [Fact]
    public async Task GetByOrcidAsync_ProjectCancellation_Propagates()
    {
        const string orcid = "0000-0002-1825-0097";
        using CancellationTokenSource cancellation = new();
        using HttpClient http = new(new StubHttpHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/public/yazar/orcid/" => StubHttpHandler.Json(
                """{"id":42,"orcid":"0000-0002-1825-0097","fullName":"Ada Test"}"""),
            "/api/authorPublicationsById/42" => StubHttpHandler.Json(
                """{"hits":{"total":{"value":0},"hits":[]}}"""),
            "/api/defaultSearch/publication/" => StubHttpHandler.Json(
                """{"hits":{"total":{"value":1,"relation":"eq"},"hits":[{"_id":"P1"}]}}"""),
            "/api/publicationById/P1" => Cancel(cancellation),
            _ => throw new InvalidOperationException(request.RequestUri.ToString())
        }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new TrDizinClient(http, Config()).GetByOrcidAsync(orcid, cancellation.Token));
    }

    private static HttpResponseMessage Cancel(CancellationTokenSource cancellation)
    {
        cancellation.Cancel();
        throw new OperationCanceledException(cancellation.Token);
    }

    private static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["TrDizin:ApiBaseUrl"] = "https://tr.example" }).Build();
}
