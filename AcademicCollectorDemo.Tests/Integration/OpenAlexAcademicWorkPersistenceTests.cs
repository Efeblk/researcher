using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Orcid;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;
using AcademicCollectorDemo.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class OpenAlexAcademicWorkPersistenceTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task SyncAsync_OpenAlexWorks_PersistsSharedRowsAndIsIdempotent()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        var researcher = CreateResearcher();
        db.Researchers.Add(researcher);
        await db.SaveChangesAsync();
        new AcademicWorkCategorizer().Categorize(researcher);
        var synchronizer = new AcademicWorkSynchronizer(db);

        await synchronizer.SyncAsync(researcher);
        AcademicWork first = await db.AcademicWorks.Include(item => item.Sources).SingleAsync(item =>
            item.PersonelId == researcher.PersonelId && item.Provider == AcademicWorkProvider.OpenAlex);
        first.Sources.Add(new AcademicWorkSource
        { Url = "https://legacy.test/CaseSensitive.pdf", Kind = "Pdf", Origin = "Legacy.OpenAccessUrl" });
        await db.SaveChangesAsync();
        researcher.OpenAlexProfile!.Works![0].RawDataJson = "{\"id\":\"W1\"}";
        await synchronizer.SyncAsync(researcher);

        AcademicWork work = await db.AcademicWorks.Include(item => item.Sources).SingleAsync(item =>
            item.PersonelId == researcher.PersonelId &&
            item.Provider == AcademicWorkProvider.OpenAlex);
        Assert.StartsWith("https://openalex.org/W", work.ProviderWorkId);
        Assert.Equal("10.1234/shared", work.Doi);
        Assert.Equal(AcademicWorkCategory.Article, work.Category);
        Assert.Equal(AcademicWorkCategorySource.OpenAlex, work.CategorySource);
        Assert.Equal(9, work.CitedByCount);
        Assert.Equal("Synthetic Journal", work.Publication);
        Assert.Equal("Ada Example", work.Authors);
        Assert.Equal("Stored payload abstract.", work.Abstract);
        Assert.Equal("https://example.test/work.pdf", work.FullTextUrl);
        Assert.Contains(work.Sources, source => source.Url == "https://example.test/work.pdf");
        Assert.Contains(work.Sources, source => source.Url == "https://legacy.test/CaseSensitive.pdf");
        Assert.Null(work.HasFullText);
        Assert.NotNull(await db.OpenAlexWorks.SingleAsync(item =>
            item.OpenAlexProfileId == researcher.OpenAlexProfile!.Id));
    }

    [Fact]
    public async Task SyncAsync_OrcidAndOpenAlexDoi_DeduplicatesSummaryAndRetainsSources()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        var researcher = CreateResearcher();
        researcher.OrcidProfile = new OrcidProfile
        {
            LastUpdatedAt = DateTime.UtcNow,
            Works =
            [
                new OrcidWork
                {
                    PutCode = 1,
                    Title = "Shared publication from ORCID",
                    PublicationYear = 2025,
                    Doi = "https://doi.org/10.1234/SHARED",
                    WorkType = "journal-article"
                },
                new OrcidWork
                {
                    PutCode = 2,
                    Title = "Distinct publication",
                    PublicationYear = 2025,
                    Doi = "10.1234/distinct",
                    WorkType = "journal-article"
                }
            ]
        };
        db.Researchers.Add(researcher);
        await db.SaveChangesAsync();
        new AcademicWorkCategorizer().Categorize(researcher);
        var works = new AcademicWorkSynchronizer(db);
        var summaries = new PublicationSummarySynchronizer(db);

        await works.SyncAsync(researcher);
        Assert.Equal(2, await summaries.SyncAsync(researcher.PersonelId));
        await works.SyncAsync(researcher);
        Assert.Equal(2, await summaries.SyncAsync(researcher.PersonelId));

        Assert.Equal(3, await db.AcademicWorks.CountAsync(item =>
            item.PersonelId == researcher.PersonelId));
        PublicationSummary shared = await db.PublicationSummaries.SingleAsync(item =>
            item.PersonelId == researcher.PersonelId && item.Doi == "10.1234/shared");
        Assert.Equal("OpenAlex,Orcid", shared.Sources);
        Assert.Single(await db.PublicationSummaries.Where(item =>
            item.PersonelId == researcher.PersonelId && item.Doi == "10.1234/distinct")
            .ToListAsync());
    }

    private static Researcher CreateResearcher()
    {
        string suffix = Guid.NewGuid().ToString("N");
        return new Researcher
        {
            PersonelId = "openalex-" + suffix,
            LastUpdatedAt = DateTime.UtcNow,
            OpenAlexProfile = new OpenAlexProfile
            {
                OpenAlexAuthorId = "https://openalex.org/A" + suffix,
                LastUpdatedAt = DateTime.UtcNow,
                Works =
                [
                    new OpenAlexWork
                    {
                        OpenAlexWorkId = "https://openalex.org/W" + suffix,
                        Title = "Shared publication from OpenAlex",
                        PublicationYear = 2025,
                        PublicationDate = new DateTime(2025, 2, 3),
                        Doi = "10.1234/shared",
                        WorkType = "article",
                        CitedByCount = 9,
                        Authors = "Ada Example",
                        SourceName = "Synthetic Journal",
                        Url = "https://example.test/work",
                        OpenAccessUrl = "https://example.test/work.pdf",
                        RawDataJson = "{\"id\":\"W1\",\"abstract_inverted_index\":{\"Stored\":[0],\"payload\":[1],\"abstract.\":[2]}}"
                    }
                ]
            }
        };
    }
}
