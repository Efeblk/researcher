using System.Net;
using System.Text;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.GoogleScholar;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Tests.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class GoogleScholarClientTests
{
    [Fact]
    public async Task FillResearcherAsync_EnglishMetrics_ParsesScreenshotValuesWithoutAuthentication()
    {
        HttpRequestMessage? sent = null;
        using HttpClient http = new(new StubHttpHandler(request =>
        {
            sent = request;
            return Html(ProfileTable(
                "Citations", "648", "521",
                "h-index", "9", "9",
                "i10-index", "9", "9",
                "Since 2021"));
        }));
        Researcher researcher = new() { PersonelId = "P-1" };

        await Client(http).FillResearcherAsync(researcher, "AbCdEfGhIjKl");

        GoogleScholarProfile profile = Assert.IsType<GoogleScholarProfile>(researcher.GoogleScholarProfile);
        Assert.Equal(648, profile.CitationCount);
        Assert.Equal(521, profile.CitationCountRecent);
        Assert.Equal(9, profile.HIndex);
        Assert.Equal(9, profile.HIndexRecent);
        Assert.Equal(9, profile.I10Index);
        Assert.Equal(9, profile.I10IndexRecent);
        Assert.Equal(2021, profile.MetricsSinceYear);
        Assert.Equal("Synthetic Scholar", profile.DisplayName);
        Assert.Contains("user=AbCdEfGhIjKl", sent!.RequestUri!.Query);
        Assert.Contains("hl=en", sent.RequestUri.Query);
        Assert.Null(sent.Headers.Authorization);
        Assert.False(GoogleScholarProfile.HasKnownDocumentsCount(profile.RawDataJson));
        Assert.Null(researcher.GoogleScholarId);
    }

    [Fact]
    public async Task FillResearcherAsync_TurkishLabelsFormattedCountsAndRealZero_ParsesAllValues()
    {
        using HttpClient http = new(new StubHttpHandler(_ => Html(ProfileTable(
            "Alıntılar", "12.345", "6 789",
            "h-endeksi", "17", "0",
            "i10-endeksi", "1,234", "0",
            "2021'den beri"))));
        Researcher researcher = new() { PersonelId = "P-1" };

        await Client(http).FillResearcherAsync(researcher, "AbCdEfGhIjKl");

        GoogleScholarProfile profile = researcher.GoogleScholarProfile!;
        Assert.Equal(12345, profile.CitationCount);
        Assert.Equal(6789, profile.CitationCountRecent);
        Assert.Equal(17, profile.HIndex);
        Assert.Equal(0, profile.HIndexRecent);
        Assert.Equal(1234, profile.I10Index);
        Assert.Equal(0, profile.I10IndexRecent);
        Assert.Equal(2021, profile.MetricsSinceYear);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1e3")]
    [InlineData("N/A 2")]
    [InlineData("12,34")]
    [InlineData("1,234.567")]
    public async Task FillResearcherAsync_MalformedCount_RejectsWholeTable(string malformed)
    {
        GoogleScholarProfile previous = ExistingProfile();
        Researcher researcher = new() { PersonelId = "P-1", GoogleScholarProfile = previous };
        using HttpClient http = new(new StubHttpHandler(_ => Html(ProfileTable(
            "Citations", malformed, "2",
            "h-index", "1", "1",
            "i10-index", "1", "1",
            "Since 2021"))));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => Client(http).FillResearcherAsync(researcher, "AbCdEfGhIjKl"));

        Assert.Same(previous, researcher.GoogleScholarProfile);
        Assert.Equal(42, previous.CitationCount);
        Assert.Equal("old raw data", previous.RawDataJson);
    }

    [Fact]
    public async Task FillResearcherAsync_CaptchaPage_PreservesPreviousProfileAndReportsRateLimit()
    {
        GoogleScholarProfile previous = ExistingProfile();
        Researcher researcher = new() { PersonelId = "P-1", GoogleScholarProfile = previous };
        using HttpClient http = new(new StubHttpHandler(_ => Html(
            """<html><body><form id="captcha-form"><div class="g-recaptcha"></div></form></body></html>""")));

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => Client(http).FillResearcherAsync(researcher, "AbCdEfGhIjKl"));

        Assert.Equal(HttpStatusCode.TooManyRequests, exception.StatusCode);
        Assert.Same(previous, researcher.GoogleScholarProfile);
        Assert.Equal("old raw data", previous.RawDataJson);
    }

    [Fact]
    public async Task FillResearcherAsync_MissingMetricsTable_PreservesPreviousProfile()
    {
        GoogleScholarProfile previous = ExistingProfile();
        Researcher researcher = new() { PersonelId = "P-1", GoogleScholarProfile = previous };
        using HttpClient http = new(new StubHttpHandler(_ => Html(
            """<html><body><div id="gsc_prf_in">Synthetic Scholar</div></body></html>""")));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => Client(http).FillResearcherAsync(researcher, "AbCdEfGhIjKl"));

        Assert.Same(previous, researcher.GoogleScholarProfile);
        Assert.Equal(42, previous.CitationCount);
    }

    [Fact]
    public async Task FillResearcherAsync_HttpError_PreservesPreviousProfile()
    {
        GoogleScholarProfile previous = ExistingProfile();
        Researcher researcher = new() { PersonelId = "P-1", GoogleScholarProfile = previous };
        using HttpClient http = new(new StubHttpHandler(_ => new(HttpStatusCode.ServiceUnavailable)));

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => Client(http).FillResearcherAsync(researcher, "AbCdEfGhIjKl"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Same(previous, researcher.GoogleScholarProfile);
    }

    [Fact]
    public async Task FillResearcherAsync_ExistingProfile_PreservesWorksDocumentCountAndMetadata()
    {
        GoogleScholarProfile previous = ExistingProfile();
        Researcher researcher = new() { PersonelId = "P-1", GoogleScholarProfile = previous };
        using HttpClient http = new(new StubHttpHandler(_ => Html(ProfileTable(
            "Citations", "648", "521",
            "h-index", "9", "9",
            "i10-index", "9", "9",
            "Since 2021",
            includeProfileMetadata: false))));

        await Client(http).FillResearcherAsync(researcher, "AbCdEfGhIjKl");

        Assert.Same(previous, researcher.GoogleScholarProfile);
        Assert.Equal(10, previous.DocumentsCount);
        Assert.Equal("Saved university", previous.University);
        Assert.Equal("Saved affiliation", previous.Affiliations);
        Assert.Single(previous.Works!);
        Assert.Equal("work-1", previous.Works![0].CitationId);
    }

    private static GoogleScholarClient Client(HttpClient http) => new(http,
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GoogleScholar:ProfileBaseUrl"] = "https://scholar.test/citations"
        }).Build());

    private static HttpResponseMessage Html(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "text/html")
    };

    private static GoogleScholarProfile ExistingProfile() => new()
    {
        DisplayName = "Saved profile",
        Affiliations = "Saved affiliation",
        University = "Saved university",
        CitationCount = 42,
        DocumentsCount = 10,
        RawDataJson = "old raw data",
        Works = [new() { CitationId = "work-1", RawDataJson = "{}" }]
    };

    private static string ProfileTable(
        string citationsLabel, string citationsAll, string citationsRecent,
        string hIndexLabel, string hIndexAll, string hIndexRecent,
        string i10Label, string i10All, string i10Recent,
        string recentHeader,
        bool includeProfileMetadata = true) =>
        $$"""
          <html><body>
            {{(includeProfileMetadata ? """
              <div id="gsc_prf_in">Synthetic Scholar</div>
              <div class="gsc_prf_il">Synthetic University</div>
              <div id="gsc_prf_ivh">Verified email at example.test</div>
              """ : string.Empty)}}
            <table id="gsc_rsb_st">
              <thead><tr><th></th><th>All</th><th>{{recentHeader}}</th></tr></thead>
              <tbody>
                <tr><td>{{citationsLabel}}</td><td>{{citationsAll}}</td><td>{{citationsRecent}}</td></tr>
                <tr><td>{{hIndexLabel}}</td><td>{{hIndexAll}}</td><td>{{hIndexRecent}}</td></tr>
                <tr><td>{{i10Label}}</td><td>{{i10All}}</td><td>{{i10Recent}}</td></tr>
              </tbody>
            </table>
          </body></html>
          """;
}
