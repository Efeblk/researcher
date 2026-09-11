using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.SemanticScholar;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using AcademicCollectorDemo.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class SemanticScholarWorkSourceSynchronizerTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task SyncAsync_CachedPaper_AttachesPdfToEveryMatchingPersonnelWorkAndIsIdempotent()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AcademicDbContext db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        string suffix = Guid.NewGuid().ToString("N");
        string doi = "10.1234/" + suffix;
        string firstPerson = "s2-first-" + suffix;
        string secondPerson = "s2-second-" + suffix;
        db.Researchers.AddRange(new Researcher { PersonelId = firstPerson },
            new Researcher { PersonelId = secondPerson });
        db.AcademicWorks.AddRange(
            Work(firstPerson, "https://doi.org/" + doi.ToUpperInvariant()),
            Work(firstPerson, doi),
            Work(secondPerson, doi));
        db.SemanticScholarPapers.Add(new SemanticScholarPaper
        {
            NormalizedDoi = doi, Found = true, FetchedAt = DateTime.UtcNow,
            OpenAccessPdfJson = """{"url":"https://pdfs.semanticscholar.org/example.pdf","status":"GREEN"}"""
        });
        await db.SaveChangesAsync();
        SemanticScholarWorkSourceSynchronizer synchronizer = new(db);

        Assert.Equal(2, await synchronizer.SyncAsync(firstPerson));
        Assert.Empty(await db.AcademicWorkSources.Where(x => x.AcademicWork!.PersonelId == secondPerson).ToListAsync());
        Assert.Equal(1, await synchronizer.SyncAsync(secondPerson));
        Assert.Equal(0, await synchronizer.SyncAsync(firstPerson));

        List<AcademicWork> works = await db.AcademicWorks.Include(x => x.Sources)
            .Where(x => x.PersonelId == firstPerson || x.PersonelId == secondPerson).ToListAsync();
        Assert.All(works, work =>
        {
            AcademicWorkSource source = Assert.Single(work.Sources);
            Assert.Equal("https://pdfs.semanticscholar.org/example.pdf", source.Url);
            Assert.Equal("Pdf", source.Kind);
            Assert.Equal("SemanticScholar.OpenAccessPdf", source.Origin);
            Assert.True(source.IsOpenAccess);
            Assert.Null(work.CitedByCount);
        });
    }

    private static AcademicWork Work(string personelId, string doi) => new()
    {
        PersonelId = personelId,
        Provider = AcademicWorkProvider.Orcid,
        ProviderWorkId = Guid.NewGuid().ToString("N"),
        Doi = doi,
        SyncedAt = DateTime.UtcNow
    };
}
