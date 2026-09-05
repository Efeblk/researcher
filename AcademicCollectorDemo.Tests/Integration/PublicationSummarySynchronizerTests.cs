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
        var researcher = new Researcher();
        db.Researchers.Add(researcher);
        await db.SaveChangesAsync();
        db.AcademicWorks.AddRange(
            Work(researcher.Id, "Shared title", "10.1234/a"),
            Work(researcher.Id, "Shared title", null),
            Work(researcher.Id, "Shared title", "10.1234/b"));
        await db.SaveChangesAsync();

        await new PublicationSummarySynchronizer(db).SyncAsync(researcher.Id);

        var dois = await db.PublicationSummaries.Where(x => x.ResearcherId == researcher.Id)
            .Select(x => x.Doi).ToListAsync();
        Assert.Contains("10.1234/a", dois);
        Assert.Contains("10.1234/b", dois);
    }

    [Fact]
    public async Task SyncAsync_DoiConnectsDifferentTitles_DoesNotCreateDuplicateFingerprints()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        var researcher = new Researcher();
        db.Researchers.Add(researcher);
        await db.SaveChangesAsync();
        db.AcademicWorks.AddRange(Work(researcher.Id, "First title", null),
            Work(researcher.Id, "Other title", "10.1234/shared"),
            Work(researcher.Id, "First title", "10.1234/shared"));
        await db.SaveChangesAsync();

        Assert.Equal(1, await new PublicationSummarySynchronizer(db).SyncAsync(researcher.Id));
    }

    [Fact]
    public async Task SyncAsync_NoTitleOrDoi_DoesNotCollapseUnknownPublications()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        var researcher = new Researcher();
        db.Researchers.Add(researcher);
        await db.SaveChangesAsync();
        db.AcademicWorks.AddRange(Work(researcher.Id, null, null), Work(researcher.Id, null, null));
        await db.SaveChangesAsync();
        var synchronizer = new PublicationSummarySynchronizer(db);
        Assert.Equal(2, await synchronizer.SyncAsync(researcher.Id));
        Assert.Equal(2, await synchronizer.SyncAsync(researcher.Id));
    }

    [Fact]
    public async Task SyncAsync_DoiAdded_PreservesExistingSelection()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        var researcher = new Researcher();
        db.Researchers.Add(researcher);
        await db.SaveChangesAsync();
        var work = Work(researcher.Id, "Publication", null);
        db.AcademicWorks.Add(work);
        await db.SaveChangesAsync();
        var synchronizer = new PublicationSummarySynchronizer(db);
        await synchronizer.SyncAsync(researcher.Id);
        var summary = await db.PublicationSummaries.SingleAsync(x => x.ResearcherId == researcher.Id);
        db.PublicationDisplayApprovals.Add(new() { ResearcherId = researcher.Id, PublicationSummaryId = summary.Id, ApprovedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        work.Doi = "https://dx.doi.org/10.1234/NEW";
        await db.SaveChangesAsync();
        await synchronizer.SyncAsync(researcher.Id);
        Assert.Equal("10.1234/new", summary.Doi);
        Assert.True(await db.PublicationDisplayApprovals.AnyAsync(x => x.PublicationSummaryId == summary.Id));
    }

    private static AcademicWork Work(int researcherId, string? title, string? doi) => new()
    {
        ResearcherId = researcherId, Title = title, Doi = doi, PublicationYear = 2025,
        Provider = AcademicWorkProvider.Orcid, ProviderWorkId = Guid.NewGuid().ToString("N"), SyncedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task SyncAsync_SelectedTitleMatchesTwoDois_DoesNotTransferApproval()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        var researcher = new Researcher();
        db.Researchers.Add(researcher);
        await db.SaveChangesAsync();
        var original = Work(researcher.Id, "Ambiguous title", null);
        db.AcademicWorks.Add(original);
        await db.SaveChangesAsync();
        var sync = new PublicationSummarySynchronizer(db);
        await sync.SyncAsync(researcher.Id);
        var selected = await db.PublicationSummaries.SingleAsync(x => x.ResearcherId == researcher.Id);
        db.PublicationDisplayApprovals.Add(new() { ResearcherId = researcher.Id, PublicationSummaryId = selected.Id, ApprovedAt = DateTime.UtcNow });
        db.AcademicWorks.Remove(original);
        db.AcademicWorks.AddRange(Work(researcher.Id, "Ambiguous title", "10.1234/first"), Work(researcher.Id, "Ambiguous title", "10.1234/second"));
        await db.SaveChangesAsync();

        Assert.Equal(2, await sync.SyncAsync(researcher.Id));
        Assert.False(await db.PublicationDisplayApprovals.AnyAsync(x => x.ResearcherId == researcher.Id));
    }

    [Fact]
    public async Task SyncAsync_MergedGroupsChangePreferredTitle_PreservesSelectedSummary()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        var researcher = new Researcher();
        db.Researchers.Add(researcher);
        await db.SaveChangesAsync();
        db.AcademicWorks.AddRange(Work(researcher.Id, "Original title", null),
            Work(researcher.Id, "Preferred title", "10.1234/merge"));
        await db.SaveChangesAsync();
        var synchronizer = new PublicationSummarySynchronizer(db);
        await synchronizer.SyncAsync(researcher.Id);
        var selected = await db.PublicationSummaries.SingleAsync(x => x.ResearcherId == researcher.Id && x.Doi == null);
        db.PublicationDisplayApprovals.Add(new() { ResearcherId = researcher.Id, PublicationSummaryId = selected.Id, ApprovedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        db.AcademicWorks.Add(Work(researcher.Id, "Original title", "10.1234/merge"));
        await db.SaveChangesAsync();

        Assert.Equal(1, await synchronizer.SyncAsync(researcher.Id));
        db.ChangeTracker.Clear();
        Assert.True(await db.PublicationDisplayApprovals.AnyAsync(x => x.PublicationSummaryId == selected.Id));
        Assert.Equal("10.1234/merge", (await db.PublicationSummaries.SingleAsync(x => x.ResearcherId == researcher.Id)).Doi);
    }
}
