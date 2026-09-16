using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResearcherAnalysisService.Products.Api.Contracts;
using ResearcherAnalysisService.Products.Data;
using ResearcherAnalysisService.Products.Metrics;
using ResearcherAnalysisService.SourceData.Researchers;
using ResearcherAnalysisService.SourceData.Works;
using ResearcherAnalysisService.Tests.Infrastructure;

namespace ResearcherAnalysisService.Tests;

[Collection("Analysis Product SQL Server")]
public sealed class PublicationMetricsEndpointTests(AnalysisProductSqlServerFixture fixture)
{
    private const string GetMetrics = "/api/v1/products/GetResearcherPublicationMetrics";
    private const string RefreshMetrics = "/api/v1/products/RefreshResearcherPublicationMetrics";

    [Fact]
    public async Task GetAndRefreshResearcherPublicationMetrics_PersistedLifecycle_IsScopedAndReadOnly()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string ownerId = "metrics-owner-" + suffix;
        string otherId = "metrics-other-" + suffix;
        string ownerTopicId = "owner-topic-" + suffix;
        string otherTopicId = "other-topic-" + suffix;
        await SeedSharedCanonicalSourceAsync(ownerId, otherId, ownerTopicId, otherTopicId);
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(
            usageDatabase: fixture.ConnectionString);

        using (HttpResponseMessage nullRequest = await host.Client.PostAsync(GetMetrics,
            new StringContent("null", Encoding.UTF8, "application/json")))
            Assert.Equal(HttpStatusCode.BadRequest, nullRequest.StatusCode);
        using (HttpResponseMessage missingId = await host.Client.PostAsJsonAsync(GetMetrics, new { }))
            Assert.Equal(HttpStatusCode.BadRequest, missingId.StatusCode);
        using (HttpResponseMessage tooLong = await host.Client.PostAsJsonAsync(GetMetrics,
            new { PersonelID = new string('x', 201) }))
            Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        using (HttpResponseMessage missingResearcher = await host.Client.PostAsJsonAsync(GetMetrics,
            new { PersonelID = "missing-" + suffix }))
            Assert.Equal(HttpStatusCode.NotFound, missingResearcher.StatusCode);

        ResearcherPublicationMetricsStatusResponse pending = await GetAsync(
            host, "  " + ownerId + "  ", HttpStatusCode.Accepted);
        Assert.Equal("Pending", pending.Status);
        Assert.Null(pending.Data);

        using (HttpResponseMessage refreshResponse = await host.Client.PostAsJsonAsync(
            RefreshMetrics, new { PersonelID = ownerId }))
        {
            Assert.Equal(HttpStatusCode.Accepted, refreshResponse.StatusCode);
            ResearcherPublicationMetricsStatusResponse scheduled =
                (await refreshResponse.Content.ReadFromJsonAsync<ResearcherPublicationMetricsStatusResponse>())!;
            Assert.Equal("Pending", scheduled.Status);
            Assert.Equal(1, scheduled.RequestedRevision);
            Assert.Null(scheduled.Data);
        }

        await ProcessUntilCurrentAsync(ownerId);
        ResearcherPublicationMetricsStatusResponse current = await GetAsync(
            host, ownerId, HttpStatusCode.OK);
        Assert.Equal("Current", current.Status);
        Assert.False(current.IsStale);
        ResearcherPublicationMetricsResponse result = Assert.IsType<ResearcherPublicationMetricsResponse>(current.Data);
        Assert.Equal(ownerId, result.PersonelId);
        Assert.Equal(1, result.CanonicalWorkCount);
        Assert.Equal(1, result.ProviderObservationCount);
        Assert.Equal(0, result.UnmappedAcademicWorkCount);
        Assert.Equal([(2020, 1)], result.PublicationYears.Histogram
            .Select(bucket => (bucket.Year, bucket.CanonicalWorkCount)));
        Assert.Equal(1, CategoryCount(result, AcademicWorkCategory.Article));
        Assert.Equal(0, CategoryCount(result, AcademicWorkCategory.Patent));
        Assert.Equal(5, result.ProviderMetrics.OpenAlex.CitationCount.Value);
        PublicationPrimaryTopicBucketDto topic = Assert.Single(result.ContextualMetrics.PrimaryTopics);
        Assert.Equal(ownerTopicId, topic.TopicId);
        Assert.DoesNotContain(result.ContextualMetrics.PrimaryTopics,
            value => value.TopicId == otherTopicId);
        Assert.Equal(1.5m, result.ContextualMetrics.ProviderReportedNormalization.Fwci.MeanValue);

