using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using ResearcherAnalysisService.Products;
using ResearcherAnalysisService.Products.Api.Contracts;
using ResearcherAnalysisService.Products.Data;
using ResearcherAnalysisService.Products.Metrics;
using ResearcherAnalysisService.SourceData;
using ResearcherAnalysisService.SourceData.Researchers;
using ResearcherAnalysisService.SourceData.Works;
using ResearcherAnalysisService.Tests.Infrastructure;

namespace ResearcherAnalysisService.Tests;

[Collection("Analysis Product SQL Server")]
public sealed class PublicationMetricsPersistenceTests(AnalysisProductSqlServerFixture fixture)
{
    [Fact]
    public async Task ProviderTotals_MissingRemainUnknown_WhileExplicitZeroIsPersisted()
    {
        SyntheticCanonicalSource missing = await fixture.SeedCanonicalSourceAsync(Person("missing"));
        SyntheticCanonicalSource zero = await fixture.SeedCanonicalSourceAsync(Person("zero"));
        await UpdateResearcherAsync(zero.PersonelId, researcher =>
        {
            researcher.OpenAlexCitationCount = 0;
            researcher.OpenAlexHIndex = 0;
            researcher.OpenAlexI10Index = 0;
            researcher.OpenAlexDocumentsCount = 0;
            researcher.OpenAlexTwoYearMeanCitedness = 0;
            researcher.OpenAlexMetricsUpdatedAt = DateTime.UtcNow;
        });

        FixedTimeProvider clock = new(new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero));
        await using ServiceProvider services = CreateServices(clock, "publication-metrics-v2");
        await ProcessUntilCurrentAsync(services, missing.PersonelId);
        await ProcessUntilCurrentAsync(services, zero.PersonelId);

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        PublicationMetricsReadService reader =
            scope.ServiceProvider.GetRequiredService<PublicationMetricsReadService>();
        ResearcherPublicationMetricsStatusResponse missingResult =
            (await reader.GetAsync(missing.PersonelId, CancellationToken.None))!;
        ResearcherPublicationMetricsStatusResponse zeroResult =
            (await reader.GetAsync(zero.PersonelId, CancellationToken.None))!;
        Assert.Equal("Current", missingResult.Status);
        Assert.Null(missingResult.Data!.ProviderMetrics.OpenAlex.CitationCount.Value);
        Assert.Equal("Unknown", missingResult.Data.ProviderMetrics.OpenAlex.CitationCount.Quality);
        Assert.Null(missingResult.Data.ProviderMetrics.OpenAlex.DocumentCount.Value);
        Assert.Equal(0, zeroResult.Data!.ProviderMetrics.OpenAlex.CitationCount.Value);
        Assert.Equal("Available", zeroResult.Data.ProviderMetrics.OpenAlex.CitationCount.Quality);
        Assert.Equal(0, zeroResult.Data.ProviderMetrics.OpenAlex.DocumentCount.Value);

        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        PublicationMetricProviderSnapshot missingRow = await database.PublicationMetricProviderSnapshots
            .AsNoTracking().SingleAsync(row =>
                row.SnapshotId == missingResult.SnapshotId && row.Provider == "OpenAlex");
        PublicationMetricProviderSnapshot zeroRow = await database.PublicationMetricProviderSnapshots
            .AsNoTracking().SingleAsync(row =>
                row.SnapshotId == zeroResult.SnapshotId && row.Provider == "OpenAlex");
        Assert.Null(missingRow.CitationCount);
        Assert.Null(missingRow.DocumentCount);
        Assert.Equal(0, zeroRow.CitationCount);
        Assert.Equal(0, zeroRow.DocumentCount);
        Assert.Contains("\"quality\":\"Unknown\"", missingRow.FieldMetadataJson,
            StringComparison.Ordinal);
        Assert.Contains("\"quality\":\"Available\"", zeroRow.FieldMetadataJson,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SourceChangeRetractionAndRemovalEvents_AdvanceOnlyOwnRevision_Idempotently()
    {
        SyntheticCanonicalSource first = await fixture.SeedCanonicalSourceAsync(Person("feed-first"));
        SyntheticCanonicalSource second = await fixture.SeedCanonicalSourceAsync(Person("feed-second"));
        FixedTimeProvider clock = new(new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero));
        await using ServiceProvider services = CreateServices(
            clock, "publication-metrics-v2", collectionChangesEnabled: true);

        Guid[] initial = (await InsertChangeSetAsync(first.PersonelId,
            first.CanonicalWorkId, first.AcademicWorkId))
            .Concat(await InsertChangeSetAsync(second.PersonelId,
                second.CanonicalWorkId, second.AcademicWorkId)).ToArray();
        await ProcessChangesUntilReceiptedAsync(services, initial);
        Assert.Equal(1, await RevisionAsync(services, first.PersonelId));
        Assert.Equal(1, await RevisionAsync(services, second.PersonelId));

        await AddSourceAsync(second.AcademicWorkId);
        Guid[] sourceChanged = await InsertChangeSetAsync(second.PersonelId,
            second.CanonicalWorkId, second.AcademicWorkId);
        await ProcessChangesUntilReceiptedAsync(services, sourceChanged);
        Assert.Equal(1, await RevisionAsync(services, first.PersonelId));
        Assert.Equal(2, await RevisionAsync(services, second.PersonelId));

        await MarkRetractedAsync(first);
        Guid[] retracted = await InsertChangeSetAsync(first.PersonelId,
            first.CanonicalWorkId, first.AcademicWorkId);
        await ProcessChangesUntilReceiptedAsync(services, retracted);
        Assert.Equal(2, await RevisionAsync(services, first.PersonelId));
        Assert.Equal(2, await RevisionAsync(services, second.PersonelId));

        await RemoveCanonicalAssociationAsync(second);
        Guid[] removed = await InsertChangeSetAsync(second.PersonelId,
            second.CanonicalWorkId, second.AcademicWorkId);
        await ProcessChangesUntilReceiptedAsync(services, removed);
        Assert.Equal(2, await RevisionAsync(services, first.PersonelId));
        Assert.Equal(3, await RevisionAsync(services, second.PersonelId));

        await using (AsyncServiceScope scope = services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<CollectionChangeProcessor>()
                .ProcessBatchAsync();
        Assert.Equal(2, await RevisionAsync(services, first.PersonelId));
        Assert.Equal(3, await RevisionAsync(services, second.PersonelId));
        Assert.Equal(initial.Length + sourceChanged.Length + retracted.Length + removed.Length,
            await ReceiptCountAsync(services,
                initial.Concat(sourceChanged).Concat(retracted).Concat(removed).ToArray()));
    }

    [Fact]
    public async Task Processor_FailureRetainsLastGood_ThenRestartAndCatalogYearChangeRefresh()
    {
        SyntheticCanonicalSource source = await fixture.SeedCanonicalSourceAsync(Person("retry"));
        FixedTimeProvider initialClock = new(
            new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero));
        await using ServiceProvider initial = CreateServices(
            initialClock, "publication-metrics-v2");
        await ProcessUntilCurrentAsync(initial, source.PersonelId);

        long firstSnapshotId;
        await using (AsyncServiceScope scope = initial.CreateAsyncScope())
        {
            ResearcherPublicationMetricsStatusResponse current = (await scope.ServiceProvider
                .GetRequiredService<PublicationMetricsReadService>()
                .GetAsync(source.PersonelId, CancellationToken.None))!;
            firstSnapshotId = current.SnapshotId!.Value;
            Assert.True(await scope.ServiceProvider.GetRequiredService<PublicationMetricsRefreshService>()
                .ScheduleAsync(source.PersonelId, CancellationToken.None));
        }
        await MakeDueAsync(initial, source.PersonelId);

        await using ServiceProvider failing = CreateServices(
            initialClock, "publication-metrics-v2", failingPersonelId: source.PersonelId);
        await using (AsyncServiceScope scope = failing.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<PublicationMetricsProcessor>()
                .ProcessBatchAsync();
        await using (AsyncServiceScope scope = failing.CreateAsyncScope())
        {
            AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
            PublicationMetricsRefreshState state = await database.PublicationMetricsRefreshStates
                .AsNoTracking().SingleAsync(value => value.PersonelId == source.PersonelId);
            Assert.Equal(firstSnapshotId, state.LastSuccessfulSnapshotId);
            Assert.True(state.RequestedRevision > state.ComputedRevision);
            Assert.Equal(1, state.Attempts);
            Assert.Equal("MetricComputationFailed", state.LastOutcomeCode);
            Assert.DoesNotContain("secret", state.LastOutcomeMessage!,
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, await database.PublicationMetricSnapshots.CountAsync(snapshot =>
                snapshot.PersonelId == source.PersonelId));
        }

        await MakeDueAsync(failing, source.PersonelId);
        await using ServiceProvider restarted = CreateServices(
            initialClock, "publication-metrics-v2");
        await ProcessUntilCurrentAsync(restarted, source.PersonelId);

        FixedTimeProvider nextYear = new(
            new DateTimeOffset(2027, 1, 2, 12, 0, 0, TimeSpan.Zero));
        await using ServiceProvider upgraded = CreateServices(
            nextYear, "publication-metrics-v3");
        await using (AsyncServiceScope scope = upgraded.CreateAsyncScope())
        {
            ResearcherPublicationMetricsStatusResponse stale = (await scope.ServiceProvider
                .GetRequiredService<PublicationMetricsReadService>()
                .GetAsync(source.PersonelId, CancellationToken.None))!;
            Assert.Equal("Stale", stale.Status);
            Assert.True(stale.IsStale);
            Assert.Equal("publication-metrics-v2", stale.SnapshotCatalogVersion);
            Assert.Equal(2026, stale.SnapshotComputationYear);
        }

        await ProcessUntilCurrentAsync(upgraded, source.PersonelId);
        await using (AsyncServiceScope scope = upgraded.CreateAsyncScope())
        {
            ResearcherPublicationMetricsStatusResponse current = (await scope.ServiceProvider
                .GetRequiredService<PublicationMetricsReadService>()
                .GetAsync(source.PersonelId, CancellationToken.None))!;
            Assert.Equal("Current", current.Status);
            Assert.False(current.IsStale);
            Assert.Equal("publication-metrics-v3", current.SnapshotCatalogVersion);
            Assert.Equal(2027, current.SnapshotComputationYear);
            Assert.Equal(2028, current.Data!.ValidYearUpperBound);
            Assert.True(current.SnapshotId > firstSnapshotId);
        }
    }

    private ServiceProvider CreateServices(
        TimeProvider clock,
        string catalogVersion,
        string? failingPersonelId = null,
        bool collectionChangesEnabled = false)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:UsageDatabase"] = fixture.ConnectionString,
                ["CollectionChanges:WorkerEnabled"] = collectionChangesEnabled.ToString(),
                ["CollectionChanges:BatchSize"] = "100",
                ["ArticleSummaryAutomation:Enabled"] = "false",
                ["ArticleSummaryAutomation:WorkerEnabled"] = "false",
                ["FacultyAssistant:WorkerEnabled"] = "false",
                ["ArticleEvaluation:WorkerEnabled"] = "false",
                ["PublicationMetrics:WorkerEnabled"] = "true",
                ["PublicationMetrics:BatchSize"] = "100",
                ["PublicationMetrics:RetrySeconds"] = "1",
                ["PublicationMetrics:CatalogVersion"] = catalogVersion
            }).Build();
        ServiceCollection services = new();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton(configuration);
        services.AddAcademicProducts(configuration);
        services.RemoveAll<TimeProvider>();
        services.AddSingleton(clock);
        if (failingPersonelId is not null)
        {
            services.RemoveAll<IPublicationMetricsComputer>();
            services.AddScoped<IPublicationMetricsComputer>(provider => new TargetFailingComputer(
                failingPersonelId,
                new PublicationMetricsComputer(
                    provider.GetRequiredService<PublicationMetricSourceLoader>())));
        }
        return services.BuildServiceProvider();
    }

    private static async Task ProcessUntilCurrentAsync(
        ServiceProvider services, string personelId)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<PublicationMetricsProcessor>()
                .ProcessBatchAsync();
            AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
            PublicationMetricsOptions settings = scope.ServiceProvider
                .GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<PublicationMetricsOptions>>()
                .CurrentValue;
            if (await database.PublicationMetricsRefreshStates.AsNoTracking().AnyAsync(state =>
                state.PersonelId == personelId &&
                state.ComputedRevision >= state.RequestedRevision &&
                state.RequestedCatalogVersion == settings.CatalogVersion &&
                state.RequestedComputationYear ==
                    scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().Year))
                return;
        }
        throw new TimeoutException("Target metric revision did not become current.");
    }

    private static async Task ProcessChangesUntilReceiptedAsync(
        ServiceProvider services, IReadOnlyCollection<Guid> eventIds)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<CollectionChangeProcessor>()
                .ProcessBatchAsync();
            AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
            int receipts = await database.CollectionChangeReceipts.AsNoTracking()
                .CountAsync(value => eventIds.Contains(value.EventId));
            if (receipts == eventIds.Count)
                return;
        }
        throw new TimeoutException("Target collection changes were not receipted.");
    }

    private async Task<Guid[]> InsertChangeSetAsync(
        string personelId, int canonicalWorkId, int academicWorkId)
    {
        Guid researcherEvent = Guid.NewGuid();
        Guid canonicalEvent = Guid.NewGuid();
        DateTime occurredAt = DateTime.UtcNow;
        await using DbContext database = fixture.CreateSeedContext();
        database.Set<CollectionChange>().AddRange(
            new()
            {
                EventId = researcherEvent,
                ChangeKind = "ResearcherCollected",
                PersonelId = personelId,
                OccurredAtUtc = occurredAt,
                PayloadVersion = 1
            },
            new()
            {
                EventId = canonicalEvent,
                ChangeKind = "CanonicalWorkChanged",
                PersonelId = personelId,
                CanonicalWorkId = canonicalWorkId,
                AcademicWorkId = academicWorkId,
                OccurredAtUtc = occurredAt,
                PayloadVersion = 1
            });
        await database.SaveChangesAsync();
        return [researcherEvent, canonicalEvent];
    }

    private async Task UpdateResearcherAsync(
        string personelId, Action<Researcher> update)
    {
        await using DbContext database = fixture.CreateSeedContext();
        Researcher researcher = await database.Set<Researcher>()
            .SingleAsync(value => value.PersonelId == personelId);
        update(researcher);
        await database.SaveChangesAsync();
    }

    private async Task AddSourceAsync(int academicWorkId)
    {
        await using DbContext database = fixture.CreateSeedContext();
        database.Set<AcademicWorkSource>().Add(new()
        {
            AcademicWorkId = academicWorkId,
            Kind = "Pdf",
            Origin = "SyntheticSemanticScholar",
            Url = "https://source.test/" + Guid.NewGuid().ToString("N") + ".pdf",
            IsOpenAccess = true
        });
        await database.SaveChangesAsync();
    }

    private async Task MarkRetractedAsync(SyntheticCanonicalSource source)
    {
        await using DbContext database = fixture.CreateSeedContext();
        AcademicWork work = await database.Set<AcademicWork>()
            .SingleAsync(value => value.Id == source.AcademicWorkId);
        CanonicalWorkObservation observation = await database.Set<CanonicalWorkObservation>()
            .SingleAsync(value => value.AcademicWorkId == source.AcademicWorkId);
        CanonicalWork canonical = await database.Set<CanonicalWork>()
            .SingleAsync(value => value.Id == source.CanonicalWorkId);
        work.IsRetracted = true;
        observation.IsRetracted = true;
        canonical.HasRetractionObservation = true;
        canonical.UpdatedAt = DateTime.UtcNow;
        await database.SaveChangesAsync();
    }

    private async Task RemoveCanonicalAssociationAsync(SyntheticCanonicalSource source)
    {
        await using DbContext database = fixture.CreateSeedContext();
        CanonicalWorkObservation observation = await database.Set<CanonicalWorkObservation>()
            .SingleAsync(value => value.AcademicWorkId == source.AcademicWorkId);
        CanonicalResearcherWork association = await database.Set<CanonicalResearcherWork>()
            .SingleAsync(value => value.CanonicalWorkId == source.CanonicalWorkId &&
                value.PersonelId == source.PersonelId);
        database.RemoveRange(observation, association);
        await database.SaveChangesAsync();
    }

    private static async Task MakeDueAsync(ServiceProvider services, string personelId)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        PublicationMetricsRefreshState state = await database.PublicationMetricsRefreshStates
            .SingleAsync(value => value.PersonelId == personelId);
        state.NextAttemptAt = DateTime.UnixEpoch;
        await database.SaveChangesAsync();
    }

    private static async Task<long> RevisionAsync(
        ServiceProvider services, string personelId)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AnalysisDbContext>()
            .PublicationMetricsRefreshStates.AsNoTracking()
            .Where(state => state.PersonelId == personelId)
            .Select(state => state.RequestedRevision).SingleAsync();
    }

    private static async Task<int> ReceiptCountAsync(
        ServiceProvider services, IReadOnlyCollection<Guid> eventIds)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AnalysisDbContext>()
            .CollectionChangeReceipts.AsNoTracking()
            .CountAsync(value => eventIds.Contains(value.EventId));
    }

    private static string Person(string prefix) =>
        "metrics-" + prefix + "-" + Guid.NewGuid().ToString("N");

    private sealed class TargetFailingComputer(
        string targetPersonelId,
        IPublicationMetricsComputer inner) : IPublicationMetricsComputer
    {
        public Task<PublicationMetricComputation> ComputeAsync(
            string personelId,
            string catalogVersion,
            DateTime computedAt,
            CancellationToken cancellationToken) => personelId == targetPersonelId
                ? throw new InvalidOperationException("secret failure detail")
                : inner.ComputeAsync(personelId, catalogVersion, computedAt, cancellationToken);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
