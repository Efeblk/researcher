using AcademicCollectorDemo.Tests.Infrastructure;
using System.Net.Http.Json;
using System.Text.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
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
        var researcher = new Researcher { FirstName = "Synthetic", LastName = "Researcher" };
        db.Researchers.Add(researcher);
        await db.SaveChangesAsync();

        using var host = new HostProcess(fixture.ConnectionString);
        await host.WaitUntilReadyAsync();
        using var page = await host.Client.GetAsync("/AcademicPerformance");
        page.EnsureSuccessStatusCode();
        using var coreScript = await host.Client.GetAsync("/Serenity.Corelib/index.global.js");
        coreScript.EnsureSuccessStatusCode();
        using var profile = await host.Client.PostAsJsonAsync("/Services/AcademicPerformance/V1/GetResearcher", new { ResearcherId = researcher.Id });
        profile.EnsureSuccessStatusCode();
        var body = await profile.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(researcher.Id, body.GetProperty("Researcher").GetProperty("Id").GetInt32());

        using var invalid = await host.Client.PostAsJsonAsync("/Services/AcademicPerformance/V1/Collect", new { Orcid = "invalid" });
        var error = await invalid.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(error.TryGetProperty("Error", out _));

        const string researcherId = "B-2345-2021";
        db.Researchers.Add(new Researcher
        {
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
            WebOfScienceResearcherId = "https://www.webofscience.com/wos/author/rid/B-2345-2021",
            GoogleScholarId = "#NAME?",
            ScopusId = "unsupported-scopus"
        });
        collected.EnsureSuccessStatusCode();
        JsonElement collectedBody = await collected.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(collectedBody.GetProperty("IsSaved").GetBoolean());
        Assert.Equal(researcherId, collectedBody.GetProperty("Researcher")
            .GetProperty("WebOfScienceResearcherId").GetString());
        Assert.Equal(2, collectedBody.GetProperty("Warnings").GetArrayLength());
        Assert.Equal(2, collectedBody.GetProperty("Messages").EnumerateArray()
            .Count(message => message.GetString()!.StartsWith("[UYARI] ")));

        using var publications = await host.Client.PostAsJsonAsync("/Services/AcademicPerformance/V1/ListPublications", new { ResearcherId = researcher.Id });
        publications.EnsureSuccessStatusCode();
        Assert.Equal(0, (await publications.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("TotalCount").GetInt32());
    }
}
