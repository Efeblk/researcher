using AcademicCollectorDemo.Tests.Infrastructure;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class PublicationSummarySynchronizerTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task SyncAsync_SharedDoiAcrossResearchers_CreatesOneCanonicalAndTwoSummaries()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        string firstId = "shared-first-" + Guid.NewGuid().ToString("N");
        string secondId = "shared-second-" + Guid.NewGuid().ToString("N");
        db.Researchers.AddRange(new() { PersonelId = firstId }, new() { PersonelId = secondId });
        db.AcademicWorks.AddRange(
            Work(firstId, "Shared", "10.1234/shared-researchers"),
            Work(secondId, "Shared", "10.1234/shared-researchers"));
        await db.SaveChangesAsync();

        await SyncAsync(db, firstId);
        await SyncAsync(db, secondId);

        int canonicalId = await db.CanonicalWorks.Where(value =>
            value.NormalizedDoi == "10.1234/shared-researchers").Select(value => value.Id).SingleAsync();
        Assert.Equal(2, await db.PublicationSummaries.CountAsync(value => value.CanonicalWorkId == canonicalId));
        Assert.Equal(2, await db.PublicationSummaries.Where(value => value.CanonicalWorkId == canonicalId)
            .Select(value => value.PersonelId).Distinct().CountAsync());
    }

    [Fact]
    public async Task SyncAsync_MissingDoiBridgesConflictingDois_KeepsDistinctPublications()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        var researcher = new Researcher { PersonelId = "test-" + Guid.NewGuid().ToString("N") };
        db.Researchers.Add(researcher);
        await db.SaveChangesAsync();
        db.AcademicWorks.AddRange(
            Work(researcher.PersonelId, "Shared title", "10.1234/a"),
            Work(researcher.PersonelId, "Shared title", null),
            Work(researcher.PersonelId, "Shared title", "10.1234/b"));
        await db.SaveChangesAsync();

        await SyncAsync(db, researcher.PersonelId);

        var dois = await db.PublicationSummaries.Where(x => x.PersonelId == researcher.PersonelId)
            .Select(x => x.Doi).ToListAsync();
        Assert.Contains("10.1234/a", dois);
        Assert.Contains("10.1234/b", dois);
    }

    [Fact]
    public async Task SyncAsync_TitleYearFallbackConnectsDoiLessObservation_ToSharedDoi()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        var researcher = new Researcher { PersonelId = "test-" + Guid.NewGuid().ToString("N") };
        db.Researchers.Add(researcher);
        await db.SaveChangesAsync();
        db.AcademicWorks.AddRange(Work(researcher.PersonelId, "First title", null),
            Work(researcher.PersonelId, "Other title", "10.1234/summary-shared"),
            Work(researcher.PersonelId, "First title", "10.1234/summary-shared"));
        await db.SaveChangesAsync();

        Assert.Equal(1, await SyncAsync(db, researcher.PersonelId));
    }

    [Fact]
    public async Task SyncAsync_MultipleLongVenues_PersistsAllDistinctValuesInOneSummary()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        var researcher = new Researcher { PersonelId = "test-" + Guid.NewGuid().ToString("N") };
        string firstVenue = "Conference, Proceedings " + new string('A', 1100);
        string secondVenue = "Journal, Supplement " + new string('B', 1100);
        AcademicWork first = Work(researcher.PersonelId, "Shared venue work", "10.1234/all-venues");
        AcademicWork duplicate = Work(researcher.PersonelId, "Shared venue work", "10.1234/all-venues");
        AcademicWork second = Work(researcher.PersonelId, "Shared venue work", "10.1234/all-venues");
        first.Publication = firstVenue;
        duplicate.Publication = firstVenue;
        second.Publication = secondVenue;
        db.Researchers.Add(researcher);
        db.AcademicWorks.AddRange(first, duplicate, second);
        await db.SaveChangesAsync();

        Assert.Equal(1, await SyncAsync(db, researcher.PersonelId));

        db.ChangeTracker.Clear();
        PublicationSummary summary = await db.PublicationSummaries.SingleAsync(
            value => value.PersonelId == researcher.PersonelId);
        Assert.Equal(string.Join(", ", firstVenue, secondVenue), summary.Publication);
        Assert.NotNull(summary.Publication);
        Assert.True(summary.Publication.Length > 2000);
    }

    [Fact]
    public async Task SyncAsync_NoTitleOrDoi_DoesNotCollapseUnknownPublications()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        var researcher = new Researcher { PersonelId = "test-" + Guid.NewGuid().ToString("N") };
        db.Researchers.Add(researcher);
        await db.SaveChangesAsync();
        db.AcademicWorks.AddRange(Work(researcher.PersonelId, null, null), Work(researcher.PersonelId, null, null));
        await db.SaveChangesAsync();
        var synchronizer = new PublicationSummarySynchronizer(db);
        await new CanonicalWorkSynchronizer(db).SyncAsync(researcher.PersonelId);
        Assert.Equal(2, await synchronizer.SyncAsync(researcher.PersonelId));
        await new CanonicalWorkSynchronizer(db).SyncAsync(researcher.PersonelId);
        Assert.Equal(2, await synchronizer.SyncAsync(researcher.PersonelId));
    }

    [Fact]
    public async Task SyncAsync_DoiAdded_CreatesSummaryForNewCanonicalIdentity()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        var researcher = new Researcher { PersonelId = "test-" + Guid.NewGuid().ToString("N") };
        db.Researchers.Add(researcher);
        await db.SaveChangesAsync();
        var work = Work(researcher.PersonelId, "Publication", null);
        db.AcademicWorks.Add(work);
        await db.SaveChangesAsync();
        var synchronizer = new PublicationSummarySynchronizer(db);
        await new CanonicalWorkSynchronizer(db).SyncAsync(researcher.PersonelId);
        await synchronizer.SyncAsync(researcher.PersonelId);
        var summary = await db.PublicationSummaries.SingleAsync(x => x.PersonelId == researcher.PersonelId);
        db.PublicationDisplayApprovals.Add(new() { PersonelId = researcher.PersonelId, PublicationSummaryId = summary.Id, ApprovedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        work.Doi = "https://dx.doi.org/10.1234/NEW";
        await db.SaveChangesAsync();
        await new CanonicalWorkSynchronizer(db).SyncAsync(researcher.PersonelId);
        await synchronizer.SyncAsync(researcher.PersonelId);
        db.ChangeTracker.Clear();
        PublicationSummary replacement = await db.PublicationSummaries.SingleAsync(
            x => x.PersonelId == researcher.PersonelId);
        Assert.Equal("10.1234/new", replacement.Doi);
        Assert.NotEqual(summary.Id, replacement.Id);
        Assert.False(await db.PublicationDisplayApprovals.AnyAsync(
            x => x.PersonelId == researcher.PersonelId));
    }

    [Fact]
    public async Task SyncAsync_ChainedDoiWrappers_PreservesSummaryIdentityAndSelection()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        var researcher = new Researcher { PersonelId = "test-" + Guid.NewGuid().ToString("N") };
        db.Researchers.Add(researcher);
        var work = Work(researcher.PersonelId, "Wrapped DOI", "doi: https://doi.org/10.1234/ABC");
        db.AcademicWorks.Add(work);
        await db.SaveChangesAsync();
        var synchronizer = new PublicationSummarySynchronizer(db);
        await new CanonicalWorkSynchronizer(db).SyncAsync(researcher.PersonelId);
        await synchronizer.SyncAsync(researcher.PersonelId);
        var summary = await db.PublicationSummaries.SingleAsync(x => x.PersonelId == researcher.PersonelId);
        int originalId = summary.Id;
        db.PublicationDisplayApprovals.Add(new()
        {
            PersonelId = researcher.PersonelId,
            PublicationSummaryId = summary.Id,
            ApprovedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        work.Doi = "https://doi.org/doi:10.1234/abc";
        await db.SaveChangesAsync();
        await new CanonicalWorkSynchronizer(db).SyncAsync(researcher.PersonelId);
        await synchronizer.SyncAsync(researcher.PersonelId);

        db.ChangeTracker.Clear();
        summary = await db.PublicationSummaries.SingleAsync(x => x.PersonelId == researcher.PersonelId);
        Assert.Equal(originalId, summary.Id);
        Assert.Equal("10.1234/abc", summary.Doi);
        Assert.True(await db.PublicationDisplayApprovals.AnyAsync(x => x.PublicationSummaryId == originalId));
    }

    private static AcademicWork Work(string researcherId, string? title, string? doi) => new()
    {
        PersonelId = researcherId, Title = title, Doi = doi, PublicationYear = 2025,
        Provider = AcademicWorkProvider.Orcid, ProviderWorkId = Guid.NewGuid().ToString("N"), SyncedAt = DateTime.UtcNow
    };

    private static async Task<int> SyncAsync(AcademicDbContext db, string personelId)
    {
        await new CanonicalWorkSynchronizer(db).SyncAsync(personelId);
        return await new PublicationSummarySynchronizer(db).SyncAsync(personelId);
    }

    [Fact]
    public async Task SyncAsync_SelectedTitleMatchesTwoDois_DoesNotTransferApproval()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        var researcher = new Researcher { PersonelId = "test-" + Guid.NewGuid().ToString("N") };
        db.Researchers.Add(researcher);
        await db.SaveChangesAsync();
        var original = Work(researcher.PersonelId, "Ambiguous title", null);
        db.AcademicWorks.Add(original);
        await db.SaveChangesAsync();
        var sync = new PublicationSummarySynchronizer(db);
        await new CanonicalWorkSynchronizer(db).SyncAsync(researcher.PersonelId);
        await sync.SyncAsync(researcher.PersonelId);
        var selected = await db.PublicationSummaries.SingleAsync(x => x.PersonelId == researcher.PersonelId);
        db.PublicationDisplayApprovals.Add(new() { PersonelId = researcher.PersonelId, PublicationSummaryId = selected.Id, ApprovedAt = DateTime.UtcNow });
        db.AcademicWorks.Remove(original);
        db.AcademicWorks.AddRange(Work(researcher.PersonelId, "Ambiguous title", "10.1234/first"), Work(researcher.PersonelId, "Ambiguous title", "10.1234/second"));
        await db.SaveChangesAsync();

        await new CanonicalWorkSynchronizer(db).SyncAsync(researcher.PersonelId);
        Assert.Equal(2, await sync.SyncAsync(researcher.PersonelId));
        Assert.False(await db.PublicationDisplayApprovals.AnyAsync(x => x.PersonelId == researcher.PersonelId));
    }

    [Fact]
    public async Task SyncAsync_MergedCanonicalIdentity_DoesNotTransferSelection()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        var researcher = new Researcher { PersonelId = "test-" + Guid.NewGuid().ToString("N") };
        db.Researchers.Add(researcher);
        await db.SaveChangesAsync();
        AcademicWork original = Work(researcher.PersonelId, "Original title", null);
        original.Provider = AcademicWorkProvider.GoogleScholar;
        AcademicWork preferred = Work(researcher.PersonelId, "Preferred title", "10.1234/merge");
        db.AcademicWorks.AddRange(original, preferred);
        await db.SaveChangesAsync();
        var synchronizer = new PublicationSummarySynchronizer(db);
        await new CanonicalWorkSynchronizer(db).SyncAsync(researcher.PersonelId);
        await synchronizer.SyncAsync(researcher.PersonelId);
        var selected = await db.PublicationSummaries.SingleAsync(x => x.PersonelId == researcher.PersonelId && x.Doi == null);
        db.PublicationDisplayApprovals.Add(new() { PersonelId = researcher.PersonelId, PublicationSummaryId = selected.Id, ApprovedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        original.Doi = "10.1234/merge";
        await db.SaveChangesAsync();

        await new CanonicalWorkSynchronizer(db).SyncAsync(researcher.PersonelId);
        Assert.Equal(1, await synchronizer.SyncAsync(researcher.PersonelId));
        db.ChangeTracker.Clear();
        Assert.False(await db.PublicationDisplayApprovals.AnyAsync(x => x.PersonelId == researcher.PersonelId));
        PublicationSummary replacement = await db.PublicationSummaries.SingleAsync(
            x => x.PersonelId == researcher.PersonelId);
        Assert.NotEqual(selected.Id, replacement.Id);
        Assert.Equal("10.1234/merge", replacement.Doi);
    }
}
