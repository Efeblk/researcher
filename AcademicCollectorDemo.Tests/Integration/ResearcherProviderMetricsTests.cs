using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.GoogleScholar;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Scopus;
using AcademicCollectorDemo.Modules.AcademicPerformance.Application;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Tests.Infrastructure;
using FluentMigrator.Runner;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class ResearcherProviderMetricsTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Migration_ExistingProfiles_BackfillsResearcherColumns()
    {
        string personelId = "backfill-" + Guid.NewGuid().ToString("N");
        DateTime updatedAt = new(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            database.Researchers.Add(new Researcher
            {
                PersonelId = personelId,
                OpenAlexProfile = new OpenAlexProfile
                {
                    OpenAlexAuthorId = "A" + Guid.NewGuid().ToString("N"),
                    CitedByCount = 101,
                    HIndex = 11,
                    I10Index = 7,
                    WorksCount = 19,
                    TwoYearMeanCitedness = 3.2579m,
                    LastUpdatedAt = updatedAt
                }
            });
            await database.SaveChangesAsync();
        }

        using (IServiceScope scope = fixture.Services.CreateScope())
        {
            IMigrationRunner runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
            runner.MigrateDown(202609090002);
            runner.MigrateUp();
        }

        await using SqlConnection connection = new(fixture.ConnectionString);
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT OpenAlexCitationCount, OpenAlexHIndex, OpenAlexI10Index,
                   OpenAlexDocumentsCount, OpenAlexTwoYearMeanCitedness,
                   OpenAlexMetricsUpdatedAt
            FROM [core].[Researchers] WHERE PersonelID = @personelId;
            """;
        command.Parameters.AddWithValue("@personelId", personelId);
        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(101, reader.GetInt32(0));
        Assert.Equal(11, reader.GetInt32(1));
        Assert.Equal(7, reader.GetInt32(2));
        Assert.Equal(19, reader.GetInt32(3));
        Assert.Equal(3.2579m, reader.GetDecimal(4));
        Assert.Equal(updatedAt, reader.GetDateTime(5));
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
}
