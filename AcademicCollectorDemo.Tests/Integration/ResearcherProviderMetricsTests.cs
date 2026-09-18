using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.GoogleScholar;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Scopus;
using AcademicCollectorDemo.Modules.AcademicPerformance.Application;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Metrics;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;
using AcademicCollectorDemo.Tests.Infrastructure;
using FluentMigrator.Runner;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class ResearcherProviderMetricsTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task RecalculateMetricsAsync_MetricsOnlyScholarProfile_DoesNotReportZeroDocuments()
    {
        string personelId = "scholar-metrics-only-" + Guid.NewGuid().ToString("N");
        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            database.Researchers.Add(new Researcher
            {
                PersonelId = personelId,
                GoogleScholarProfile = new()
                {
                    CitationCount = 648,
                    CitationCountRecent = 521,
                    HIndex = 9,
                    HIndexRecent = 9,
                    I10Index = 9,
                    I10IndexRecent = 9,
                    MetricsSinceYear = 2021,
                    DocumentsCount = 0,
                    LastUpdatedAt = DateTime.UtcNow,
                    RawDataJson = GoogleScholarProfile.CreateScrapeSnapshot("<html></html>", false)
                }
            });
            await database.SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            IAcademicPerformanceApplicationService service = scope.ServiceProvider
                .GetRequiredService<IAcademicPerformanceApplicationService>();
            await service.RecalculateMetricsAsync(new() { PersonelId = personelId });
        }

        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            Researcher saved = await database.Researchers.AsNoTracking()
                .SingleAsync(value => value.PersonelId == personelId);
            Assert.Equal(648, saved.ScholarCitationCount);
            Assert.Equal(521, saved.ScholarCitationCountRecent);
            Assert.Equal(9, saved.ScholarHIndex);
            Assert.Equal(9, saved.ScholarI10Index);
            Assert.Equal(2021, saved.ScholarMetricsSinceYear);
            Assert.Null(saved.ScholarDocumentsCount);
        }
    }

    [Fact]
    public async Task RecalculateMetricsAsync_SavedProfilesAndWorks_UpdatesMetricsIdempotently()
    {
        string personelId = "metrics-" + Guid.NewGuid().ToString("N");
        DateTime initialTime = new(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);
        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            database.Researchers.Add(new Researcher
            {
                PersonelId = personelId,
                OpenAlexProfile = new OpenAlexProfile
                {
                    OpenAlexAuthorId = "A" + Guid.NewGuid().ToString("N"),
                    CitedByCount = 0,
                    WorksCount = 0,
                    HIndex = null,
                    I10Index = 0,
                    LastUpdatedAt = initialTime
                },
                GoogleScholarProfile = new GoogleScholarProfile
                {
                    CitationCount = 25,
                    HIndex = 4,
                    I10Index = 2,
                    DocumentsCount = 8,
                    LastUpdatedAt = initialTime
                },
                WebOfScienceProfile = new WebOfScienceProfile
                {
                    TotalTimesCited = 999,
                    HIndex = 99,
                    DocumentsCount = 99,
                    LastUpdatedAt = initialTime,
                    Works =
                    [
                        new() { Uid = "WOS:1", TimesCited = 10 },
                        new() { Uid = "WOS:2", TimesCited = 2 },
                        new() { Uid = "WOS:3", TimesCited = 1 }
                    ]
                },
                ScopusProfile = new ScopusProfile
                {
                    ScopusAuthorId = Random.Shared.NextInt64(10000000000, 99999999999).ToString(),
                    CitationCount = 31,
                    CitedByCount = 700,
                    HIndex = 6,
                    DocumentsCount = 12,
                    LastUpdatedAt = initialTime
                }
            });
            await database.SaveChangesAsync();
        }

        DateTime refreshedTime = initialTime.AddMinutes(17);
        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            OpenAlexProfile profile = await database.OpenAlexProfiles
                .SingleAsync(value => value.PersonelId == personelId);
            profile.CitedByCount = 41;
            profile.HIndex = null;
            profile.I10Index = 0;
            profile.WorksCount = 9;
            profile.LastUpdatedAt = refreshedTime;
            await database.SaveChangesAsync();

            Researcher before = await database.Researchers.AsNoTracking()
                .SingleAsync(value => value.PersonelId == personelId);
            Assert.Null(before.OpenAlexCitationCount);
        }

        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            await database.Researchers.Include(value => value.OpenAlexProfile)
                .SingleAsync(value => value.PersonelId == personelId);
            await using (AsyncServiceScope updateScope = fixture.Services.CreateAsyncScope())
            {
                AcademicDbContext updateDatabase = updateScope.ServiceProvider.GetRequiredService<AcademicDbContext>();
                OpenAlexProfile latest = await updateDatabase.OpenAlexProfiles
                    .SingleAsync(value => value.PersonelId == personelId);
                latest.CitedByCount = 42;
                await updateDatabase.SaveChangesAsync();
            }
            IAcademicPerformanceApplicationService service = scope.ServiceProvider
                .GetRequiredService<IAcademicPerformanceApplicationService>();
            ResearcherMetricsResponse first = await service.RecalculateMetricsAsync(
                new() { PersonelId = personelId });
            int eventsAfterFirst = await database.CollectionChanges.CountAsync(value =>
                value.PersonelId == personelId && value.ChangeKind == "ResearcherCollected");
            ResearcherMetricsResponse second = await service.RecalculateMetricsAsync(
                new() { PersonelId = personelId });
            int eventsAfterSecond = await database.CollectionChanges.CountAsync(value =>
                value.PersonelId == personelId && value.ChangeKind == "ResearcherCollected");
            Assert.Equal(personelId, first.PersonelId);
            Assert.True(second.RecalculatedAt >= first.RecalculatedAt);
            Assert.True(eventsAfterFirst > 0);
            Assert.Equal(eventsAfterFirst, eventsAfterSecond);
        }

        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            Researcher saved = await database.Researchers.AsNoTracking()
                .SingleAsync(value => value.PersonelId == personelId);
            Assert.Equal(42, saved.OpenAlexCitationCount);
            Assert.Null(saved.OpenAlexHIndex);
            Assert.Equal(0, saved.OpenAlexI10Index);
            Assert.Equal(9, saved.OpenAlexDocumentsCount);
            Assert.Equal(refreshedTime, saved.OpenAlexMetricsUpdatedAt);
            Assert.Equal(25, saved.ScholarCitationCount);
            Assert.Equal(13, saved.WosCitationCount);
            Assert.Equal(2, saved.WosHIndex);
            Assert.Equal(3, saved.WosDocumentsCount);
            Assert.Equal(initialTime, saved.WosMetricsUpdatedAt);
            Assert.Equal(31, saved.ScopusCitationCount);
            Assert.Equal(6, saved.ScopusHIndex);
        }
    }

    [Fact]
    public async Task RecalculateMetricsAsync_MissingCitationAndMissingResearcher_ReturnsNullAndRejects()
    {
        string personelId = "metrics-null-" + Guid.NewGuid().ToString("N");
        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            database.Researchers.Add(new Researcher
            {
                PersonelId = personelId,
                WosCitationCount = 88,
                WosHIndex = 8,
                WebOfScienceProfile = new WebOfScienceProfile
                {
                    LastUpdatedAt = DateTime.UtcNow.AddDays(-2),
                    Works = [new() { Uid = "WOS:null", TimesCited = null }]
                }
            });
            await database.SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            IAcademicPerformanceApplicationService service = scope.ServiceProvider
                .GetRequiredService<IAcademicPerformanceApplicationService>();
            await service.RecalculateMetricsAsync(new() { PersonelId = personelId });
            await Assert.ThrowsAsync<ArgumentException>(() => service.RecalculateMetricsAsync(
                new() { PersonelId = "missing-" + Guid.NewGuid().ToString("N") }));
            await Assert.ThrowsAsync<ArgumentException>(() => service.RecalculateMetricsAsync(new()));
        }

        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            Researcher saved = await database.Researchers.AsNoTracking()
                .SingleAsync(value => value.PersonelId == personelId);
            Assert.Null(saved.WosCitationCount);
            Assert.Null(saved.WosHIndex);
        }
    }

    [Fact]
    public async Task SaveChanges_ProfileNotLoaded_PreservesMaterializedMetrics()
    {
        string personelId = "preserve-" + Guid.NewGuid().ToString("N");
        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            database.Researchers.Add(new Researcher
            {
                PersonelId = personelId,
                FirstName = "Before",
                ScholarCitationCount = 73
            });
            await database.SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            Researcher researcher = await database.Researchers
                .SingleAsync(value => value.PersonelId == personelId);
            Assert.False(database.Entry(researcher).Reference(value => value.GoogleScholarProfile).IsLoaded);
            researcher.FirstName = "After";
            await database.SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            Researcher saved = await database.Researchers.AsNoTracking()
                .SingleAsync(value => value.PersonelId == personelId);
            Assert.Equal(73, saved.ScholarCitationCount);
        }
    }

    [Fact]
    public async Task RecalculateMetricsAsync_QueriesOnlyMetricColumns_AndPreservesRawPayloads()
    {
        string personelId = "metrics-query-" + Guid.NewGuid().ToString("N");
        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            database.Researchers.Add(new Researcher
            {
                PersonelId = personelId,
                OpenAlexProfile = new OpenAlexProfile
                {
                    OpenAlexAuthorId = "A-query",
                    RawDataJson = "openalex-raw",
                    LastUpdatedAt = DateTime.UtcNow
                },
                WebOfScienceProfile = new WebOfScienceProfile
                {
                    RawDataJson = "wos-profile-raw",
                    DocumentPagesJson = "wos-pages-raw",
                    LastUpdatedAt = DateTime.UtcNow,
                    Works = [new() { Uid = "WOS:query", TimesCited = 7, RawDataJson = "wos-work-raw" }]
                }
            });
            await database.SaveChangesAsync();
        }

        CommandCaptureInterceptor capture = new();
        DbContextOptions<AcademicDbContext> options = new DbContextOptionsBuilder<AcademicDbContext>()
            .UseSqlServer(fixture.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using (AcademicDbContext database = new(options))
        {
            ResearcherMetricsService service = new(database, new CanonicalWorkSynchronizer(database));
            await service.RecalculateAsync(personelId);
        }

        string selectSql = string.Join('\n', capture.Commands.Where(command =>
            command.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains("WebOfScienceWorks", selectSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TimesCited", selectSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[g].[RawDataJson]\r\nFROM", selectSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DocumentPagesJson", selectSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WorksPagesJson", selectSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SearchPagesJson", selectSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CitationsJson", selectSql, StringComparison.OrdinalIgnoreCase);

        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            WebOfScienceProfile profile = await database.WebOfScienceProfiles.AsNoTracking()
                .SingleAsync(value => value.PersonelId == personelId);
            WebOfScienceWork work = await database.WebOfScienceWorks.AsNoTracking()
                .SingleAsync(value => value.WebOfScienceProfileId == profile.Id);
            Assert.Equal("wos-profile-raw", profile.RawDataJson);
            Assert.Equal("wos-pages-raw", profile.DocumentPagesJson);
            Assert.Equal("wos-work-raw", work.RawDataJson);
            Assert.Equal(7, profile.TotalTimesCited);
        }
    }

    private sealed class CommandCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
