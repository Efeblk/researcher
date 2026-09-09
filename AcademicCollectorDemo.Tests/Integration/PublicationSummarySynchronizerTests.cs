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

        await new PublicationSummarySynchronizer(db).SyncAsync(researcher.PersonelId);

        var dois = await db.PublicationSummaries.Where(x => x.PersonelId == researcher.PersonelId)
            .Select(x => x.Doi).ToListAsync();
        Assert.Contains("10.1234/a", dois);
        Assert.Contains("10.1234/b", dois);
    }

    [Fact]
    public async Task SyncAsync_DoiConnectsDifferentTitles_DoesNotCreateDuplicateFingerprints()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        var researcher = new Researcher { PersonelId = "test-" + Guid.NewGuid().ToString("N") };
        db.Researchers.Add(researcher);
        await db.SaveChangesAsync();
        db.AcademicWorks.AddRange(Work(researcher.PersonelId, "First title", null),
            Work(researcher.PersonelId, "Other title", "10.1234/shared"),
            Work(researcher.PersonelId, "First title", "10.1234/shared"));
        await db.SaveChangesAsync();

        Assert.Equal(1, await new PublicationSummarySynchronizer(db).SyncAsync(researcher.PersonelId));
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
        Assert.Equal(2, await synchronizer.SyncAsync(researcher.PersonelId));
        Assert.Equal(2, await synchronizer.SyncAsync(researcher.PersonelId));
    }

    [Fact]
    public async Task SyncAsync_DoiAdded_PreservesExistingSelection()
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
        await synchronizer.SyncAsync(researcher.PersonelId);
        var summary = await db.PublicationSummaries.SingleAsync(x => x.PersonelId == researcher.PersonelId);
        db.PublicationDisplayApprovals.Add(new() { PersonelId = researcher.PersonelId, PublicationSummaryId = summary.Id, ApprovedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        work.Doi = "https://dx.doi.org/10.1234/NEW";
        await db.SaveChangesAsync();
        await synchronizer.SyncAsync(researcher.PersonelId);
        Assert.Equal("10.1234/new", summary.Doi);
        Assert.True(await db.PublicationDisplayApprovals.AnyAsync(x => x.PublicationSummaryId == summary.Id));
    }

    private static AcademicWork Work(string researcherId, string? title, string? doi) => new()
    {
        PersonelId = researcherId, Title = title, Doi = doi, PublicationYear = 2025,
        Provider = AcademicWorkProvider.Orcid, ProviderWorkId = Guid.NewGuid().ToString("N"), SyncedAt = DateTime.UtcNow
    };

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
        await sync.SyncAsync(researcher.PersonelId);
        var selected = await db.PublicationSummaries.SingleAsync(x => x.PersonelId == researcher.PersonelId);
        db.PublicationDisplayApprovals.Add(new() { PersonelId = researcher.PersonelId, PublicationSummaryId = selected.Id, ApprovedAt = DateTime.UtcNow });
        db.AcademicWorks.Remove(original);
        db.AcademicWorks.AddRange(Work(researcher.PersonelId, "Ambiguous title", "10.1234/first"), Work(researcher.PersonelId, "Ambiguous title", "10.1234/second"));
        await db.SaveChangesAsync();

        Assert.Equal(2, await sync.SyncAsync(researcher.PersonelId));
        Assert.False(await db.PublicationDisplayApprovals.AnyAsync(x => x.PersonelId == researcher.PersonelId));
    }

    [Fact]
    public async Task SyncAsync_MergedGroupsChangePreferredTitle_PreservesSelectedSummary()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        var researcher = new Researcher { PersonelId = "test-" + Guid.NewGuid().ToString("N") };
        db.Researchers.Add(researcher);
        await db.SaveChangesAsync();
        db.AcademicWorks.AddRange(Work(researcher.PersonelId, "Original title", null),
            Work(researcher.PersonelId, "Preferred title", "10.1234/merge"));
        await db.SaveChangesAsync();
        var synchronizer = new PublicationSummarySynchronizer(db);
        await synchronizer.SyncAsync(researcher.PersonelId);
        var selected = await db.PublicationSummaries.SingleAsync(x => x.PersonelId == researcher.PersonelId && x.Doi == null);
        db.PublicationDisplayApprovals.Add(new() { PersonelId = researcher.PersonelId, PublicationSummaryId = selected.Id, ApprovedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        db.AcademicWorks.Add(Work(researcher.PersonelId, "Original title", "10.1234/merge"));
        await db.SaveChangesAsync();

        Assert.Equal(1, await synchronizer.SyncAsync(researcher.PersonelId));
        db.ChangeTracker.Clear();
        Assert.True(await db.PublicationDisplayApprovals.AnyAsync(x => x.PublicationSummaryId == selected.Id));
        Assert.Equal("10.1234/merge", (await db.PublicationSummaries.SingleAsync(x => x.PersonelId == researcher.PersonelId)).Doi);
    }
}
