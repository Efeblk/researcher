using AcademicCollectorDemo.Tests.Infrastructure;
using System.Text.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.GoogleScholar;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Orcid;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis.Collection;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Collection;
using Microsoft.Extensions.Configuration;

namespace AcademicCollectorDemo.Tests.Integration;

public sealed class ProviderResponseTests
{
    [Theory]
    [InlineData(429, "RateLimited")]
    [InlineData(401, "AuthenticationOrConfiguration")]
    public void ProviderCollectionException_HttpStatus_PreservesSafeCause(int status, string code)
    {
        var inner = new HttpRequestException("synthetic", null, (System.Net.HttpStatusCode)status);
        var failure = new ProviderCollectionException("PageFailure", "page failed", 2, 5,
            new InvalidOperationException("wrapper", inner));
        Assert.Equal(code, failure.CauseCode);
        Assert.Equal(2, failure.RetrievedCount);
        Assert.DoesNotContain("synthetic", failure.CauseDescription);
    }

    [Fact]
    public void ProviderCollectionException_MalformedAfterProgress_ReportsMalformed()
    {
        var failure = new ProviderCollectionException("PageFailure", "page failed", 3, 8,
            new JsonException("synthetic payload"));
        Assert.Equal("MalformedResponse", failure.CauseCode);
        Assert.Equal(3, failure.RetrievedCount);
    }

    [Fact]
    public async Task CollectAsync_MixedPublicationDetails_ReportsKnownCoverageAndGroupedReason()
    {
        string tc = new('1', 11);
        using var http = new HttpClient(new StubHttpHandler(request =>
        {
            string body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            if (body.Contains("getMakaleBilgisiV1Request", StringComparison.Ordinal) &&
                !body.Contains("Detay", StringComparison.Ordinal))
                return SoapSuccess("<Kayit><YAYIN_ID>one</YAYIN_ID></Kayit><Kayit><YAYIN_ID>two</YAYIN_ID></Kayit>");
            if (body.Contains("getMakaleBilgisiDetayV1Request", StringComparison.Ordinal) &&
                body.Contains("<P_ESER_ID>one</P_ESER_ID>", StringComparison.Ordinal))
                return SoapSuccess("<Kayit><YAYIN_ID>one</YAYIN_ID><MAKALE_ADI>One</MAKALE_ADI></Kayit>");
            if (body.Contains("getMakaleBilgisiDetayV1Request", StringComparison.Ordinal))
                return SoapResult(0, "42", "Kayıt geçici olarak kullanılamıyor");
            return SoapSuccess();
        }));

        var response = await new YoksisCollectionService(new(http, YoksisConfig())).CollectAsync(
            new() { TcKimlikNo = tc });

        Assert.Equal(2, response.PublicationDetailTotalCount);
        Assert.Equal(1, response.PublicationDetailRetrievedCount);
        Assert.Equal(1, response.PublicationDetailFailedCount);
        YoksisFailureSummary reason = Assert.Single(response.PublicationFailureReasons);
        Assert.Equal("ProviderRejected:42", reason.Code);
        Assert.Equal(1, reason.AffectedCount);
        Assert.Contains("Kayıt geçici olarak kullanılamıyor", reason.Description);
        YoksisOperationResult details = Assert.Single(response.Categories, x =>
            x.OperationName == "getMakaleBilgisiDetayV1");
        Assert.False(details.IsSuccess);
        Assert.Single(details.Records);
    }

