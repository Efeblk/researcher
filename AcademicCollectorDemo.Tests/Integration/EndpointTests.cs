using AcademicCollectorDemo.Tests.Infrastructure;
using System.Net.Http.Json;
using System.Text.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;
using Microsoft.Extensions.DependencyInjection;

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
        using var coreScript = await host.Client.GetAsync("/Serenity.Corelib/index.global.js");
        coreScript.EnsureSuccessStatusCode();
        using var profile = await host.Client.PostAsJsonAsync("/Services/AcademicPerformance/V1/GetResearcher", new { PersonelID = researcher.PersonelId });
        profile.EnsureSuccessStatusCode();
        var body = await profile.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(researcher.PersonelId, body.GetProperty("Researcher").GetProperty("PersonelID").GetString());

        using var summaryList = await host.Client.PostAsJsonAsync(
            "/Services/AcademicPerformance/PublicationSummary/List",
            new { EqualityFilter = new Dictionary<string, object> { ["PersonelID"] = "00123-A" } });
        summaryList.EnsureSuccessStatusCode();
        JsonElement summaryBody = await summaryList.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement summary = summaryBody.GetProperty("Entities")[0];
        Assert.Equal("Endpoint publication", summary.GetProperty("Title").GetString());

        using var approval = await host.Client.PostAsJsonAsync(
            "/Services/AcademicPerformance/PublicationDisplayApproval/Save",
            new { PersonelID = "00123-A", PublicationSummaryIds = new[] { summary.GetProperty("Id").GetInt32() } });
        approval.EnsureSuccessStatusCode();
        Assert.Equal("00123-A", (await approval.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("PersonelID").GetString());

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
}
