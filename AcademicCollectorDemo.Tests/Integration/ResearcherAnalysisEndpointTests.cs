using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AcademicCollector.Analysis.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using AcademicCollectorDemo.Tests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class ResearcherAnalysisEndpointTests(SqlServerFixture fixture)
{
    private const string Api = "/Services/AcademicPerformance/V1/";

    [Fact]
    public async Task AnalyzeResearcher_RepeatedThenFailed_PreservesHistoryAndRetrievesWithoutAi()
    {
        using var scope = fixture.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        const string personelId = "000000987654";
        var researcher = new Researcher
        {
            PersonelId = personelId, FirstName = "Synthetic", LastName = "Researcher"
        };
        database.Researchers.Add(researcher);
        await database.SaveChangesAsync();
        database.PublicationSummaries.Add(new PublicationSummary
        {
            ResearcherId = researcher.Id, Title = "Coastal water monitoring", Fingerprint = Guid.NewGuid().ToString("N"),
            PublicationYear = 2025, Sources = "Orcid", UpdatedAt = DateTime.UtcNow
        });
        await database.SaveChangesAsync();

        DateTimeOffset snapshotAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        int calls = 0;
        bool fail = false;
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        await using var analysis = builder.Build();
        analysis.MapPost("/api/v1/analyze", (AnalyzeResearcherRequest snapshot) =>
        {
            calls++;
            if (fail)
                return Results.StatusCode(502);
            Assert.Equal(researcher.Id, snapshot.ResearcherId);
            Assert.Equal(snapshotAt, snapshot.SnapshotAt);
            Assert.Single(snapshot.Publications);
            return Results.Json(new ResearcherAnalysisReport
            {
                ResearcherId = snapshot.ResearcherId, GeneratedAt = DateTimeOffset.UtcNow,
                Model = "synthetic-model", PromptVersion = "synthetic-v1",
                Findings = new() { ResearchFocus = [], WritingObservations = [] },
                Activity = new(snapshot.Publications.Count, [], []), CitationMetrics = snapshot.CitationMetrics,
                Coverage = new(snapshot.SnapshotAt, snapshot.TotalPublicationCount, snapshot.Publications.Count, 0, 0, false, [])
            });
        });
        await analysis.StartAsync();
        var input = new { PersonelID = $"  {personelId}  ", SnapshotAt = snapshotAt };
        long latestId;
        using (var host = new HostProcess(fixture.ConnectionString, analysis.Urls.Single()))
        {
            await host.WaitUntilReadyAsync();
            using var missing = await host.Client.PostAsJsonAsync(Api + "GetResearcherAnalysis", input);
            Assert.True(missing.StatusCode == HttpStatusCode.NotFound, await missing.Content.ReadAsStringAsync());
            foreach (object generationInput in new object[]
            {
                input,
                new { ResearcherId = researcher.Id, SnapshotAt = snapshotAt }
            })
            {
                using var response = await host.Client.PostAsJsonAsync(Api + "AnalyzeResearcher", generationInput);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
            var saved = await database.ResearcherAnalyses.AsNoTracking().Where(value => value.ResearcherId == researcher.Id)
                .OrderBy(value => value.Id).ToListAsync();
            Assert.Equal(2, saved.Count);
            latestId = saved[1].Id;
            var snapshot = JsonSerializer.Deserialize<AnalyzeResearcherRequest>(saved[1].SnapshotJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Equal("Coastal water monitoring", Assert.Single(snapshot.Publications).Title);
            Assert.Equal(snapshotAt, snapshot.SnapshotAt);
            fail = true;
            using var failed = await host.Client.PostAsJsonAsync(Api + "AnalyzeResearcher", input);
            Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
            Assert.Equal(2, await database.ResearcherAnalyses.CountAsync(value => value.ResearcherId == researcher.Id));
        }
        await analysis.StopAsync();
        using var restarted = new HostProcess(fixture.ConnectionString, analysis.Urls.FirstOrDefault() ?? "http://127.0.0.1:1/");
        await restarted.WaitUntilReadyAsync();
        using var retrieved = await restarted.Client.PostAsJsonAsync(Api + "GetResearcherAnalysis", input);
        retrieved.EnsureSuccessStatusCode();
        var result = await retrieved.Content.ReadFromJsonAsync<SavedResearcherAnalysisResponse>();
        Assert.Equal(latestId, result!.Id);
        Assert.Equal("synthetic-model", result.Report.Model);
        Assert.Equal(3, calls);

        using var numericCompatibility = await restarted.Client.PostAsJsonAsync(Api + "GetResearcherAnalysis",
            new { ResearcherId = researcher.Id });
        numericCompatibility.EnsureSuccessStatusCode();
        using var matchingIdentifiers = await restarted.Client.PostAsJsonAsync(Api + "GetResearcherAnalysis",
            new { PersonelID = personelId, ResearcherId = researcher.Id });
        matchingIdentifiers.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task AnalyzeResearcher_MissingOrEmptyResearcher_DoesNotCallAiOrSave()
    {
        using var scope = fixture.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        var researcher = new Researcher { PersonelId = "empty-person", FirstName = "Synthetic empty" };
        var otherResearcher = new Researcher { PersonelId = "other-person", FirstName = "Synthetic other" };
        database.Researchers.AddRange(researcher, otherResearcher);
        await database.SaveChangesAsync();
        using var host = new HostProcess(fixture.ConnectionString, "http://127.0.0.1:1/");
        await host.WaitUntilReadyAsync();
        foreach (DateTimeOffset invalidDate in new[] { default(DateTimeOffset), DateTimeOffset.UtcNow.AddDays(1) })
        {
            using var invalid = await host.Client.PostAsJsonAsync(Api + "AnalyzeResearcher",
                new { PersonelID = researcher.PersonelId, SnapshotAt = invalidDate });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        foreach (var (input, status) in new (object Input, HttpStatusCode Status)[]
        {
            (new { }, HttpStatusCode.BadRequest),
            (new { PersonelID = "   " }, HttpStatusCode.BadRequest),
            (new { PersonelID = new string('x', 201) }, HttpStatusCode.BadRequest),
            (new { ResearcherId = 0 }, HttpStatusCode.BadRequest),
            (new { PersonelID = "missing-person" }, HttpStatusCode.NotFound),
            (new { ResearcherId = int.MaxValue }, HttpStatusCode.NotFound),
            (new { PersonelID = researcher.PersonelId }, HttpStatusCode.UnprocessableEntity)
        })
        {
            using var response = await host.Client.PostAsJsonAsync(Api + "AnalyzeResearcher", input);
            Assert.True(status == response.StatusCode, await response.Content.ReadAsStringAsync());
        }
        foreach (string action in new[] { "AnalyzeResearcher", "GetResearcherAnalysis" })
        {
            using var absent = await host.Client.PostAsJsonAsync(Api + action, new { });
            Assert.Equal(HttpStatusCode.BadRequest, absent.StatusCode);
            using var missing = await host.Client.PostAsJsonAsync(Api + action,
                new { PersonelID = "missing-for-both-actions" });
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            using var missingPersonelId = await host.Client.PostAsJsonAsync(Api + action,
                new { PersonelID = "missing-with-existing-id", ResearcherId = researcher.Id });
            Assert.Equal(HttpStatusCode.NotFound, missingPersonelId.StatusCode);
            using var missingResearcherId = await host.Client.PostAsJsonAsync(Api + action,
                new { PersonelID = researcher.PersonelId, ResearcherId = int.MaxValue });
            Assert.Equal(HttpStatusCode.NotFound, missingResearcherId.StatusCode);
            using var mismatch = await host.Client.PostAsJsonAsync(Api + action,
                new { PersonelID = researcher.PersonelId, ResearcherId = otherResearcher.Id });
            Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
        }
        Assert.False(await database.ResearcherAnalyses.AnyAsync(
            value => value.ResearcherId == researcher.Id || value.ResearcherId == otherResearcher.Id));
    }
}