        long firstSnapshotId = current.SnapshotId!.Value;
        string originalJson;
        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
            originalJson = await database.PublicationMetricSnapshots.AsNoTracking()
                .Where(snapshot => snapshot.Id == firstSnapshotId)
                .Select(snapshot => snapshot.ResultJson).SingleAsync();
            Assert.Single(await database.PublicationMetricSnapshots.AsNoTracking()
                .Where(snapshot => snapshot.PersonelId == ownerId).ToListAsync());
        }

        Assert.Equal(firstSnapshotId, (await GetAsync(host, ownerId, HttpStatusCode.OK)).SnapshotId);
        await using (DbContext source = fixture.CreateSeedContext())
        {
            Researcher owner = await source.Set<Researcher>().SingleAsync(value => value.PersonelId == ownerId);
            owner.OpenAlexCitationCount = 17;
            source.Add(new AcademicWork
            {
                PersonelId = ownerId,
                Provider = AcademicWorkProvider.Orcid,
                ProviderWorkId = "unmapped-" + suffix,
                Title = "Unmapped source row",
                PublicationYear = 2025,
                Category = AcademicWorkCategory.Other,
                SyncedAt = DateTime.UtcNow
            });
            await source.SaveChangesAsync();
        }

        ResearcherPublicationMetricsStatusResponse unchanged = await GetAsync(
            host, ownerId, HttpStatusCode.OK);
        Assert.Equal(firstSnapshotId, unchanged.SnapshotId);
        Assert.Equal(5, unchanged.Data!.ProviderMetrics.OpenAlex.CitationCount.Value);
        Assert.Equal(0, unchanged.Data.UnmappedAcademicWorkCount);

        using (HttpResponseMessage refreshResponse = await host.Client.PostAsJsonAsync(
            RefreshMetrics, new { PersonelID = ownerId }))
        {
            Assert.Equal(HttpStatusCode.Accepted, refreshResponse.StatusCode);
            ResearcherPublicationMetricsStatusResponse stale =
                (await refreshResponse.Content.ReadFromJsonAsync<ResearcherPublicationMetricsStatusResponse>())!;
            Assert.Equal("Stale", stale.Status);
            Assert.True(stale.IsStale);
            Assert.Equal(firstSnapshotId, stale.SnapshotId);
        }

        await ProcessUntilCurrentAsync(ownerId);
        ResearcherPublicationMetricsStatusResponse refreshed = await GetAsync(
            host, ownerId, HttpStatusCode.OK);
        Assert.NotEqual(firstSnapshotId, refreshed.SnapshotId);
        Assert.Equal(17, refreshed.Data!.ProviderMetrics.OpenAlex.CitationCount.Value);
        Assert.Equal(1, refreshed.Data.UnmappedAcademicWorkCount);

        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
            Assert.Equal(originalJson, await database.PublicationMetricSnapshots.AsNoTracking()
                .Where(snapshot => snapshot.Id == firstSnapshotId)
                .Select(snapshot => snapshot.ResultJson).SingleAsync());
            Assert.Equal(2, await database.PublicationMetricSnapshots.AsNoTracking()
                .CountAsync(snapshot => snapshot.PersonelId == ownerId));
            Assert.Equal(1, await database.AcademicWorkResearchContexts.AsNoTracking()
                .CountAsync(context => context.AcademicWork!.PersonelId == ownerId && context.Fwci == 1.5m));
            Assert.Equal(1, await database.AcademicWorkResearchContexts.AsNoTracking()
                .CountAsync(context => context.AcademicWork!.PersonelId == otherId && context.Fwci == 99m));
        }
    }

    [Fact]
    public async Task MetricsBackgroundWorker_DiscoversEmptyResearcher_AndPersistsFirstSnapshot()
    {
        string personelId = "metrics-hosted-" + Guid.NewGuid().ToString("N");
        await using (DbContext source = fixture.CreateSeedContext())
        {
            source.Add(new Researcher { PersonelId = personelId });
            await source.SaveChangesAsync();
        }
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(
            usageDatabase: fixture.ConnectionString,
            settings: new Dictionary<string, string?>
            {
                ["PublicationMetrics:WorkerEnabled"] = "true",
                ["PublicationMetrics:PollSeconds"] = "1",
                ["PublicationMetrics:BatchSize"] = "100"
            });

        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            using HttpResponseMessage response = await host.Client.PostAsJsonAsync(
                GetMetrics, new { PersonelID = personelId });
            if (response.StatusCode == HttpStatusCode.OK)
            {
                ResearcherPublicationMetricsStatusResponse ready =
                    (await response.Content.ReadFromJsonAsync<ResearcherPublicationMetricsStatusResponse>())!;
                Assert.Equal("Current", ready.Status);
                Assert.Equal(0, ready.Data!.CanonicalWorkCount);
                Assert.Equal(0, ready.Data.ProviderObservationCount);
                Assert.Null(ready.Data.Coverage.SavedAbstract.Proportion);
                return;
            }
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            await Task.Delay(200);
        }
        throw new TimeoutException("Metrics worker did not persist the initial empty snapshot.");
    }

    private async Task SeedSharedCanonicalSourceAsync(
        string ownerId, string otherId, string ownerTopicId, string otherTopicId)
    {
        DateTime now = DateTime.UtcNow;
        Researcher owner = new() { PersonelId = ownerId, OpenAlexCitationCount = 5 };
        Researcher other = new() { PersonelId = otherId, OpenAlexCitationCount = 999 };
        CanonicalWork canonical = new()
        {
            NormalizedDoi = "10.9876/" + Guid.NewGuid().ToString("N"),
            CreatedAt = now,
            UpdatedAt = now
        };
        AcademicWork ownerWork = CreateWork(owner, canonical, "owner-work-" + ownerTopicId,
            2020, AcademicWorkCategory.Article, 1.5m, ownerTopicId, "Owner topic");
        AcademicWork otherWork = CreateWork(other, canonical, "other-work-" + otherTopicId,
            1990, AcademicWorkCategory.Patent, 99m, otherTopicId, "Other topic");
        CanonicalResearcherWork ownerAssociation = new()
        {
            CanonicalWork = canonical,
            Researcher = owner,
            PersonelId = ownerId,
            LastObservedAt = now
        };
        CanonicalResearcherWork otherAssociation = new()
        {
            CanonicalWork = canonical,
            Researcher = other,
            PersonelId = otherId,
            LastObservedAt = now
        };

        await using DbContext source = fixture.CreateSeedContext();
        source.AddRange(ownerWork, otherWork, ownerAssociation, otherAssociation);
        await source.SaveChangesAsync();
    }

    private static AcademicWork CreateWork(Researcher researcher, CanonicalWork canonical,
        string providerWorkId, int year, AcademicWorkCategory category, decimal fwci,
        string topicId, string topicName)
    {
        DateTime now = DateTime.UtcNow;
        AcademicWork work = new()
        {
            Researcher = researcher,
            PersonelId = researcher.PersonelId,
            Provider = AcademicWorkProvider.OpenAlex,
            ProviderWorkId = providerWorkId,
            Title = topicName + " publication",
            Doi = canonical.NormalizedDoi,
            PublicationYear = year,
            Category = category,
            Abstract = topicName + " abstract",
            SyncedAt = now,
            CanonicalObservation = new()
            {
                CanonicalWork = canonical,
                PersonelId = researcher.PersonelId,
                Provider = AcademicWorkProvider.OpenAlex,
                ProviderWorkId = providerWorkId,
                PublicationYearObserved = year,
                CategoryObserved = category,
                ObservedAt = now
            },
            ResearchContext = new()
            {
                Provider = "OpenAlex",
                SourceWorkId = providerWorkId,
                ParserVersion = "synthetic-v1",
                PayloadFingerprint = Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant(),
                SourceSyncedAt = now,
                SourcePublicationYear = year,
                RawType = category == AcademicWorkCategory.Article ? "article" : "patent",
                PrimarySourceType = "journal",
                Fwci = fwci,
                ParseQuality = "Available",
                PrimaryTopicQuality = "Available",
                ValueQualityJson = "{}",
                Topics =
                [
                    new()
                    {
                        TopicId = topicId,
                        TopicName = topicName,
                        SubfieldId = "subfield-" + topicId,
                        SubfieldName = topicName + " subfield",
                        FieldId = "field-" + topicId,
                        FieldName = topicName + " field",
                        OriginalRank = 1,
                        IsPrimary = true
                    }
                ]
            }
        };
        return work;
    }

    private async Task ProcessUntilCurrentAsync(string personelId)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
            AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
            TestOptionsMonitor<PublicationMetricsOptions> options = new(new()
            {
                WorkerEnabled = true,
                BatchSize = 100,
                CatalogVersion = PublicationMetricCatalog.Version
            });
            PublicationMetricsProcessor processor = new(database,
                scope.ServiceProvider.GetRequiredService<IPublicationMetricsComputer>(),
                scope.ServiceProvider.GetRequiredService<AnalysisSourceLock>(),
                options, TimeProvider.System);
            await processor.ProcessBatchAsync();
            database.ChangeTracker.Clear();
            if (await database.PublicationMetricsRefreshStates.AsNoTracking().AnyAsync(state =>
                state.PersonelId == personelId && state.ComputedRevision >= state.RequestedRevision))
                return;
        }
        throw new TimeoutException("Target metrics revision was not computed.");
    }

    private static async Task<ResearcherPublicationMetricsStatusResponse> GetAsync(
        AnalysisTestHost host, string personelId, HttpStatusCode expected)
    {
        using HttpResponseMessage response = await host.Client.PostAsJsonAsync(
            GetMetrics, new { PersonelID = personelId });
        Assert.Equal(expected, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ResearcherPublicationMetricsStatusResponse>())!;
    }

    private static int CategoryCount(ResearcherPublicationMetricsResponse result,
        AcademicWorkCategory category) => result.Categories.Histogram
            .Single(bucket => bucket.Category == category.ToString()).CanonicalWorkCount;
}
