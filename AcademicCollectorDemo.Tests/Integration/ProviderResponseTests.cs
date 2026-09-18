using AcademicCollectorDemo.Tests.Infrastructure;
using System.Text.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.GoogleScholar;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Orcid;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis.Collection;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Collection;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using Microsoft.Extensions.Configuration;

namespace AcademicCollectorDemo.Tests.Integration;

public sealed class ProviderResponseTests
{
    [Fact]
    public async Task CollectAsync_MissingCredentials_StopsBeforeFirstRequest()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("<Envelope />"));
        using var http = new HttpClient(handler);
        var service = new YoksisCollectionService(new(http, Config([])));

        await Assert.ThrowsAnyAsync<Exception>(() => service.CollectAsync(
            new() { TcKimlikNo = new('1', 11) }));

        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task CollectAsync_SystemicResponse_StopsRemainingCategories()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(
            System.Net.HttpStatusCode.TooManyRequests));
        using var http = new HttpClient(handler);
        var service = new YoksisCollectionService(new(http, Config(new()
        {
            ["Yoksis:Username"] = "user", ["Yoksis:Password"] = "secret"
        })));

        var response = await service.CollectAsync(new() { TcKimlikNo = new('1', 11) });

        Assert.Equal(1, handler.RequestCount);
        Assert.Single(response.Categories);
        Assert.Equal(1, response.FailedCategoryCount);
    }

    [Fact]
    public async Task CollectAsync_SystemicDetailFailure_FinalizesPartialCoverageAndStopsRequests()
    {
        int requests = 0;
        var handler = new StubHttpHandler(_ => ++requests switch
        {
            <= 4 => SoapSuccess(),
            5 => SoapSuccess("""
                <Record><YAYIN_ID> 101 </YAYIN_ID></Record>
                <Record><YAYIN_ID>102</YAYIN_ID></Record>
                <Record><YAYIN_ID>103</YAYIN_ID></Record>
                """),
            6 => SoapSuccess("<Record><YAYIN_ID>101</YAYIN_ID></Record>"),
            7 => new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests),
            _ => throw new InvalidOperationException("A request was sent after the systemic stop.")
        });
        using var http = new HttpClient(handler);
        var service = new YoksisCollectionService(new(http, YoksisConfig()));

        YoksisCollectResponse response = await service.CollectAsync(
            new() { TcKimlikNo = new('1', 11) });

        YoksisOperationResult details = Assert.Single(response.Categories,
            category => category.OperationName == "getBildiriBilgisiDetayV1");
        Assert.Equal(7, handler.RequestCount);
        Assert.False(details.IsSuccess);
        Assert.Equal(3, details.ExpectedDetailCount);
        Assert.Equal(1, details.RetrievedDetailCount);
        Assert.Equal(2, details.FailedDetailCount);
        Assert.Contains(details.FailureReasons,
            failure => failure.Code == "NotAttempted" && failure.AffectedCount == 1);
        Assert.Null(response.PublicationDetailTotalCount);
        Assert.Equal(1, response.PublicationDetailRetrievedCount);
        Assert.Equal(2, response.PublicationDetailFailedCount);
    }

    [Fact]
    public async Task CollectAsync_RequestCancellation_StopsInFlightHttpCall()
    {
        using var http = new HttpClient(new CancelAwareHttpHandler());
        var service = new YoksisCollectionService(new(http, Config(new()
        {
            ["Yoksis:Username"] = "user", ["Yoksis:Password"] = "secret"
        })));
        using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CollectAsync(
            new() { TcKimlikNo = new('1', 11) }, cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task CollectAsync_SuccessfulSoapEchoesSensitiveValues_RedactsRecordsAndRawXml()
    {
        string tc = new('1', 11);
        string username = Guid.NewGuid().ToString("N");
        string password = Guid.NewGuid().ToString("N");
        var config = Config(new() { ["Yoksis:Username"] = username, ["Yoksis:Password"] = password });
        using var http = new HttpClient(new StubHttpHandler(_ => StubHttpHandler.Json(
            $"<Envelope><Body><Response><Sonuc><SonucKod>1</SonucKod><SonucMesaj>{tc} {username} {password}</SonucMesaj></Sonuc><Record><TC_KIMLIK_NO>{tc}</TC_KIMLIK_NO><NAME>Synthetic Name</NAME></Record></Response></Body></Envelope>")));
        var response = await new YoksisCollectionService(new(http, config)).CollectAsync(new() { TcKimlikNo = tc });
        string serialized = JsonSerializer.Serialize(response);
        Assert.DoesNotContain(tc, serialized);
        Assert.DoesNotContain(username, serialized);
        Assert.DoesNotContain(password, serialized);
        Assert.Contains("Synthetic Name", serialized);
    }

    [Fact]
    public async Task FillResearcherAsync_OrcidOmitsOptionalSections_CollectsProfile()
    {
        using var http = new HttpClient(new StubHttpHandler(_ => StubHttpHandler.Json(
            """{"orcid-identifier":{"path":"0000-0001-8560-7482"},"person":{},"activities-summary":{"works":{"group":[]}}}""")));
        var researcher = new Researcher
        {
            PersonelId = "test-" + Guid.NewGuid().ToString("N"), Orcid = "0000-0001-8560-7482" };
        await new OrcidClient(http, Config([])).FillResearcherAsync(researcher);
        Assert.NotNull(researcher.OrcidProfile);
        Assert.Empty(researcher.OrcidProfile.Works!);
    }

    [Fact]
    public async Task FillResearcherAsync_OrcidVersionRelationPrecedesSelf_UsesSelfDoi()
    {
        using var http = new HttpClient(new StubHttpHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/record", StringComparison.Ordinal)
                ? StubHttpHandler.Json("""
                    {"orcid-identifier":{"path":"0000-0001-8560-7482"},"person":{},
                     "activities-summary":{"works":{"group":[{"work-summary":[{"put-code":7,"display-index":1}]}]}}}
                    """)
                : StubHttpHandler.Json("""
                    {"bulk":[{"work":{"put-code":7,"external-ids":{"external-id":[
                      {"external-id-type":"doi","external-id-value":"10.1000/parent","external-id-relationship":"version-of"},
                      {"external-id-type":"doi","external-id-value":"10.1000/work","external-id-relationship":"self"}
                    ]}}}]}
                    """)));
        Researcher researcher = new() { PersonelId = "P-1", Orcid = "0000-0001-8560-7482" };

        await new OrcidClient(http, Config([])).FillResearcherAsync(researcher);

        Assert.Equal("10.1000/work", Assert.Single(researcher.OrcidProfile!.Works!).Doi);
    }

    [Fact]
    public async Task FillResearcherAsync_ScholarMalformedTable_PreservesPreviousProfile()
    {
        var previous = new GoogleScholarProfile { DisplayName = "Saved profile", DocumentsCount = 10 };
        var researcher = new Researcher
        {
            PersonelId = "test-" + Guid.NewGuid().ToString("N"), GoogleScholarId = "AbCdEfGhIjKl", GoogleScholarProfile = previous };
        int requests = 0;
        using var http = new HttpClient(new StubHttpHandler(_ => ++requests == 1
            ? StubHttpHandler.Json("""<html><body><div id="gsc_prf_in">New profile</div></body></html>""")
            : throw new InvalidOperationException("Only one request was expected.")));
        var client = new GoogleScholarClient(http, Config([]));
        await Assert.ThrowsAsync<InvalidDataException>(
            () => client.FillResearcherAsync(researcher, researcher.GoogleScholarId));
        Assert.Same(previous, researcher.GoogleScholarProfile);
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task FillResearcherAsync_ScholarRequestId_DoesNotPopulateCanonicalIdentifier()
    {
        var researcher = new Researcher
        {
            PersonelId = "test-" + Guid.NewGuid().ToString("N")
        };
        using var http = new HttpClient(new StubHttpHandler(_ =>
            StubHttpHandler.Json("""
                <html><body><div id="gsc_prf_in">Synthetic Scholar</div>
                <table id="gsc_rsb_st"><thead><tr><th></th><th>All</th><th>Since 2021</th></tr></thead>
                <tbody><tr><td>Citations</td><td>1</td><td>1</td></tr>
                <tr><td>h-index</td><td>1</td><td>1</td></tr>
                <tr><td>i10-index</td><td>0</td><td>0</td></tr></tbody></table>
                </body></html>
                """)));
        var client = new GoogleScholarClient(http, Config([]));

        await client.FillResearcherAsync(researcher, "AbCdEfGhIjKl");

        Assert.Null(researcher.GoogleScholarId);
        Assert.Equal("Synthetic Scholar", researcher.GoogleScholarProfile!.DisplayName);
    }

    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static IConfiguration YoksisConfig() => Config(new()
    {
        ["Yoksis:Username"] = "synthetic-user",
        ["Yoksis:Password"] = "synthetic-password"
    });

    private static HttpResponseMessage SoapSuccess(string records = "") =>
        StubHttpHandler.Json($"<Envelope><Body><Response><Sonuc><SonucKod>1</SonucKod></Sonuc>{records}</Response></Body></Envelope>");

    private static HttpResponseMessage SoapResult(
        int resultCode,
        string externalCode,
        string message) => StubHttpHandler.Json(
            $"<Envelope><Body><Response><Sonuc><SonucKod>{resultCode}</SonucKod>" +
            $"<DisSistemSonucKod>{externalCode}</DisSistemSonucKod>" +
            $"<SonucMesaj>{message}</SonucMesaj></Sonuc></Response></Body></Envelope>");

    private sealed class CancelAwareHttpHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable");
        }
    }
}
