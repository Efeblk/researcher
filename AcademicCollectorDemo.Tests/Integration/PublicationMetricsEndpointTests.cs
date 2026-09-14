using System.Net;
using System.Net.Http.Json;
using System.Text;
using AcademicCollectorDemo.Modules.AcademicPerformance;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Metrics;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;
using AcademicCollectorDemo.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class PublicationMetricsEndpointTests(SqlServerFixture fixture)
{
    private const string Api = "/Services/AcademicPerformance/V1/";

    [Fact]
    public async Task GetAndRefreshResearcherPublicationMetrics_PersistedLifecycle_IsScopedAndReadOnly()
    {
        string ownerId = "metrics-owner-" + Guid.NewGuid().ToString("N");
        string otherId = "metrics-other-" + Guid.NewGuid().ToString("N");
        string emptyId = "metrics-empty-" + Guid.NewGuid().ToString("N");
        string suffix = Guid.NewGuid().ToString("N");
        DateTime now = DateTime.UtcNow;
        await using AsyncServiceScope seedScope = fixture.Services.CreateAsyncScope();
        AcademicDbContext database = seedScope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        Researcher owner = new()
        {
            PersonelId = ownerId,
            OpenAlexCitationCount = 0,
            OpenAlexHIndex = -1,
            OpenAlexDocumentsCount = 5,
            OpenAlexTwoYearMeanCitedness = 1.25m,
            OpenAlexMetricsUpdatedAt = now.AddDays(-2),
            ScholarCitationCount = 20,
            ScholarHIndex = 4,
            ScholarI10Index = 0,
            ScholarDocumentsCount = 3,
            ScholarCitationCountRecent = 8,
            ScholarHIndexRecent = 2,
            ScholarI10IndexRecent = 1,
            ScholarMetricsSinceYear = 2021,
            ScholarMetricsUpdatedAt = now.AddDays(-1),
            WosCitationCount = -2,
            WosHIndex = 0,
            WosDocumentsCount = 7,
            WosMetricsUpdatedAt = now.AddDays(-3)
        };
        Researcher other = new()
        {
            PersonelId = otherId,
            OpenAlexCitationCount = 999,
            ScholarCitationCount = 888,
            WosCitationCount = 777
        };
        database.Researchers.AddRange(owner, other, new Researcher { PersonelId = emptyId });

        AcademicWork sharedOne = Work(ownerId, AcademicWorkProvider.OpenAlex,
            $"10.5000/shared-{suffix}", "shared-one", 2020, AcademicWorkCategory.Article);
        sharedOne.Link = "https://metadata.test/landing";
        AcademicWork sharedTwo = Work(ownerId, AcademicWorkProvider.WebOfScience,
            $"HTTPS://DOI.ORG/10.5000/SHARED-{suffix.ToUpperInvariant()}", "shared-two", 2020,
            AcademicWorkCategory.Article);
        AcademicWork conflictOne = Work(ownerId, AcademicWorkProvider.OpenAlex,
            $"10.5000/conflict-{suffix}", "conflict-one", 2019, AcademicWorkCategory.Book);
        conflictOne.FullTextUrl = "https://metadata.test/file.pdf";
        AcademicWork conflictTwo = Work(ownerId, AcademicWorkProvider.WebOfScience,
            $"10.5000/conflict-{suffix}", "conflict-two", 2021, AcademicWorkCategory.BookChapter);
        AcademicWork unknown = Work(ownerId, AcademicWorkProvider.Legacy,
            $"10.5000/unknown-{suffix}", "unknown", null, AcademicWorkCategory.Unknown);
        AcademicWork invalid = Work(ownerId, AcademicWorkProvider.OpenAlex,
            $"10.5000/invalid-{suffix}", "invalid", now.Year + 2, AcademicWorkCategory.Dataset);
        invalid.PublicationDate = new DateTime(2018, 1, 1);
        invalid.Abstract = " \t\r\n ";
        AcademicWork fallback = Work(ownerId, AcademicWorkProvider.Crossref,
            $"10.5000/fallback-{suffix}", "fallback", null, AcademicWorkCategory.Report);
        fallback.PublicationDate = new DateTime(2018, 3, 2);
        fallback.Abstract = "Stored owner abstract";
        fallback.Sources.Add(new AcademicWorkSource
        { Url = "https://metadata.test/source", Kind = "landing", Origin = "synthetic" });
        AcademicWork otherShared = Work(otherId, AcademicWorkProvider.Yoksis,
            $"10.5000/shared-{suffix}", "other-shared", 1990, AcademicWorkCategory.Patent);
        otherShared.Abstract = "Other person's abstract must not affect the owner.";
        otherShared.Link = "https://other.test/source";
        database.AcademicWorks.AddRange(sharedOne, sharedTwo, conflictOne, conflictTwo,
            unknown, invalid, fallback, otherShared);
        await database.SaveChangesAsync();
        CanonicalWorkSynchronizer synchronizer =
            seedScope.ServiceProvider.GetRequiredService<CanonicalWorkSynchronizer>();
        await synchronizer.SyncAsync(ownerId);
        await synchronizer.SyncAsync(otherId);
        database.AcademicWorks.Add(Work(ownerId, AcademicWorkProvider.Legacy,
            null, "unmapped", 2024, AcademicWorkCategory.Other));
        await database.SaveChangesAsync();
        PublicationMetricsRefreshState ownerState = await database.PublicationMetricsRefreshStates
            .SingleAsync(state => state.PersonelId == ownerId);
        ownerState.NextAttemptAt = DateTime.UnixEpoch;
        await database.SaveChangesAsync();

        using var host = new HostProcess(fixture.ConnectionString, "http://127.0.0.1:1/");
        host.Client.Timeout = TimeSpan.FromSeconds(30);
        await host.WaitUntilReadyAsync();
        using (HttpResponseMessage nullRequest = await host.Client.PostAsync(
            Api + "GetResearcherPublicationMetrics",
            new StringContent("null", Encoding.UTF8, "application/json")))
            Assert.Equal(HttpStatusCode.BadRequest, nullRequest.StatusCode);
        using (HttpResponseMessage missing = await host.Client.PostAsJsonAsync(
            Api + "GetResearcherPublicationMetrics", new { }))
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using (HttpResponseMessage tooLong = await host.Client.PostAsJsonAsync(
            Api + "GetResearcherPublicationMetrics", new { PersonelID = new string('x', 201) }))
            Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        using (HttpResponseMessage missingResearcher = await host.Client.PostAsJsonAsync(
            Api + "GetResearcherPublicationMetrics", new { PersonelID = "missing-" + suffix }))
            Assert.Equal(HttpStatusCode.NotFound, missingResearcher.StatusCode);
        Assert.Null((await GetAsync(host, ownerId, HttpStatusCode.Accepted)).Data);
        Assert.Null((await GetAsync(host, emptyId, HttpStatusCode.Accepted)).Data);

        await using ServiceProvider processorServices = CreateMetricsServices();
        await ProcessUntilReadyAsync(processorServices, ownerId);
        ResearcherPublicationMetricsStatusResponse current = await GetAsync(host,
            $"  {ownerId}  ", HttpStatusCode.OK);
        Assert.Equal("Current", current.Status);
        Assert.False(current.IsStale);
        ResearcherPublicationMetricsResponse result = current.Data!;
        Assert.Equal("publication-metrics-v4", result.CatalogVersion);
        Assert.Equal(5, result.CanonicalWorkCount);
        Assert.Equal(7, result.ProviderObservationCount);
        Assert.Equal(1, result.UnmappedAcademicWorkCount);
        Assert.Equal([(2018, 1), (2020, 1)], result.PublicationYears.Histogram
            .Select(bucket => (bucket.Year, bucket.CanonicalWorkCount)));
        Assert.Equal(1, result.PublicationYears.ConflictCanonicalWorkCount);
        Assert.Equal(2, result.PublicationYears.UnknownCanonicalWorkCount);
        Assert.Equal(1, result.PublicationYears.InvalidYearObservationCount);
        Assert.Equal(0, CategoryCount(result, AcademicWorkCategory.Patent));
        Assert.Equal(1, result.Coverage.SavedAbstract.Numerator);
        Assert.Equal(3, result.Coverage.RecordedSourceUrl.Numerator);
        Assert.Equal(0, result.ProviderMetrics.OpenAlex.CitationCount.Value);
        Assert.Equal("Invalid", result.ProviderMetrics.OpenAlex.HIndex.Quality);
        Assert.Equal(20, result.ProviderMetrics.GoogleScholar.CitationCount.Value);
        Assert.Equal(0, result.ProviderMetrics.GoogleScholar.I10Index.Value);
        Assert.Null(result.ProviderMetrics.WebOfScience.CitationCount.Value);
        Assert.Equal("Invalid", result.ProviderMetrics.WebOfScience.CitationCount.Quality);
        Assert.Equal(0, result.ProviderMetrics.WebOfScience.HIndex.Value);
        Assert.DoesNotContain("999", result.ProviderMetrics.OpenAlex.CitationCount.Value?.ToString());
        Assert.Equal(DateTimeKind.Utc,
            result.ProviderMetrics.OpenAlex.SourceUpdatedAt!.Value.Kind);
        Assert.NotEqual(result.ComputedAt, result.ProviderMetrics.OpenAlex.SourceUpdatedAt);
        Assert.Equal(Enum.GetNames<AcademicWorkCategory>(), result.EligibilityPolicy.IncludedCategories);
        Assert.Equal("Excluded", result.EligibilityPolicy.ProviderTotalsInEvaluatedOutputs);
        Assert.Equal("Unavailable", result.ReferencePopulation.Status);
        Assert.False(result.ReferencePopulation.IsReviewed);
        Assert.NotEmpty(result.CrossProviderComparability.ProviderPairs);
        Assert.All(result.CrossProviderComparability.ProviderPairs, pair =>
            Assert.True(pair.CanonicalWorkOverlapDenominator >= 0));
        long snapshotId = current.SnapshotId!.Value;
        List<PublicationMetricProviderSnapshot> providerRows = await database
            .PublicationMetricProviderSnapshots.AsNoTracking()
            .Where(row => row.SnapshotId == snapshotId).OrderBy(row => row.Provider).ToListAsync();
        Assert.Equal(3, providerRows.Count);
        PublicationMetricProviderSnapshot openAlexRow = providerRows
            .Single(row => row.Provider == "OpenAlex");
        Assert.Equal(0, openAlexRow.CitationCount);
        Assert.Null(openAlexRow.HIndex);
        Assert.True(openAlexRow.HasInvalidValues);
        Assert.Contains("OpenAlex author profile", openAlexRow.FieldMetadataJson);
        PublicationMetricSnapshot persisted = await database.PublicationMetricSnapshots.AsNoTracking()
            .SingleAsync(snapshot => snapshot.Id == snapshotId);
        Assert.Contains("providerMetrics", persisted.ResultJson);
        string originalJson = persisted.ResultJson;
        int snapshotCount = await database.PublicationMetricSnapshots.CountAsync(
            snapshot => snapshot.PersonelId == ownerId);
        Assert.Equal(snapshotId, (await GetAsync(host, ownerId, HttpStatusCode.OK)).SnapshotId);
        Assert.Equal(snapshotCount, await database.PublicationMetricSnapshots.CountAsync(
            snapshot => snapshot.PersonelId == ownerId));

        Researcher storedOwner = await database.Researchers
            .SingleAsync(researcher => researcher.PersonelId == ownerId);
        storedOwner.OpenAlexCitationCount = 17;
        storedOwner.OpenAlexMetricsUpdatedAt = now;
        database.AcademicWorks.Add(Work(ownerId, AcademicWorkProvider.Legacy,
            null, "raw-unscheduled", 2025, AcademicWorkCategory.Other));
        await database.SaveChangesAsync();
        ResearcherPublicationMetricsStatusResponse unchanged = await GetAsync(host, ownerId,
            HttpStatusCode.OK);
        Assert.Equal(snapshotId, unchanged.SnapshotId);
        Assert.Equal(1, unchanged.Data!.UnmappedAcademicWorkCount);
        Assert.Equal(0, unchanged.Data.ProviderMetrics.OpenAlex.CitationCount.Value);

        using HttpResponseMessage refreshResponse = await host.Client.PostAsJsonAsync(
            Api + "RefreshResearcherPublicationMetrics", new { PersonelID = ownerId });
        Assert.Equal(HttpStatusCode.Accepted, refreshResponse.StatusCode);
        ResearcherPublicationMetricsStatusResponse stale =
            (await refreshResponse.Content.ReadFromJsonAsync<ResearcherPublicationMetricsStatusResponse>())!;
        Assert.Equal("Stale", stale.Status);
        Assert.True(stale.IsStale);
        Assert.Equal(snapshotId, stale.SnapshotId);
        database.ChangeTracker.Clear();
        PublicationMetricsRefreshState scheduled = await database.PublicationMetricsRefreshStates
            .SingleAsync(state => state.PersonelId == ownerId);
        scheduled.NextAttemptAt = DateTime.UnixEpoch;
        await database.SaveChangesAsync();
        await ProcessUntilReadyAsync(processorServices, ownerId);
        ResearcherPublicationMetricsStatusResponse refreshed = await GetAsync(host, ownerId,
            HttpStatusCode.OK);
        Assert.NotEqual(snapshotId, refreshed.SnapshotId);
        Assert.Equal(2, refreshed.Data!.UnmappedAcademicWorkCount);
        Assert.Equal(17, refreshed.Data.ProviderMetrics.OpenAlex.CitationCount.Value);
        Assert.Equal(17, await database.PublicationMetricProviderSnapshots.AsNoTracking()
            .Where(row => row.SnapshotId == refreshed.SnapshotId && row.Provider == "OpenAlex")
            .Select(row => row.CitationCount).SingleAsync());
        Assert.Equal(3, await database.PublicationMetricProviderSnapshots.CountAsync(row =>
            row.SnapshotId == snapshotId));
        Assert.Equal(0, await database.PublicationMetricProviderSnapshots.AsNoTracking()
            .Where(row => row.SnapshotId == snapshotId && row.Provider == "OpenAlex")
            .Select(row => row.CitationCount).SingleAsync());
        Assert.Equal(originalJson, await database.PublicationMetricSnapshots.AsNoTracking()
            .Where(snapshot => snapshot.Id == snapshotId)
            .Select(snapshot => snapshot.ResultJson).SingleAsync());

        await ProcessUntilReadyAsync(processorServices, emptyId);
        ResearcherPublicationMetricsStatusResponse empty = await GetAsync(host, emptyId,
            HttpStatusCode.OK);
        Assert.Equal(0, empty.Data!.CanonicalWorkCount);
        Assert.Null(empty.Data.Coverage.SavedAbstract.Proportion);
    }

    [Fact]
    public async Task MetricsBackgroundWorker_DiscoversEmptyResearcher_AndPersistsFirstSnapshot()
    {
        using var host = new HostProcess(fixture.ConnectionString, "http://127.0.0.1:1/",
            publicationMetricsWorkerEnabled: true, publicationMetricsPollSeconds: 1,
            publicationMetricsBatchSize: 100);
        host.Client.Timeout = TimeSpan.FromSeconds(30);
        await host.WaitUntilReadyAsync();
        string personelId = "metrics-hosted-" + Guid.NewGuid().ToString("N");
        using (IServiceScope scope = fixture.Services.CreateScope())
        {
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            database.Researchers.Add(new Researcher { PersonelId = personelId });
            await database.SaveChangesAsync();
        }
        DateTime deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            using HttpResponseMessage response = await host.Client.PostAsJsonAsync(
                Api + "GetResearcherPublicationMetrics", new { PersonelID = personelId });
            if (response.StatusCode == HttpStatusCode.OK)
            {
                ResearcherPublicationMetricsStatusResponse ready =
                    (await response.Content.ReadFromJsonAsync<ResearcherPublicationMetricsStatusResponse>())!;
                Assert.Equal(0, ready.Data!.CanonicalWorkCount);
                return;
            }
            await Task.Delay(200);
        }
        throw new TimeoutException("Metrics worker did not persist the initial empty snapshot.");
    }

    private ServiceProvider CreateMetricsServices()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:AcademicDatabase"] = fixture.ConnectionString,
                ["BulkCollection:WorkerEnabled"] = "false",
                ["ArticleSummaryAutomation:Enabled"] = "false",
                ["ArticleSummaryAutomation:WorkerEnabled"] = "false",
                ["PublicationMetrics:WorkerEnabled"] = "true",
                ["PublicationMetrics:BatchSize"] = "100"
            }).Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddAcademicPerformanceModule(configuration);
        return services.BuildServiceProvider();
    }

    private static async Task ProcessUntilReadyAsync(ServiceProvider services, string personelId)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            using IServiceScope scope = services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<PublicationMetricsProcessor>()
                .ProcessBatchAsync();
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            if (await database.PublicationMetricsRefreshStates.AsNoTracking().AnyAsync(state =>
                state.PersonelId == personelId && state.ComputedRevision >= state.RequestedRevision))
                return;
        }
        throw new TimeoutException("Target metrics revision was not computed.");
    }

    private static async Task<ResearcherPublicationMetricsStatusResponse> GetAsync(
        HostProcess host, string personelId, HttpStatusCode expected)
    {
        using HttpResponseMessage response = await host.Client.PostAsJsonAsync(
            Api + "GetResearcherPublicationMetrics", new { PersonelID = personelId });
        Assert.Equal(expected, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ResearcherPublicationMetricsStatusResponse>())!;
    }

    private static int CategoryCount(ResearcherPublicationMetricsResponse result,
        AcademicWorkCategory category) => result.Categories.Histogram
            .Single(bucket => bucket.Category == category.ToString()).CanonicalWorkCount;

    private static AcademicWork Work(string personelId, AcademicWorkProvider provider, string? doi,
        string providerWorkId, int? year, AcademicWorkCategory category) => new()
        {
            PersonelId = personelId, Provider = provider, ProviderWorkId = providerWorkId,
            Doi = doi, PublicationYear = year, Category = category, SyncedAt = DateTime.UtcNow
        };
}
