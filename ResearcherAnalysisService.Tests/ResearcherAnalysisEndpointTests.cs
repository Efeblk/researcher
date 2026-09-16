using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AcademicCollector.Analysis.Contracts;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Products.Api.Contracts;
using ResearcherAnalysisService.Products.Data;
using ResearcherAnalysisService.SourceData.Researchers;
using ResearcherAnalysisService.SourceData.Works;
using ResearcherAnalysisService.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ResearcherAnalysisService.Tests;

[Collection("Analysis Product SQL Server")]
public sealed class ResearcherAnalysisEndpointTests(AnalysisProductSqlServerFixture fixture)
{
    private const string Api = "/api/v1/";

    [Fact]
    public async Task AnalyzeResearcher_RepeatedThenFailed_PreservesHistoryAndRetrievesWithoutAi()
    {
        using var scope = fixture.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        var researcher = new Researcher
        {
            PersonelId = "test-" + Guid.NewGuid().ToString("N"), FirstName = "Synthetic", LastName = "Researcher" };
        database.Researchers.Add(researcher);
        await database.SaveChangesWithSourceSeedAsync(fixture.ConnectionString);
        database.PublicationSummaries.Add(new PublicationSummary
        {
            PersonelId = researcher.PersonelId, Title = "Coastal water monitoring", Fingerprint = Guid.NewGuid().ToString("N"),
            PublicationYear = 2025, Sources = "Orcid", UpdatedAt = DateTime.UtcNow
        });
        await database.SaveChangesWithSourceSeedAsync(fixture.ConnectionString);

        DateTimeOffset snapshotAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        StubResearcherReportGenerator generator = new(researcher.PersonelId, snapshotAt);
        var input = new { PersonelID = $"  {researcher.PersonelId}  ", SnapshotAt = snapshotAt };
        long latestId;
        await using (AnalysisTestHost host = await AnalysisTestHost.StartAsync(
            generator, usageDatabase: fixture.ConnectionString))
        {
            using var missing = await host.Client.PostAsJsonAsync(Api + "researchers/analysis", input);
            missing.EnsureSuccessStatusCode();
            string emptyBody = await missing.Content.ReadAsStringAsync();
            Assert.Contains("\"Analysis\":null", emptyBody, StringComparison.Ordinal);
            Assert.Contains("\"Coverage\":", emptyBody, StringComparison.Ordinal);
            Assert.Null((await missing.Content.ReadFromJsonAsync<ResearcherAnalysisReadResponse>())!.Analysis);

            using var unknown = await host.Client.PostAsJsonAsync(Api + "researchers/analysis",
                new { PersonelID = "missing-" + Guid.NewGuid().ToString("N") });
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
            for (int index = 0; index < 2; index++)
            {
                using var response = await host.Client.PostAsJsonAsync(Api + "researchers/analysis/generate", input);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
            var saved = await database.ResearcherAnalyses.AsNoTracking().Where(value => value.PersonelId == researcher.PersonelId)
                .OrderBy(value => value.Id).ToListAsync();
            Assert.Equal(2, saved.Count);
            latestId = saved[1].Id;
            var snapshot = JsonSerializer.Deserialize<AnalyzeResearcherRequest>(saved[1].SnapshotJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Equal("Coastal water monitoring", Assert.Single(snapshot.Publications).Title);
            Assert.Equal(snapshotAt, snapshot.SnapshotAt);
            generator.Fail = true;
            using var failed = await host.Client.PostAsJsonAsync(Api + "researchers/analysis/generate", input);
            Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
            Assert.Equal(2, await database.ResearcherAnalyses.CountAsync(value => value.PersonelId == researcher.PersonelId));
        }
        await using AnalysisTestHost restarted = await AnalysisTestHost.StartAsync(
            new StubResearcherReportGenerator(researcher.PersonelId, snapshotAt) { Fail = true },
            usageDatabase: fixture.ConnectionString);
        using var retrieved = await restarted.Client.PostAsJsonAsync(Api + "researchers/analysis", input);
        retrieved.EnsureSuccessStatusCode();
        var result = await retrieved.Content.ReadFromJsonAsync<ResearcherAnalysisReadResponse>();
        Assert.Equal(latestId, result!.Analysis!.Id);
        Assert.Equal("synthetic-model", result.Analysis.Report.Model);
        Assert.Equal(3, generator.Calls);
    }

    [Fact]
    public async Task AnalyzeResearcher_MissingOrEmptyResearcher_DoesNotCallAiOrSave()
    {
        using var scope = fixture.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        var researcher = new Researcher
        {
            PersonelId = "test-" + Guid.NewGuid().ToString("N"), FirstName = "Synthetic empty" };
        database.Researchers.Add(researcher);
        await database.SaveChangesWithSourceSeedAsync(fixture.ConnectionString);
        using var host = new HostProcess(fixture.ConnectionString, "http://127.0.0.1:1/");
        await host.WaitUntilReadyAsync();
        foreach (DateTimeOffset invalidDate in new[] { default(DateTimeOffset), DateTimeOffset.UtcNow.AddDays(1) })
        {
            using var invalid = await host.Client.PostAsJsonAsync(Api + "researchers/analysis/generate",
                new { PersonelID = researcher.PersonelId, SnapshotAt = invalidDate });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        foreach (var (id, status) in new[]
        {
            (string.Empty, HttpStatusCode.BadRequest), ("missing-person", HttpStatusCode.NotFound),
            (researcher.PersonelId, HttpStatusCode.UnprocessableEntity)
        })
        {
            using var response = await host.Client.PostAsJsonAsync(Api + "researchers/analysis/generate", new { PersonelID = id });
            Assert.True(status == response.StatusCode, await response.Content.ReadAsStringAsync());
        }
        Assert.False(await database.ResearcherAnalyses.AnyAsync(value => value.PersonelId == researcher.PersonelId));
    }

    private sealed class StubResearcherReportGenerator(string expectedPersonelId,
        DateTimeOffset expectedSnapshotAt) : IResearcherReportGenerator
    {
        public int Calls { get; private set; }
        public bool Fail { get; set; }

        public Task<GeneratedFindings> GenerateAsync(
            AnalyzeResearcherRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            if (Fail)
                throw new HttpRequestException("Synthetic provider failure.", null,
                    HttpStatusCode.BadGateway);
            Assert.Equal(expectedPersonelId, request.PersonelId);
            Assert.Equal(expectedSnapshotAt, request.SnapshotAt);
            Assert.Single(request.Publications);
            return Task.FromResult(new GeneratedFindings(new AnalysisFindings
            {
                ResearchFocus = [],
                WritingObservations = []
            }, "synthetic-model", "synthetic-v1"));
        }
    }
}
