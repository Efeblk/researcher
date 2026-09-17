using System.Text.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Orcid;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.RateLimiting;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Collection;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Tests.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class OrcidActivityDetailsTests
{
    private const string Orcid = "0000-0001-8560-7482";

    [Fact]
    public async Task FillResearcherAsync_AllPublicActivitySummaries_FetchesEveryDetail()
    {
        List<string> requestedPaths = [];
        List<long> bufferLimits = [];
        using HttpClient http = new(new StubHttpHandler(request =>
        {
            string path = request.RequestUri!.AbsolutePath;
            requestedPaths.Add(path);
            Assert.True(request.Options.TryGetValue(
                ProviderRateLimitHandler.ResponseBufferLimit, out long bufferLimit));
            bufferLimits.Add(bufferLimit);
            if (path.EndsWith("/record", StringComparison.Ordinal))
                return StubHttpHandler.Json(RecordWithEveryActivity());
            long putCode = long.Parse(path[(path.LastIndexOf('/') + 1)..]);
            return StubHttpHandler.Json($$"""{"put-code":{{putCode}},"visibility":"public"}""");
        }));
        Researcher researcher = new() { Orcid = Orcid };

        await new OrcidClient(http, Configuration()).FillResearcherAsync(researcher);

        Assert.Equal(11, requestedPaths.Count);
        Assert.All(bufferLimits, limit => Assert.Equal(8L * 1024 * 1024, limit));
        using JsonDocument details = JsonDocument.Parse(
            researcher.OrcidProfile!.ActivitiesDetailsJson!);
        Assert.Equal(10, details.RootElement.GetArrayLength());
        string[] categories = details.RootElement.EnumerateArray()
            .Select(item => item.GetProperty("Category").GetString()!).ToArray();
        Assert.Equal(new[] { "employment", "education", "qualification", "invited-position",
            "distinction", "membership", "service", "funding", "peer-review",
            "research-resource" }, categories);
    }

    [Fact]
    public async Task FillResearcherAsync_DetailIdentityMismatch_PreservesPreviousProfile()
    {
        OrcidProfile previous = new()
        {
            DisplayName = "Saved",
            ActivitiesDetailsJson = "[{\"Category\":\"saved\"}]"
        };
        Researcher researcher = new() { Orcid = Orcid, OrcidProfile = previous };
        using HttpClient http = new(new StubHttpHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/record", StringComparison.Ordinal)
                ? StubHttpHandler.Json(RecordWithEmployment())
                : StubHttpHandler.Json("""{"put-code":999}""")));

        ProviderCollectionException exception = await Assert.ThrowsAsync<ProviderCollectionException>(
            () => new OrcidClient(http, Configuration()).FillResearcherAsync(researcher));

        Assert.Equal("DetailFailure", exception.Code);
        Assert.Same(previous, researcher.OrcidProfile);
        Assert.Equal("Saved", previous.DisplayName);
    }

    [Fact]
    public async Task FillResearcherAsync_CancelledActivityRequest_PropagatesCancellation()
    {
        using HttpClient http = new(new CancellationHandler());
        using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new OrcidClient(http, Configuration()).FillResearcherAsync(
                new() { Orcid = Orcid }, cancellation.Token));
    }

    [Fact]
    public async Task FillResearcherAsync_MalformedKnownActivitySection_PreservesPreviousProfile()
    {
        OrcidProfile previous = new()
        {
            DisplayName = "Saved",
            ActivitiesDetailsJson = "[]"
        };
        Researcher researcher = new() { Orcid = Orcid, OrcidProfile = previous };
        string record = """
            {"orcid-identifier":{"path":"ORCID_VALUE"},"person":{},"activities-summary":{
            "works":{"group":[]},"fundings":{"group":{"funding-summary":[]}}}}
            """.Replace("ORCID_VALUE", Orcid, StringComparison.Ordinal);
        using HttpClient http = new(new StubHttpHandler(_ => StubHttpHandler.Json(record)));

        ProviderCollectionException exception = await Assert.ThrowsAsync<ProviderCollectionException>(
            () => new OrcidClient(http, Configuration()).FillResearcherAsync(researcher));

        Assert.Equal("MalformedResponse", exception.CauseCode);
        Assert.Same(previous, researcher.OrcidProfile);
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Orcid:ApiBaseUrl"] = "https://orcid.test/v3.0"
        }).Build();

    private static string RecordWithEmployment() => """
        {"orcid-identifier":{"path":"ORCID_VALUE"},"person":{},"activities-summary":{
        "works":{"group":[]},"employments":{"affiliation-group":[{"summaries":[
        {"employment-summary":{"put-code":1}}]}]}}}
        """.Replace("ORCID_VALUE", Orcid, StringComparison.Ordinal);

    private static string RecordWithEveryActivity() => """
        {"orcid-identifier":{"path":"ORCID_VALUE"},"person":{},"activities-summary":{
          "works":{"group":[]},
          "employments":{"affiliation-group":[{"summaries":[{"employment-summary":{"put-code":1}}]}]},
          "educations":{"affiliation-group":[{"summaries":[{"education-summary":{"put-code":2}}]}]},
          "qualifications":{"affiliation-group":[{"summaries":[{"qualification-summary":{"put-code":3}}]}]},
          "invited-positions":{"affiliation-group":[{"summaries":[{"invited-position-summary":{"put-code":4}}]}]},
          "distinctions":{"affiliation-group":[{"summaries":[{"distinction-summary":{"put-code":5}}]}]},
          "memberships":{"affiliation-group":[{"summaries":[{"membership-summary":{"put-code":6}}]}]},
          "services":{"affiliation-group":[{"summaries":[{"service-summary":{"put-code":7}}]}]},
          "fundings":{"group":[{"funding-summary":[{"put-code":8}]}]},
          "peer-reviews":{"group":[{"peer-review-group":[{"peer-review-summary":[{"put-code":9}]}]}]},
          "research-resources":{"group":[{"research-resource-summary":[{"put-code":10}]}]}
        }}
        """.Replace("ORCID_VALUE", Orcid, StringComparison.Ordinal);

    private sealed class CancellationHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/record", StringComparison.Ordinal))
                return StubHttpHandler.Json(RecordWithEmployment());
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException();
        }
    }
}
