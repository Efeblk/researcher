using AcademicCollectorDemo.Modules.AcademicPerformance;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Metrics;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Persistence;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;
using AcademicCollectorDemo.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class AcademicWorkResearchContextTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Worker_NormalizesPreexistingOwnPayload_AndKeepsCachedAndHistoricalResults()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string ownerId = "context-owner-" + suffix;
        string otherId = "context-other-" + suffix;
        string doi = "10.9911/" + suffix;
        string ownerWorkId = "https://openalex.org/W" + suffix;
        string otherWorkId = "https://openalex.org/W9" + suffix;
        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            database.Researchers.AddRange(new Researcher { PersonelId = ownerId },
                new Researcher { PersonelId = otherId });
            database.AcademicWorks.AddRange(
                Work(ownerId, ownerWorkId, doi, Payload(ownerWorkId, "T1", "T2", 1m)),
                Work(otherId, otherWorkId, doi, Payload(otherWorkId, "T9", "T8", 99m)));
            await database.SaveChangesAsync();

            // Build canonical rows without the optional context hook to represent pre-v3 data.
            CanonicalWorkSynchronizer legacyCanonical = new(database);
            await legacyCanonical.SyncAsync(ownerId);
            await legacyCanonical.SyncAsync(otherId);
            Assert.False(await database.AcademicWorkResearchContexts.AnyAsync(context =>
                context.AcademicWork!.PersonelId == ownerId ||
                context.AcademicWork.PersonelId == otherId));
            await scope.ServiceProvider.GetRequiredService<AcademicWorkResearchContextSynchronizer>()
                .SynchronizeAsync(otherId);
        }

        await using ServiceProvider metrics = CreateMetricsServices();
        await ScheduleAndProcessAsync(metrics, ownerId);
        ResearcherPublicationMetricsStatusResponse first = await ReadAsync(metrics, ownerId);
        Assert.Equal("publication-metrics-v3", first.Data!.CatalogVersion);
        Assert.Equal(1, first.Data.ContextualMetrics.EligibleCanonicalWorkCount);
        Assert.Equal(1, first.Data.ContextualMetrics.OpenAlexContextCoverage.Numerator);
        Assert.Equal("T1", Assert.Single(first.Data.ContextualMetrics.PrimaryTopics).TopicId);
        Assert.Equal(1m, first.Data.ContextualMetrics.ProviderReportedNormalization.Fwci.MeanValue);
        Assert.Null(first.Data.ContextualMetrics.InternalNormalizedScore);
        long firstSnapshotId = first.SnapshotId!.Value;

        using (var host = new HostProcess(fixture.ConnectionString, "http://127.0.0.1:1/"))
        {
            await host.WaitUntilReadyAsync();
            using HttpResponseMessage publicationResponse = await host.Client.PostAsJsonAsync(
                "/Services/AcademicPerformance/V1/ListCanonicalPublications",
                new { PersonelID = ownerId, Take = 10 });
            publicationResponse.EnsureSuccessStatusCode();
            CanonicalPublicationListResponse httpPublications = (await publicationResponse.Content
                .ReadFromJsonAsync<CanonicalPublicationListResponse>())!;
            Assert.Equal("T1", Assert.Single(Assert.Single(httpPublications.Entities).Observations)
                .ResearchContext!.Topics.Single(topic => topic.IsPrimary).TopicId);
            using HttpResponseMessage metricResponse = await host.Client.PostAsJsonAsync(
                "/Services/AcademicPerformance/V1/GetResearcherPublicationMetrics",
                new { PersonelID = ownerId });
            metricResponse.EnsureSuccessStatusCode();
            ResearcherPublicationMetricsStatusResponse httpMetrics = (await metricResponse.Content
                .ReadFromJsonAsync<ResearcherPublicationMetricsStatusResponse>())!;
            Assert.Equal(1m, httpMetrics.Data!.ContextualMetrics.ProviderReportedNormalization
                .Fwci.MeanValue);
            Assert.Contains(httpMetrics.Data.ContextualMetrics.PrimaryFieldGroups,
                group => group.ClassificationId == "27" && group.RawType == "article" &&
                    group.ArticlePrimarySourceType == "journal");
        }

        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            CanonicalPublicationListResponse publications = await scope.ServiceProvider
                .GetRequiredService<CanonicalWorkQueryService>()
                .ListAsync(ownerId, new CanonicalPublicationListRequest { Take = 10 });
            CanonicalPublicationObservationDto observation = Assert.Single(
                Assert.Single(publications.Entities).Observations);
            AcademicWorkResearchContextDto context = Assert.IsType<AcademicWorkResearchContextDto>(
                observation.ResearchContext);
            Assert.Equal(["T1", "T2"], context.Topics.Select(topic => topic.TopicId));
            Assert.Equal("T1", context.Topics.Single(topic => topic.IsPrimary).TopicId);
            Assert.DoesNotContain(context.Topics, topic => topic.TopicId == "T9");

            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            AcademicWork ownerWork = await database.AcademicWorks.SingleAsync(work =>
                work.PersonelId == ownerId);
            ownerWork.ProviderPayload = Payload(ownerWorkId, "T3", "T1", 4m);
            ownerWork.SyncedAt = ownerWork.SyncedAt.AddMinutes(1);
            await database.SaveChangesAsync();
        }

        ResearcherPublicationMetricsStatusResponse cached = await ReadAsync(metrics, ownerId);
        Assert.Equal(firstSnapshotId, cached.SnapshotId);
        Assert.Equal("T1", Assert.Single(cached.Data!.ContextualMetrics.PrimaryTopics).TopicId);

        await ScheduleAndProcessAsync(metrics, ownerId);
        ResearcherPublicationMetricsStatusResponse second = await ReadAsync(metrics, ownerId);
        Assert.NotEqual(firstSnapshotId, second.SnapshotId);
        Assert.Equal("T3", Assert.Single(second.Data!.ContextualMetrics.PrimaryTopics).TopicId);
        Assert.Equal(4m, second.Data.ContextualMetrics.ProviderReportedNormalization.Fwci.MeanValue);

        using (IServiceScope scope = metrics.CreateScope())
        {
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            AcademicWorkResearchContext current = await database.AcademicWorkResearchContexts
                .AsNoTracking().Include(context => context.Topics)
                .SingleAsync(context => context.AcademicWork!.PersonelId == ownerId);
            Assert.Equal(["T3", "T1"], current.Topics.OrderBy(topic => topic.OriginalRank)
                .Select(topic => topic.TopicId));
            Assert.Equal("T3", current.Topics.Single(topic => topic.IsPrimary).TopicId);
            string oldJson = await database.PublicationMetricSnapshots.AsNoTracking()
                .Where(snapshot => snapshot.Id == firstSnapshotId)
                .Select(snapshot => snapshot.ResultJson).SingleAsync();
            Assert.Contains("T1", oldJson);
            Assert.DoesNotContain("T3", oldJson);

            AcademicWork tracked = await database.AcademicWorks.SingleAsync(work =>
                work.PersonelId == ownerId);
            AcademicWorkResearchContextSynchronizer synchronizer = scope.ServiceProvider
                .GetRequiredService<AcademicWorkResearchContextSynchronizer>();
            tracked.ProviderPayload = Payload(ownerWorkId, "T3", "T3", 4m);
            await synchronizer.SynchronizeAsync(ownerId);
            tracked.ProviderPayload = Payload(ownerWorkId, "T1", "T3", 4m);
            await synchronizer.SynchronizeAsync(ownerId);
            Assert.Equal(["T1", "T3"], await database.AcademicWorkTopics.AsNoTracking()
                .Where(topic => topic.AcademicWorkId == tracked.Id)
                .OrderBy(topic => topic.OriginalRank).Select(topic => topic.TopicId).ToArrayAsync());
        }
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
                ["PublicationMetrics:BatchSize"] = "100",
                ["PublicationMetrics:CatalogVersion"] = "publication-metrics-v3"
            }).Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddAcademicPerformanceModule(configuration);
        return services.BuildServiceProvider();
    }

    private static async Task ScheduleAndProcessAsync(ServiceProvider services, string personelId)
    {
        using (IServiceScope scope = services.CreateScope())
        {
            Assert.True(await scope.ServiceProvider.GetRequiredService<PublicationMetricsRefreshService>()
                .ScheduleAsync(personelId, CancellationToken.None));
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            await database.PublicationMetricsRefreshStates.Where(state => state.PersonelId == personelId)
                .ExecuteUpdateAsync(update => update.SetProperty(
                    state => state.NextAttemptAt, DateTime.UnixEpoch));
        }
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
        throw new TimeoutException("Target context metric revision was not computed.");
    }

    private static async Task<ResearcherPublicationMetricsStatusResponse> ReadAsync(
        ServiceProvider services, string personelId)
    {
        using IServiceScope scope = services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<PublicationMetricsReadService>()
            .GetAsync(personelId, CancellationToken.None))!;
    }

    private static AcademicWork Work(
        string personelId, string providerWorkId, string doi, string payload) => new()
    {
        PersonelId = personelId,
        Provider = AcademicWorkProvider.OpenAlex,
        ProviderWorkId = providerWorkId,
        Doi = doi,
        PublicationYear = 2024,
        RawType = "article",
        Category = AcademicWorkCategory.Article,
        ProviderPayload = payload,
        SyncedAt = new DateTime(2026, 4, 5, 0, 0, 0, DateTimeKind.Utc)
    };

    private static string Payload(
        string workId, string primaryTopicId, string secondaryTopicId, decimal fwci) => $$$"""
        {
          "id":"{{{workId}}}","updated_date":"2026-04-03","publication_year":2024,
          "type":"article","primary_location":{"source":{"type":"journal"}},
          "fwci":{{{fwci.ToString(System.Globalization.CultureInfo.InvariantCulture)}}},
          "citation_normalized_percentile":{"value":0,"is_in_top_1_percent":false,"is_in_top_10_percent":false},
          "primary_topic":{"id":"{{{primaryTopicId}}}","subfield":{"id":2740},"field":{"id":27},"domain":{"id":4}},
          "topics":[
            {"id":"{{{primaryTopicId}}}","display_name":"Primary {{{primaryTopicId}}}","score":0.9,
             "subfield":{"id":2740,"display_name":"AI"},"field":{"id":27,"display_name":"Computer Science"},
             "domain":{"id":4,"display_name":"Physical Sciences"}},
            {"id":"{{{secondaryTopicId}}}","display_name":"Secondary {{{secondaryTopicId}}}","score":0.4}
          ]
        }
        """;
}
