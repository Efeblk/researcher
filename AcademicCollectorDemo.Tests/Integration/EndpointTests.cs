using AcademicCollectorDemo.Tests.Infrastructure;
using System.Net.Http.Json;
using System.Text.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using AcademicCollectorDemo.Modules.AcademicPerformance.Application;
using AcademicCollectorDemo.Modules.AcademicPerformance.WebClient.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.WebClient.Endpoints;
using AcademicCollectorDemo.Modules.AcademicPerformance.WebClient.Identity;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis.Collection;
using Serenity.Services;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class EndpointTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Api_StoredResearcherAndInvalidCollection_ReturnsExpectedResponses()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        var researcher = new Researcher
        {
            PersonelId = "00123-A", FirstName = "Synthetic", LastName = "Researcher" };
        db.Researchers.Add(researcher);
        db.AcademicWorks.Add(new AcademicWork
        {
            PersonelId = researcher.PersonelId,
            Provider = AcademicWorkProvider.Orcid,
            ProviderWorkId = "endpoint-work",
            Title = "Endpoint publication",
            SyncedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        await new PublicationSummarySynchronizer(db).SyncAsync(researcher.PersonelId);

        using var host = new HostProcess(fixture.ConnectionString);
        await host.WaitUntilReadyAsync();
        using var page = await host.Client.GetAsync("/AcademicPerformance");
        page.EnsureSuccessStatusCode();
        Assert.DoesNotContain("id=\"PersonelId\"", await page.Content.ReadAsStringAsync());
        using var coreScript = await host.Client.GetAsync("/Serenity.Corelib/index.global.js");
        coreScript.EnsureSuccessStatusCode();
        using var profile = await host.Client.PostAsJsonAsync("/Services/AcademicPerformance/V1/GetResearcher", new { PersonelID = researcher.PersonelId });
        profile.EnsureSuccessStatusCode();
        var body = await profile.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(researcher.PersonelId, body.GetProperty("Researcher").GetProperty("PersonelID").GetString());

        using var summaryList = await host.Client.PostAsJsonAsync(
            "/Services/AcademicPerformance/PublicationSummary/List",
            new { EqualityFilter = new Dictionary<string, object> { ["PersonelID"] = "00123-A" } });
        Assert.Equal(HttpStatusCode.Unauthorized, summaryList.StatusCode);
        JsonElement summaryBody = await summaryList.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("Oturum", summaryBody.GetProperty("Error").GetProperty("Message").GetString());

        PublicationSummaryEndpoint summaryEndpoint = new();
        var scopedList = await summaryEndpoint.List(
            new ListRequest
            {
                EqualityFilter = new Dictionary<string, object>
                {
                    ["PersonelID"] = "spoofed-person"
                }
            },
            db,
            new FixedPersonnelResolver(researcher.PersonelId));
        var scopedBody = Assert.IsType<ListResponse<AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models.PublicationSummary>>(
            scopedList.Value);
        var summary = Assert.Single(scopedBody.Entities);
        Assert.Equal("Endpoint publication", summary.Title);

        PublicationDisplayApprovalEndpoint approvalEndpoint = new();
        IAcademicPerformanceApplicationService applicationService =
            scope.ServiceProvider.GetRequiredService<IAcademicPerformanceApplicationService>();
        var approval = await approvalEndpoint.Save(
            new PublicationDisplayApprovalRequest { PublicationSummaryIds = [summary.Id] },
            new FixedPersonnelResolver(researcher.PersonelId),
            applicationService);
        Assert.Equal(researcher.PersonelId, approval.Value!.PersonelId);

        using var invalid = await host.Client.PostAsJsonAsync("/Services/AcademicPerformance/V1/Collect", new
        {
            PersonelID = "invalid-provider-person",
            ORCID = "invalid"
        });
        var error = await invalid.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(error.TryGetProperty("Error", out _));

        const string researcherId = "B-2345-2021";
        db.Researchers.Add(new Researcher
        {
            PersonelId = "endpoint-person",
            WebOfScienceResearcherId = researcherId,
            WebOfScienceProfile = new WebOfScienceProfile
            {
                LastUpdatedAt = DateTime.UtcNow,
                DocumentPagesJson = "{\"WOS\":[{}]}",
                Works = []
            }
        });
        await db.SaveChangesAsync();
        ResearcherCollectionEndpoint webCollectionEndpoint = new();
        var webCollected = await webCollectionEndpoint.Collect(
            new ResearcherCollectionRequest
            {
                WebOfScienceResearcherId =
                    "https://www.webofscience.com/wos/author/rid/B-2345-2021"
            },
            new FixedPersonnelResolver("endpoint-person"),
            applicationService);
        Assert.True(webCollected.Value!.IsSaved);
        Assert.Equal("endpoint-person", webCollected.Value.Researcher!.PersonelId);

        var webYoksis = await webCollectionEndpoint.CollectYoksis(
            new YoksisCollectionRequest { TcKimlikNo = new string('1', 11) },
            new FixedPersonnelResolver("endpoint-person"),
            scope.ServiceProvider.GetRequiredService<YoksisCollectionHandler>());
        Assert.True(webYoksis.Value!.IsSaved);
        Assert.Equal("endpoint-person", webYoksis.Value.PersonelId);

        using var collected = await host.Client.PostAsJsonAsync("/Services/AcademicPerformance/V1/Collect", new
        {
            PersonelID = "endpoint-person",
            ResearcherID = "https://www.webofscience.com/wos/author/rid/B-2345-2021",
            ScholarID = "#NAME?",
            ScopusID = "unsupported-scopus"
        });
        Assert.True(collected.IsSuccessStatusCode, await collected.Content.ReadAsStringAsync());
        JsonElement collectedBody = await collected.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(collectedBody.GetProperty("IsSaved").GetBoolean());
        Assert.Equal(researcherId, collectedBody.GetProperty("Researcher")
            .GetProperty("ResearcherID").GetString());
        Assert.Equal("endpoint-person", collectedBody.GetProperty("Researcher")
            .GetProperty("PersonelID").GetString());
        Assert.Equal(2, collectedBody.GetProperty("Warnings").GetArrayLength());
        Assert.Equal(2, collectedBody.GetProperty("Messages").EnumerateArray()
            .Count(message => message.GetString()!.StartsWith("[UYARI] ")));

        using var byPersonnel = await host.Client.PostAsJsonAsync(
            "/Services/AcademicPerformance/V1/GetResearcher",
            new { PersonelID = "endpoint-person" });
        byPersonnel.EnsureSuccessStatusCode();
        JsonElement personnelBody = await byPersonnel.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(collectedBody.GetProperty("Researcher").GetProperty("PersonelID").GetString(),
            personnelBody.GetProperty("Researcher").GetProperty("PersonelID").GetString());

        using var publications = await host.Client.PostAsJsonAsync("/Services/AcademicPerformance/V1/ListPublications", new { PersonelID = researcher.PersonelId });
        publications.EnsureSuccessStatusCode();
        Assert.Equal(1, (await publications.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("TotalCount").GetInt32());
    }

    [Fact]
    public async Task WebClientIdentityEndpoints_AnonymousSpoofedPersonnel_ReturnUnauthorizedBeforeWork()
    {
        using var host = new HostProcess(fixture.ConnectionString);
        await host.WaitUntilReadyAsync();
        var requests = new (string Route, object Body)[]
        {
            ("/Services/AcademicPerformance/ResearcherCollection/Collect",
                new { PersonelID = "spoofed", ORCID = "invalid" }),
            ("/Services/AcademicPerformance/ResearcherCollection/CollectYoksis",
                new { PersonelID = "spoofed", TcKimlikNo = "invalid" }),
            ("/Services/AcademicPerformance/PublicationDisplayApproval/Get",
                new { PersonelID = "spoofed", PublicationSummaryIds = Array.Empty<int>() }),
            ("/Services/AcademicPerformance/PublicationDisplayApproval/Save",
                new { PersonelID = "spoofed", PublicationSummaryIds = new[] { int.MaxValue } }),
            ("/Services/AcademicPerformance/PublicationDisplayApproval/ListApproved",
                new { PersonelID = "spoofed", PublicationSummaryIds = Array.Empty<int>() })
        };

        foreach ((string route, object body) in requests)
        {
            using HttpResponseMessage response = await host.Client.PostAsJsonAsync(route, body);
            JsonElement responseBody = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(
                response.StatusCode == HttpStatusCode.Unauthorized,
                $"{route} returned {(int)response.StatusCode}: {responseBody}");
            Assert.Contains(
                "Oturum",
                responseBody.GetProperty("Error").GetProperty("Message").GetString());
        }
    }

    private sealed class FixedPersonnelResolver(string personelId) : ICurrentPersonnelResolver
    {
        public string GetPersonelId() => personelId;
    }
}
