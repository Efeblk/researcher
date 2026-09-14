using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResearcherAnalysisService.Products.Data;
using ResearcherAnalysisService.Products.Metrics;
using ResearcherAnalysisService.SourceData.Researchers;
using ResearcherAnalysisService.Tests.Infrastructure;

namespace ResearcherAnalysisService.Tests;

[Collection("Analysis Product SQL Server")]
public sealed class PublicationMetricsEndpointTests(AnalysisProductSqlServerFixture fixture)
{
    [Fact]
    public async Task RefreshAndRead_PersistsCurrentSnapshotWithoutChangingSourceRows()
    {
        SyntheticCanonicalSource source = await fixture.SeedCanonicalSourceAsync(
            "metrics-" + Guid.NewGuid().ToString("N"));
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        TestOptionsMonitor<PublicationMetricsOptions> options = new(new()
        {
            WorkerEnabled = true,
            BatchSize = 10,
            CatalogVersion = PublicationMetricCatalog.Version
        });
        TimeProvider time = TimeProvider.System;
        AnalysisSourceLock sourceLock = scope.ServiceProvider.GetRequiredService<AnalysisSourceLock>();
        PublicationMetricsRefreshScheduler scheduler = new(database, options, time);
        PublicationMetricsRefreshService refresh = new(database, sourceLock, scheduler);
        PublicationMetricsProcessor processor = new(database,
            scope.ServiceProvider.GetRequiredService<IPublicationMetricsComputer>(),
            sourceLock, options, time);

        Assert.True(await refresh.ScheduleAsync(source.PersonelId, default));
        Assert.True(await processor.ProcessBatchAsync() >= 1);
        ResearcherAnalysisService.Products.Api.Contracts.ResearcherPublicationMetricsStatusResponse? result =
            await new PublicationMetricsReadService(database, options, time)
                .GetAsync(source.PersonelId, default);

        Assert.NotNull(result);
        Assert.Equal("Current", result.Status);
        Assert.False(result.IsStale);
        Assert.Equal(1, result.Data!.CanonicalWorkCount);
        Assert.Equal(1, await database.Researchers.CountAsync(value =>
            value.PersonelId == source.PersonelId));
        Assert.Single(await database.PublicationMetricSnapshots.Where(value =>
            value.PersonelId == source.PersonelId).ToListAsync());
    }

    [Fact]
    public async Task BackgroundBatch_DiscoversEmptyResearcherAndPersistsZeroSnapshot()
    {
        string personelId = "metrics-empty-" + Guid.NewGuid().ToString("N");
        await using (DbContext source = fixture.CreateSeedContext())
        {
            source.Add(new Researcher { PersonelId = personelId });
            await source.SaveChangesAsync();
        }
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        TestOptionsMonitor<PublicationMetricsOptions> options = new(new()
        {
            WorkerEnabled = true,
            BatchSize = 10,
            CatalogVersion = PublicationMetricCatalog.Version
        });
        PublicationMetricsProcessor processor = new(database,
            scope.ServiceProvider.GetRequiredService<IPublicationMetricsComputer>(),
            scope.ServiceProvider.GetRequiredService<AnalysisSourceLock>(),
            options, TimeProvider.System);

        Assert.True(await processor.ProcessBatchAsync() >= 1);
        PublicationMetricSnapshot snapshot = await database.PublicationMetricSnapshots
            .SingleAsync(value => value.PersonelId == personelId);
        Assert.Equal(0, snapshot.CanonicalWorkCount);
        Assert.Equal(0, snapshot.ProviderObservationCount);
    }
}
