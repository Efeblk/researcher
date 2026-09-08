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
        var researcher = new Researcher { FirstName = "Synthetic", LastName = "Researcher" };
        database.Researchers.Add(researcher);
        await database.SaveChangesAsync();
        database.PublicationSummaries.Add(new PublicationSummary
        {
            ResearcherId = researcher.Id, Title = "Coastal water monitoring", Fingerprint = Guid.NewGuid().ToString("N"),
            PublicationYear = 2025, Sources = "Orcid", UpdatedAt = DateTime.UtcNow
        });
        await database.SaveChangesAsync();

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
        var input = new { ResearcherId = researcher.Id };
        long latestId;
        using (var host = new HostProcess(fixture.ConnectionString, analysis.Urls.Single()))
        {
            await host.WaitUntilReadyAsync();
            using var missing = await host.Client.PostAsJsonAsync(Api + "GetResearcherAnalysis", input);
            Assert.True(missing.StatusCode == HttpStatusCode.NotFound, await missing.Content.ReadAsStringAsync());
            for (int index = 0; index < 2; index++)
            {
                using var response = await host.Client.PostAsJsonAsync(Api + "AnalyzeResearcher", input);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
            var saved = await database.ResearcherAnalyses.AsNoTracking().Where(value => value.ResearcherId == researcher.Id)
                .OrderBy(value => value.Id).ToListAsync();
            Assert.Equal(2, saved.Count);
            latestId = saved[1].Id;
            var snapshot = JsonSerializer.Deserialize<AnalyzeResearcherRequest>(saved[1].SnapshotJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Equal("Coastal water monitoring", Assert.Single(snapshot.Publications).Title);
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
    }

    [Fact]
    public async Task AnalyzeResearcher_MissingOrEmptyResearcher_DoesNotCallAiOrSave()
    {
        using var scope = fixture.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        var researcher = new Researcher { FirstName = "Synthetic empty" };
        database.Researchers.Add(researcher);
        await database.SaveChangesAsync();
        using var host = new HostProcess(fixture.ConnectionString, "http://127.0.0.1:1/");
        await host.WaitUntilReadyAsync();
        foreach (var (id, status) in new[]
        {
            (0, HttpStatusCode.BadRequest), (int.MaxValue, HttpStatusCode.NotFound),
            (researcher.Id, HttpStatusCode.UnprocessableEntity)
        })
        {
            using var response = await host.Client.PostAsJsonAsync(Api + "AnalyzeResearcher", new { ResearcherId = id });
            Assert.True(status == response.StatusCode, await response.Content.ReadAsStringAsync());
        }
        Assert.False(await database.ResearcherAnalyses.AnyAsync(value => value.ResearcherId == researcher.Id));
    }
}
