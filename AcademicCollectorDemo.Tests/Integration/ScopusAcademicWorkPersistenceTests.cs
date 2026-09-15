using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Scopus;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Persistence;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;
using AcademicCollectorDemo.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class ScopusAcademicWorkPersistenceTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task SyncAsync_ScopusOnly_PersistsProfileMetricsSourceIdentityAndCanonicalDoi()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AcademicDbContext db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        string suffix = Guid.NewGuid().ToString("N");
        Researcher researcher = new()
        {
            PersonelId = "scopus-" + suffix,
            ScopusId = "57200000001",
            ScopusProfile = new()
            {
                ScopusAuthorId = "57200000001",
                DocumentsCount = 1,
                CitationCount = 44,
                HIndex = 8,
                LastUpdatedAt = DateTime.UtcNow,
                RawDataJson = "{}",
                SearchPagesJson = "[]",
                Works = [new()
                {
                    ScopusWorkId = "851" + suffix,
                    Eid = "2-s2.0-851" + suffix,
                    Title = "Scopus-only synthetic work",
                    Doi = "https://doi.org/10.1234/SCOPUS-ONLY",
                    PublicationYear = 2026,
                    WorkType = "Article",
                    SourceName = "Synthetic Journal",
                    RawDataJson = "{}"
                }]
            }
        };
        db.Researchers.Add(researcher);
        await db.SaveChangesAsync();
        new AcademicWorkCategorizer().Categorize(researcher);

        await new AcademicWorkSynchronizer(db).SyncAsync(researcher);
        await new CanonicalWorkSynchronizer(db).SyncAsync(researcher.PersonelId);
        db.ChangeTracker.Clear();

        Researcher stored = await new ResearcherRepository(db).FindByPersonelIdAsync(researcher.PersonelId)
            ?? throw new InvalidOperationException();
        AcademicWork work = await db.AcademicWorks.SingleAsync(item =>
            item.PersonelId == researcher.PersonelId && item.Provider == AcademicWorkProvider.Scopus);
        CanonicalWork canonical = await db.CanonicalWorks.SingleAsync(item =>
            item.NormalizedDoi == "10.1234/scopus-only");
        Assert.Equal(44, stored.ScopusCitationCount);
        Assert.Equal(8, stored.ScopusHIndex);
        Assert.Equal("57200000001", stored.ScopusProfile!.ScopusAuthorId);
        Assert.Equal("2-s2.0-851" + suffix, work.SourceId);
        Assert.Equal(AcademicWorkCategorySource.Scopus, work.CategorySource);
        Assert.Equal(canonical.Id, (await db.CanonicalWorkObservations.SingleAsync(item =>
            item.AcademicWorkId == work.Id)).CanonicalWorkId);
    }

    [Fact]
    public async Task FindByIdentifiersAsync_DuplicateScopusOwnership_RejectsBeforeCollection()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AcademicDbContext db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        string scopusId = "57" + Random.Shared.NextInt64(100000000, 999999999);
        db.Researchers.AddRange(
            new Researcher { PersonelId = "scopus-owner-" + Guid.NewGuid(), ScopusId = scopusId },
            new Researcher { PersonelId = "scopus-request-" + Guid.NewGuid() });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => new ResearcherRepository(db)
            .FindByIdentifiersAsync(new() { PersonelId = db.ChangeTracker.Entries<Researcher>()
                .Last().Entity.PersonelId, ScopusId = scopusId }));
    }
}