    [Fact]
    public async Task CollectAsync_PublicationListFails_DoesNotFabricateTotalOrExposeException()
    {
        string tc = new('1', 11);
        const string sensitiveFailure = "synthetic SOAP body and identity";
        using var http = new HttpClient(new StubHttpHandler(request =>
        {
            string body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            if (body.Contains("getMakaleBilgisiV1Request", StringComparison.Ordinal))
                throw new HttpRequestException(sensitiveFailure);
            return SoapSuccess();
        }));

        var response = await new YoksisCollectionService(new(http, YoksisConfig())).CollectAsync(
            new() { TcKimlikNo = tc });

        Assert.Null(response.PublicationDetailTotalCount);
        Assert.Equal(0, response.PublicationDetailRetrievedCount);
        Assert.Contains(response.FailureReasons, x => x.Code == "Unavailable");
        Assert.DoesNotContain(sensitiveFailure, JsonSerializer.Serialize(response));
    }

    [Fact]
    public async Task CollectAsync_EmptyPublicationDetail_IsIncompleteAndGrouped()
    {
        string tc = new('1', 11);
        using var http = new HttpClient(new StubHttpHandler(request =>
        {
            string body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            if (body.Contains("getMakaleBilgisiV1Request", StringComparison.Ordinal) &&
                !body.Contains("Detay", StringComparison.Ordinal))
                return SoapSuccess("<Kayit><YAYIN_ID>one</YAYIN_ID></Kayit>");
            return SoapSuccess();
        }));

        var response = await new YoksisCollectionService(new(http, YoksisConfig())).CollectAsync(
            new() { TcKimlikNo = tc });

        Assert.Equal(1, response.PublicationDetailTotalCount);
        Assert.Equal(0, response.PublicationDetailRetrievedCount);
        Assert.Equal(1, response.PublicationDetailFailedCount);
        Assert.Contains(response.PublicationFailureReasons, x =>
            x.Code == "EmptyDetail" && x.AffectedCount == 1);
    }

