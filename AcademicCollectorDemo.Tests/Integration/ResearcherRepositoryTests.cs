using System.Diagnostics;
using System.Data.Common;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.GoogleScholar;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Orcid;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Scopus;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.TrDizin;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Persistence;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using AcademicCollectorDemo.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit.Abstractions;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class ResearcherRepositoryTests(
    SqlServerFixture fixture,
    ITestOutputHelper output)
{
    [Fact]
    public async Task FindByPersonelIdAsync_BoundedCompleteGraph_LoadsCollectionsWithoutProfileJoins()
    {
        CommandCaptureInterceptor interceptor = new();
        DbContextOptions<AcademicDbContext> options = new DbContextOptionsBuilder<AcademicDbContext>()
            .UseSqlServer(fixture.ConnectionString)
            .AddInterceptors(interceptor)
            .Options;
        string personelId = "repository-" + Guid.NewGuid().ToString("N");

        await using (AcademicDbContext seed = new(options))
        {
            seed.Researchers.Add(CreateCompleteResearcher(personelId));
            await seed.SaveChangesAsync();
        }

        interceptor.Commands.Clear();
        await using AcademicDbContext database = new(options);
        Stopwatch stopwatch = Stopwatch.StartNew();
        await LoadLegacySplitGraphAsync(database, personelId);
        stopwatch.Stop();
        TimeSpan legacyElapsed = stopwatch.Elapsed;
        int legacySqlCharacters = interceptor.Commands.Sum(command => command.Length);
        Assert.Equal(9, interceptor.Commands.Count);
        Assert.All(interceptor.Commands.Skip(1), command =>
            Assert.Contains("[core].[Researchers]", command));
        Assert.Contains("RawPublicationsJson", interceptor.Commands[0]);
        Assert.All(interceptor.Commands.Skip(1), command =>
            Assert.DoesNotContain("RawPublicationsJson", command));

        database.ChangeTracker.Clear();
        interceptor.Commands.Clear();
        stopwatch.Restart();
        Researcher loaded = await new ResearcherRepository(database)
            .FindByPersonelIdAsync(personelId) ?? throw new InvalidOperationException();
        stopwatch.Stop();
        output.WriteLine(
            "Bounded graph load: legacy {0:F1} ms/{1} SQL chars; direct collections {2:F1} ms/{3} SQL chars.",
            legacyElapsed.TotalMilliseconds,
            legacySqlCharacters,
            stopwatch.Elapsed.TotalMilliseconds,
            interceptor.Commands.Sum(command => command.Length));

        Assert.Equal(2, loaded.OrcidProfile!.Works!.Count);
        Assert.Equal(2, loaded.GoogleScholarProfile!.Works!.Count);
        Assert.Equal(2, loaded.OpenAlexProfile!.Works!.Count);
        Assert.Equal(2, loaded.ScopusProfile!.Works!.Count);
        Assert.Equal(2, loaded.WebOfScienceProfile!.Works!.Count);
        Assert.Equal(2, loaded.WebOfScienceProfile.PeerReviews!.Count);
        Assert.Equal(2, loaded.TrDizinProfile!.Works!.Count);
        Assert.Equal(2, loaded.AcademicWorks!.Count);
        Assert.Equal(32 * 1024, loaded.TrDizinProfile.RawPublicationsJson.Length);
        Assert.All(database.ChangeTracker.Entries(), entry =>
            Assert.Equal(EntityState.Unchanged, entry.State));
        Assert.True(database.Entry(loaded.OrcidProfile).Collection(profile => profile.Works!).IsLoaded);
        Assert.True(database.Entry(loaded.WebOfScienceProfile)
            .Collection(profile => profile.PeerReviews!).IsLoaded);
        Assert.True(database.Entry(loaded).Collection(researcher => researcher.AcademicWorks!).IsLoaded);
        Assert.Equal(9, interceptor.Commands.Count);

        string rootCommand = interceptor.Commands[0];
        Assert.Contains("LEFT JOIN [orcid].[OrcidProfiles]", rootCommand);
        Assert.Contains("[t].[RawPublicationsJson]", rootCommand);
        Assert.All(interceptor.Commands.Skip(1), command =>
        {
            Assert.DoesNotContain("[core].[Researchers]", command);
            Assert.DoesNotContain(" JOIN ", command, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("RawPublicationsJson", command);
        });
    }

    [Fact]
    public async Task FindByPersonelIdAsync_MissingProfiles_LoadsEmptyAcademicWorks()
    {
        DbContextOptions<AcademicDbContext> options = new DbContextOptionsBuilder<AcademicDbContext>()
            .UseSqlServer(fixture.ConnectionString)
            .Options;
        string personelId = "repository-empty-" + Guid.NewGuid().ToString("N");
        await using (AcademicDbContext seed = new(options))
        {
            seed.Researchers.Add(new() { PersonelId = personelId });
            await seed.SaveChangesAsync();
        }

        await using AcademicDbContext database = new(options);
        Researcher loaded = await new ResearcherRepository(database)
            .FindByPersonelIdAsync(personelId) ?? throw new InvalidOperationException();

        Assert.Null(loaded.OrcidProfile);
        Assert.Null(loaded.TrDizinProfile);
        Assert.NotNull(loaded.AcademicWorks);
        Assert.Empty(loaded.AcademicWorks);
        Assert.True(database.Entry(loaded).Collection(researcher => researcher.AcademicWorks!).IsLoaded);
    }

    private static async Task LoadLegacySplitGraphAsync(
        AcademicDbContext database,
        string personelId)
    {
        await database.Researchers
            .Include(researcher => researcher.OrcidProfile)
                .ThenInclude(profile => profile!.Works)
            .Include(researcher => researcher.GoogleScholarProfile)
                .ThenInclude(profile => profile!.Works)
            .Include(researcher => researcher.OpenAlexProfile)
                .ThenInclude(profile => profile!.Works)
            .Include(researcher => researcher.ScopusProfile)
                .ThenInclude(profile => profile!.Works)
            .Include(researcher => researcher.WebOfScienceProfile)
                .ThenInclude(profile => profile!.Works)
            .Include(researcher => researcher.WebOfScienceProfile)
                .ThenInclude(profile => profile!.PeerReviews)
            .Include(researcher => researcher.TrDizinProfile)
                .ThenInclude(profile => profile!.Works)
            .Include(researcher => researcher.AcademicWorks)
            .AsSplitQuery()
            .FirstAsync(researcher => researcher.PersonelId == personelId);
    }

    private static Researcher CreateCompleteResearcher(string personelId)
    {
        string payload = new('x', 32 * 1024);
        DateTime now = DateTime.UtcNow;
        return new()
        {
            PersonelId = personelId,
            OrcidProfile = new()
            {
                RawDataJson = payload,
                LastUpdatedAt = now,
                Works = [new() { PutCode = 1 }, new() { PutCode = 2 }]
            },
            GoogleScholarProfile = new()
            {
                RawDataJson = payload,
                LastUpdatedAt = now,
                Works = [new() { CitationId = "one" }, new() { CitationId = "two" }]
            },
            OpenAlexProfile = new()
            {
                OpenAlexAuthorId = "A1",
                RawDataJson = payload,
                LastUpdatedAt = now,
                Works = [new() { OpenAlexWorkId = "W1" }, new() { OpenAlexWorkId = "W2" }]
            },
            ScopusProfile = new()
            {
                ScopusAuthorId = "S1",
                RawDataJson = payload,
                LastUpdatedAt = now,
                Works = [new() { ScopusWorkId = "S-W1" }, new() { ScopusWorkId = "S-W2" }]
            },
            WebOfScienceProfile = new()
            {
                RawDataJson = payload,
                LastUpdatedAt = now,
                Works = [new() { Uid = "UT:1" }, new() { Uid = "UT:2" }],
                PeerReviews = [new() { Journal = "One" }, new() { Journal = "Two" }]
            },
            TrDizinProfile = new()
            {
                Orcid = "0000-0000-0000-0000",
                AuthorId = 1,
                RawAuthorJson = payload,
                RawPublicationsJson = payload,
                LastUpdatedAt = now,
                Works = [new() { PublicationId = "T1" }, new() { PublicationId = "T2" }]
            },
            AcademicWorks =
            [
                new() { ProviderWorkId = "A1", SyncedAt = DateTime.UtcNow },
                new() { ProviderWorkId = "A2", SyncedAt = DateTime.UtcNow }
            ]
        };
    }

    private sealed class CommandCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
