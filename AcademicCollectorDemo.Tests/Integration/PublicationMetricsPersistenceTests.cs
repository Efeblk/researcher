using AcademicCollectorDemo.Modules.AcademicPerformance;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.SemanticScholar;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using AcademicCollectorDemo.Modules.AcademicPerformance.Metrics;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;
using AcademicCollectorDemo.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class PublicationMetricsPersistenceTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task OpenAlexClient_MissingAuthorTotalsRemainUnknown_WhileExplicitZeroIsPreserved()
    {
        string suffix = Guid.NewGuid().ToString("N");
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["OpenAlex:ApiBaseUrl"] = "https://openalex.test"
            }).Build();
        Researcher missing = new() { Orcid = "0000-0000-0000-0001" };
        using (HttpClient http = new(new StubHttpHandler(request =>
            StubHttpHandler.Json(request.RequestUri!.AbsolutePath.EndsWith("/authors")
                ? $"{{\"results\":[{{\"id\":\"https://openalex.org/A{suffix}\",\"summary_stats\":{{}}}}]}}"
                : "{\"results\":[],\"meta\":{\"next_cursor\":null}}"))))
        {
            await new OpenAlexClient(http, configuration).FillResearcherAsync(missing);
        }
        Assert.Null(missing.OpenAlexProfile!.WorksCount);
        Assert.Null(missing.OpenAlexProfile.CitedByCount);

        Researcher zero = new() { Orcid = "0000-0000-0000-0002" };
        using (HttpClient http = new(new StubHttpHandler(request =>
            StubHttpHandler.Json(request.RequestUri!.AbsolutePath.EndsWith("/authors")
                ? $"{{\"results\":[{{\"id\":\"https://openalex.org/A0{suffix}\",\"works_count\":0,\"cited_by_count\":0,\"summary_stats\":{{\"h_index\":0,\"i10_index\":0}}}}]}}"
                : "{\"results\":[],\"meta\":{\"next_cursor\":null}}"))))
        {
            await new OpenAlexClient(http, configuration).FillResearcherAsync(zero);
        }
        Assert.Equal(0, zero.OpenAlexProfile!.WorksCount);
        Assert.Equal(0, zero.OpenAlexProfile.CitedByCount);

        string missingId = "metrics-oa-missing-" + suffix;
        string zeroId = "metrics-oa-zero-" + suffix;
        missing.PersonelId = missingId;
        missing.OpenAlexProfile.PersonelId = missingId;
        zero.PersonelId = zeroId;
        zero.OpenAlexProfile.PersonelId = zeroId;
        using (IServiceScope scope = fixture.Services.CreateScope())
        {
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            database.Researchers.AddRange(missing, zero);
            await database.SaveChangesAsync();
            CanonicalWorkSynchronizer canonical =
                scope.ServiceProvider.GetRequiredService<CanonicalWorkSynchronizer>();
            await canonical.SyncAsync(missingId);
            await canonical.SyncAsync(zeroId);
            await database.PublicationMetricsRefreshStates
                .Where(state => state.PersonelId == missingId || state.PersonelId == zeroId)
                .ExecuteUpdateAsync(update => update.SetProperty(
                    state => state.NextAttemptAt, DateTime.UnixEpoch));
        }

        await using ServiceProvider metrics = CreateServices(
            new FixedTimeProvider(new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero)),
            "publication-metrics-v2");
        await ProcessUntilCurrentAsync(metrics, missingId);
        await ProcessUntilCurrentAsync(metrics, zeroId);
        using (IServiceScope scope = metrics.CreateScope())
        {
            PublicationMetricsReadService reader =
                scope.ServiceProvider.GetRequiredService<PublicationMetricsReadService>();
            ResearcherPublicationMetricsStatusResponse missingSnapshot =
                (await reader.GetAsync(missingId, CancellationToken.None))!;
            ResearcherPublicationMetricsStatusResponse zeroSnapshot =
                (await reader.GetAsync(zeroId, CancellationToken.None))!;
            Assert.Null(missingSnapshot.Data!.ProviderMetrics.OpenAlex.CitationCount.Value);
            Assert.Equal("Unknown",
                missingSnapshot.Data.ProviderMetrics.OpenAlex.CitationCount.Quality);
            Assert.Equal(0, zeroSnapshot.Data!.ProviderMetrics.OpenAlex.CitationCount.Value);
            Assert.Equal("Available", zeroSnapshot.Data.ProviderMetrics.OpenAlex.CitationCount.Quality);

            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            Assert.Null(await database.PublicationMetricProviderSnapshots.AsNoTracking()
                .Where(row => row.SnapshotId == missingSnapshot.SnapshotId &&
                    row.Provider == "OpenAlex")
                .Select(row => row.CitationCount).SingleAsync());
            Assert.Equal(0, await database.PublicationMetricProviderSnapshots.AsNoTracking()
                .Where(row => row.SnapshotId == zeroSnapshot.SnapshotId &&
                    row.Provider == "OpenAlex")
                .Select(row => row.CitationCount).SingleAsync());
        }
    }

    [Fact]
    public async Task SourceHooks_CanonicalRemovalAndSemanticSource_AdvanceOwnRevision()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        string firstId = "metrics-remove-" + Guid.NewGuid().ToString("N");
        string secondId = "metrics-source-" + Guid.NewGuid().ToString("N");
        string doi = "10.7777/" + Guid.NewGuid().ToString("N");
        Researcher first = new() { PersonelId = firstId };
        Researcher second = new() { PersonelId = secondId };
        AcademicWork removed = Work(firstId, null, "removed");
        AcademicWork sourced = Work(secondId, doi, "sourced");
        database.AddRange(first, second, removed, sourced);
        await database.SaveChangesAsync();
        CanonicalWorkSynchronizer canonical =
            scope.ServiceProvider.GetRequiredService<CanonicalWorkSynchronizer>();
        await canonical.SyncAsync(firstId);
        await canonical.SyncAsync(secondId);
        Assert.Equal(1, await RevisionAsync(database, firstId));
        Assert.Equal(1, await RevisionAsync(database, secondId));

        database.AcademicWorks.Remove(removed);
        await database.SaveChangesAsync();
        await canonical.SyncAsync(firstId);
        Assert.Equal(2, await RevisionAsync(database, firstId));
        Assert.False(await database.CanonicalResearcherWorks.AnyAsync(value =>
            value.PersonelId == firstId));

        await using (var collectionTransaction = await database.Database.BeginTransactionAsync())
        {
            database.OpenAlexProfiles.Add(new OpenAlexProfile
            {
                PersonelId = secondId,
                OpenAlexAuthorId = "A" + Guid.NewGuid().ToString("N"),
                WorksCount = null,
                CitedByCount = 0,
                HIndex = 0,
                LastUpdatedAt = DateTime.UtcNow
            });
            await database.SaveChangesAsync();
            await canonical.SyncAsync(secondId);
            await collectionTransaction.CommitAsync();
        }
        database.ChangeTracker.Clear();
        Researcher providerUpdated = await database.Researchers.AsNoTracking()
            .SingleAsync(value => value.PersonelId == secondId);
        Assert.Equal(0, providerUpdated.OpenAlexCitationCount);
        Assert.Null(providerUpdated.OpenAlexDocumentsCount);
        Assert.Equal(2, await RevisionAsync(database, secondId));

        database.SemanticScholarPapers.Add(new()
        {
            NormalizedDoi = doi,
            Found = true,
            FetchedAt = DateTime.UtcNow,
            OpenAccessPdfJson = "{\"url\":\"https://source.test/paper.pdf\",\"status\":\"OPEN\"}"
        });
        await database.SaveChangesAsync();
        int added = await scope.ServiceProvider
            .GetRequiredService<SemanticScholarWorkSourceSynchronizer>()
            .SyncAsync(secondId);
        Assert.Equal(1, added);
        Assert.Equal(3, await RevisionAsync(database, secondId));
        Assert.True(await database.AcademicWorkSources.AnyAsync(source =>
            source.AcademicWorkId == sourced.Id));
    }

    [Fact]
    public async Task Processor_FailureRetainsLastGood_ThenRestartAndCatalogYearChangeRefresh()
    {
        string personelId = "metrics-retry-" + Guid.NewGuid().ToString("N");
        FixedTimeProvider initialClock = new(new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero));
        await using ServiceProvider initial = CreateServices(initialClock, "publication-metrics-v2");
        using (IServiceScope scope = initial.CreateScope())
        {
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            database.Researchers.Add(new Researcher { PersonelId = personelId });
            database.AcademicWorks.Add(Work(personelId,
                "10.8888/" + Guid.NewGuid().ToString("N"), "retry"));
            await database.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<CanonicalWorkSynchronizer>()
                .SyncAsync(personelId);
            PublicationMetricsRefreshState state = await database.PublicationMetricsRefreshStates
                .SingleAsync(value => value.PersonelId == personelId);
            state.NextAttemptAt = DateTime.UnixEpoch;
            await database.SaveChangesAsync();
        }
        await ProcessUntilCurrentAsync(initial, personelId);
        long firstSnapshotId;
        using (IServiceScope scope = initial.CreateScope())
        {
            PublicationMetricsReadService reader =
                scope.ServiceProvider.GetRequiredService<PublicationMetricsReadService>();
            ResearcherPublicationMetricsStatusResponse current =
                (await reader.GetAsync(personelId, CancellationToken.None))!;
            firstSnapshotId = current.SnapshotId!.Value;
            await scope.ServiceProvider.GetRequiredService<PublicationMetricsRefreshService>()
                .ScheduleAsync(personelId, CancellationToken.None);
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            PublicationMetricsRefreshState state = await database.PublicationMetricsRefreshStates
                .SingleAsync(value => value.PersonelId == personelId);
            state.NextAttemptAt = DateTime.UnixEpoch;
            await database.SaveChangesAsync();
        }

        await using ServiceProvider failing = CreateServices(initialClock,
            "publication-metrics-v2", personelId);
        using (IServiceScope scope = failing.CreateScope())
            await scope.ServiceProvider.GetRequiredService<PublicationMetricsProcessor>()
                .ProcessBatchAsync();
        using (IServiceScope scope = failing.CreateScope())
        {
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            PublicationMetricsRefreshState state = await database.PublicationMetricsRefreshStates
                .AsNoTracking().SingleAsync(value => value.PersonelId == personelId);
            Assert.Equal(firstSnapshotId, state.LastSuccessfulSnapshotId);
            Assert.True(state.RequestedRevision > state.ComputedRevision);
            Assert.Equal(1, state.Attempts);
            Assert.Equal("MetricComputationFailed", state.LastOutcomeCode);
            Assert.DoesNotContain("secret", state.LastOutcomeMessage!, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, await database.PublicationMetricSnapshots.CountAsync(snapshot =>
                snapshot.PersonelId == personelId));
            state = await database.PublicationMetricsRefreshStates
                .SingleAsync(value => value.PersonelId == personelId);
            state.NextAttemptAt = DateTime.UnixEpoch;
            await database.SaveChangesAsync();
        }

        await using ServiceProvider restarted = CreateServices(initialClock, "publication-metrics-v2");
        await ProcessUntilCurrentAsync(restarted, personelId);
        FixedTimeProvider nextYear = new(new DateTimeOffset(2027, 1, 2, 12, 0, 0, TimeSpan.Zero));
        await using ServiceProvider upgraded = CreateServices(nextYear, "publication-metrics-v3");
        using (IServiceScope scope = upgraded.CreateScope())
        {
            ResearcherPublicationMetricsStatusResponse stale = (await scope.ServiceProvider
                .GetRequiredService<PublicationMetricsReadService>()
                .GetAsync(personelId, CancellationToken.None))!;
            Assert.Equal("Stale", stale.Status);
            Assert.Equal("publication-metrics-v2", stale.SnapshotCatalogVersion);
        }
        await ProcessUntilCurrentAsync(upgraded, personelId);
        using (IServiceScope scope = upgraded.CreateScope())
        {
            ResearcherPublicationMetricsStatusResponse current = (await scope.ServiceProvider
                .GetRequiredService<PublicationMetricsReadService>()
                .GetAsync(personelId, CancellationToken.None))!;
            Assert.Equal("Current", current.Status);
            Assert.Equal("publication-metrics-v3", current.SnapshotCatalogVersion);
            Assert.Equal(2027, current.SnapshotComputationYear);
            Assert.Equal(2028, current.Data!.ValidYearUpperBound);
        }
    }

    private ServiceProvider CreateServices(TimeProvider clock, string catalogVersion,
        string? failingPersonelId = null)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:AcademicDatabase"] = fixture.ConnectionString,
                ["BulkCollection:WorkerEnabled"] = "false",
                ["ArticleSummaryAutomation:Enabled"] = "false",
                ["ArticleSummaryAutomation:WorkerEnabled"] = "false",
                ["PublicationMetrics:WorkerEnabled"] = "true",
                ["PublicationMetrics:BatchSize"] = "100",
                ["PublicationMetrics:RetrySeconds"] = "1",
                ["PublicationMetrics:CatalogVersion"] = catalogVersion
            }).Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddAcademicPerformanceModule(configuration);
        services.RemoveAll<TimeProvider>();
        services.AddSingleton(clock);
        if (failingPersonelId is not null)
        {
            services.AddScoped<IPublicationMetricsComputer>(provider => new TargetFailingComputer(
                failingPersonelId,
                new PublicationMetricsComputer(
                    provider.GetRequiredService<PublicationMetricSourceLoader>())));
        }
        return services.BuildServiceProvider();
    }

    private static async Task ProcessUntilCurrentAsync(ServiceProvider services, string personelId)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            using IServiceScope scope = services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<PublicationMetricsProcessor>()
                .ProcessBatchAsync();
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            if (await database.PublicationMetricsRefreshStates.AsNoTracking().AnyAsync(state =>
                state.PersonelId == personelId && state.ComputedRevision >= state.RequestedRevision &&
                state.RequestedCatalogVersion ==
                    scope.ServiceProvider.GetRequiredService<IConfiguration>()["PublicationMetrics:CatalogVersion"]))
                return;
        }
        throw new TimeoutException("Target metric revision did not become current.");
    }

    private static async Task<long> RevisionAsync(AcademicDbContext database, string personelId)
    {
        database.ChangeTracker.Clear();
        return await database.PublicationMetricsRefreshStates.AsNoTracking()
            .Where(state => state.PersonelId == personelId)
            .Select(state => state.RequestedRevision).SingleAsync();
    }

    private static AcademicWork Work(string personelId, string? doi, string providerWorkId) => new()
    {
        PersonelId = personelId, Provider = AcademicWorkProvider.OpenAlex,
        ProviderWorkId = providerWorkId, Doi = doi, PublicationYear = 2025,
        Category = AcademicWorkCategory.Article, SyncedAt = DateTime.UtcNow
    };

    private sealed class TargetFailingComputer(
        string targetPersonelId,
        IPublicationMetricsComputer inner) : IPublicationMetricsComputer
    {
        public Task<PublicationMetricComputation> ComputeAsync(string personelId,
            string catalogVersion, DateTime computedAt, CancellationToken cancellationToken) =>
            personelId == targetPersonelId
                ? throw new InvalidOperationException("secret failure detail")
                : inner.ComputeAsync(personelId, catalogVersion, computedAt, cancellationToken);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