    [Fact]
    public async Task CollectAsync_DuplicateAndMissingIdentifiers_GroupStableReasonsAndExcludeProjects()
    {
        string tc = new('1', 11);
        using var http = new HttpClient(new StubHttpHandler(request =>
        {
            string body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            if (body.Contains("getMakaleBilgisiV1Request", StringComparison.Ordinal) &&
                !body.Contains("Detay", StringComparison.Ordinal))
                return SoapSuccess("<K><YAYIN_ID>one</YAYIN_ID></K><K><YAYIN_ID>one</YAYIN_ID></K>" +
                    "<K><YAYIN_ID>two</YAYIN_ID></K><K><YAYIN_ID>three</YAYIN_ID></K><K><BILGI>x</BILGI></K>");
            if (body.Contains("getMakaleBilgisiDetayV1Request", StringComparison.Ordinal))
                return SoapResult(0, "42", body.Contains("two", StringComparison.Ordinal)
                    ? "İkinci güvenli açıklama" : "Birinci güvenli açıklama");
            if (body.Contains("getirProjeListesiRequest", StringComparison.Ordinal) &&
                !body.Contains("Detay", StringComparison.Ordinal))
                return SoapSuccess("<K><PROJE_ID>project</PROJE_ID></K>");
            if (body.Contains("getirProjeListesiDetayRequest", StringComparison.Ordinal))
                return SoapSuccess("<K><PROJE_ID>project</PROJE_ID></K>");
            return SoapSuccess();
        }));

        var response = await new YoksisCollectionService(new(http, YoksisConfig())).CollectAsync(
            new() { TcKimlikNo = tc });

        Assert.Equal(4, response.PublicationDetailTotalCount);
        Assert.Equal(4, response.PublicationDetailFailedCount);
        Assert.Contains(response.PublicationFailureReasons, x =>
            x.Code == "MissingIdentifier" && x.AffectedCount == 1);
        Assert.Contains(response.PublicationFailureReasons, x =>
            x.Description.Contains("Birinci güvenli açıklama") && x.AffectedCount == 2);
        Assert.Contains(response.PublicationFailureReasons, x =>
            x.Description.Contains("İkinci güvenli açıklama") && x.AffectedCount == 1);
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
    public async Task FillResearcherAsync_OrcidBulkOmitsKnownSummary_ReportsIncompleteTotal()
    {
        const string orcid = "0000-0001-8560-7482";
        var handler = new StubHttpHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/record", StringComparison.Ordinal)
                ? StubHttpHandler.Json(
                    """{"orcid-identifier":{"path":"0000-0001-8560-7482"},"person":{},"activities-summary":{"works":{"group":[{"work-summary":[{"put-code":42,"display-index":"1"}]}]}}}""")
                : StubHttpHandler.Json("""{"bulk":[]}"""));
        using var http = new HttpClient(handler);
        var previous = new OrcidProfile { DisplayName = "Saved profile", WorksCount = 3 };
        var researcher = new Researcher
        {
            PersonelId = "test-" + Guid.NewGuid().ToString("N"),
            Orcid = orcid,
            OrcidProfile = previous
        };

        ProviderCollectionException failure = await Assert.ThrowsAsync<ProviderCollectionException>(
            () => new OrcidClient(http, Config([])).FillResearcherAsync(researcher));

        Assert.Equal("DetailFailure", failure.Code);
        Assert.Equal(0, failure.RetrievedCount);
        Assert.Equal(1, failure.ExpectedCount);
        Assert.Same(previous, researcher.OrcidProfile);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task FillResearcherAsync_ScholarMalformedSecondPage_ReportsPreviousPageCount()
    {
        var previous = new GoogleScholarProfile { DisplayName = "Saved profile", DocumentsCount = 10 };
        var researcher = new Researcher
        {
            PersonelId = "test-" + Guid.NewGuid().ToString("N"),
            GoogleScholarId = "AbCdEfGhIjKl",
            GoogleScholarProfile = previous
        };
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(
            _.RequestUri!.Query.Contains("page=1", StringComparison.Ordinal)
                ? """{"author":{"name":"New profile"},"articles":[{"citation_id":"one","title":"One"}],"pagination":{"next":"page2"}}"""
                : "{ malformed"));
        using var http = new HttpClient(handler);

        ProviderCollectionException failure = await Assert.ThrowsAsync<ProviderCollectionException>(() =>
            new GoogleScholarClient(http, Config(new()
            {
                ["SearchApi:ApiKey"] = Guid.NewGuid().ToString("N")
            })).FillResearcherAsync(researcher, researcher.GoogleScholarId));

        Assert.Equal("PageFailure", failure.Code);
        Assert.Equal("MalformedResponse", failure.CauseCode);
        Assert.Equal(1, failure.RetrievedCount);
        Assert.Null(failure.ExpectedCount);
        Assert.Same(previous, researcher.GoogleScholarProfile);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task FillResearcherAsync_ScholarPageFails_PreservesPreviousProfile()
    {
        var previous = new GoogleScholarProfile { DisplayName = "Saved profile", DocumentsCount = 10 };
        var researcher = new Researcher
        {
            PersonelId = "test-" + Guid.NewGuid().ToString("N"), GoogleScholarId = "AbCdEfGhIjKl", GoogleScholarProfile = previous };
        int requests = 0;
        using var http = new HttpClient(new StubHttpHandler(_ => ++requests == 1
            ? StubHttpHandler.Json("""{"author":{"name":"New profile"},"articles":[],"pagination":{"next":"page2"}}""")
            : throw new HttpRequestException("Synthetic network failure")));
        var client = new GoogleScholarClient(http, Config(new() { ["SearchApi:ApiKey"] = Guid.NewGuid().ToString("N") }));
        ProviderCollectionException failure = await Assert.ThrowsAsync<ProviderCollectionException>(
            () => client.FillResearcherAsync(researcher, researcher.GoogleScholarId));
        Assert.Equal("PageFailure", failure.Code);
        Assert.Equal(0, failure.RetrievedCount);
        Assert.Null(failure.ExpectedCount);
        Assert.Same(previous, researcher.GoogleScholarProfile);
        Assert.Equal(2, requests);
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
}
