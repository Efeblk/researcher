using AcademicCollectorDemo.Tests.Infrastructure;
using System.Net.Http.Json;
using System.Text.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

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
            Doi = "10.7000/endpoint",
            SyncedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        await new PublicationSummarySynchronizer(db).SyncAsync(researcher.PersonelId);
        await scope.ServiceProvider.GetRequiredService<CanonicalWorkSynchronizer>()
            .SyncAsync(researcher.PersonelId);

        string collectedPersonelId = "endpoint-canonical-" + Guid.NewGuid().ToString("N");
        const string collectedResearcherId = "C-7391-2098";
        string collectedDoi = "10.7100/collect-" + Guid.NewGuid().ToString("N");
        db.Researchers.Add(new Researcher
        {
            PersonelId = collectedPersonelId,
            WebOfScienceResearcherId = collectedResearcherId,
            WebOfScienceProfile = new WebOfScienceProfile
            {
                PersonelId = collectedPersonelId,
                LastUpdatedAt = DateTime.UtcNow,
                DocumentsCount = 1,
                DocumentPagesJson = """{"WOS":[{}],"WOK":[{}]}""",
                Works = [new WebOfScienceWork
                {
                    Uid = "WOS:canonical-collect",
                    Title = "Collected canonical publication",
                    Doi = collectedDoi,
                    PublicationYear = 2026,
                    WorkTypes = "Article",
                    RawDataJson = "{}"
                }]
            }
        });
        await db.SaveChangesAsync();

        using var host = new HostProcess(
            fixture.ConnectionString,
            disablePublicationEnrichmentProviders: true,
            articleSummaryAutomationEnabled: true,
            articleSummaryAutomationWorkerEnabled: false);
        await host.WaitUntilReadyAsync();
        using var page = await host.Client.GetAsync("/AcademicPerformance");
        page.EnsureSuccessStatusCode();
        using var coreScript = await host.Client.GetAsync("/Serenity.Corelib/index.global.js");
        coreScript.EnsureSuccessStatusCode();
        using var profile = await host.Client.PostAsJsonAsync("/Services/AcademicPerformance/V1/GetResearcher", new { PersonelID = researcher.PersonelId });
        profile.EnsureSuccessStatusCode();
        var body = await profile.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(researcher.PersonelId, body.GetProperty("Researcher").GetProperty("PersonelID").GetString());

        using var canonicalCollect = await host.Client.PostAsJsonAsync(
            "/Services/AcademicPerformance/V1/Collect",
            new { PersonelID = collectedPersonelId, ResearcherID = collectedResearcherId });
        canonicalCollect.EnsureSuccessStatusCode();
        JsonElement canonicalCollectBody = await canonicalCollect.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(canonicalCollectBody.GetProperty("IsSaved").GetBoolean(),
            await canonicalCollect.Content.ReadAsStringAsync());
        using var collectedCanonicalList = await host.Client.PostAsJsonAsync(
            "/Services/AcademicPerformance/V1/ListCanonicalPublications",
            new { PersonelID = collectedPersonelId, SearchText = collectedDoi.ToUpperInvariant() });
        collectedCanonicalList.EnsureSuccessStatusCode();
        JsonElement collectedCanonicalBody =
            await collectedCanonicalList.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, collectedCanonicalBody.GetProperty("TotalCount").GetInt32());
        JsonElement collectedCanonical = collectedCanonicalBody.GetProperty("Entities")[0];
        Assert.Equal(collectedDoi, collectedCanonical.GetProperty("NormalizedDoi").GetString());
        Assert.Equal("WebOfScience", collectedCanonical.GetProperty("Observations")[0]
            .GetProperty("Provider").GetString());
        Assert.True(await db.CollectionChanges.AsNoTracking().AnyAsync(change =>
            change.PersonelId == collectedPersonelId &&
            change.ChangeKind == "CanonicalWorkChanged"));

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

        using var canonical = await host.Client.PostAsJsonAsync(
            "/Services/AcademicPerformance/V1/ListCanonicalPublications",
            new { PersonelID = researcher.PersonelId, SearchText = "HTTPS://DOI.ORG/10.7000/ENDPOINT" });
        canonical.EnsureSuccessStatusCode();
        JsonElement canonicalBody = await canonical.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, canonicalBody.GetProperty("TotalCount").GetInt32());
        JsonElement canonicalEntity = canonicalBody.GetProperty("Entities")[0];
        Assert.Equal(researcher.PersonelId, canonicalBody.GetProperty("PersonelID").GetString());
        Assert.Equal("10.7000/endpoint", canonicalEntity.GetProperty("NormalizedDoi").GetString());
        JsonElement canonicalObservation = canonicalEntity.GetProperty("Observations")[0];
        Assert.False(canonicalObservation.TryGetProperty("PersonelID", out _));
        Assert.False(canonicalObservation.TryGetProperty("ProviderPayload", out _));

        using var rebuild = await host.Client.PostAsJsonAsync(
            "/Services/AcademicPerformance/V1/RebuildCanonicalPublications",
            new { PersonelID = researcher.PersonelId });
        rebuild.EnsureSuccessStatusCode();
        JsonElement rebuildBody = await rebuild.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, rebuildBody.GetProperty("CanonicalWorkCount").GetInt32());
        Assert.Equal(1, rebuildBody.GetProperty("ObservationCount").GetInt32());

        using var missingRebuild = await host.Client.PostAsJsonAsync(
            "/Services/AcademicPerformance/V1/RebuildCanonicalPublications",
            new { PersonelID = "missing-canonical-researcher" });
        JsonElement missingBody = await missingRebuild.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(missingBody.TryGetProperty("Error", out _));
    }
}
