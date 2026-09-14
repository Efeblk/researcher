using ResearcherAnalysisService.Products.ArticleSummaries;
using ResearcherAnalysisService.Products.Data;
using ResearcherAnalysisService.SourceData.Works;
using ResearcherAnalysisService.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ResearcherAnalysisService.Tests;

[Collection("Analysis Product SQL Server")]
public sealed class CanonicalArticleEvidenceMigrationTests(AnalysisProductSqlServerFixture fixture)
{
    [Fact]
    public async Task SourceIdentity_ContentParserAndCanonicalWork_AreDistinctAndUnique()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        DateTime now = DateTime.UtcNow;
        CanonicalWork first = new()
        {
            NormalizedDoi = "10.7000/" + Guid.NewGuid().ToString("N"),
            CreatedAt = now,
            UpdatedAt = now
        };
        CanonicalWork second = new()
        {
            NormalizedDoi = "10.7001/" + Guid.NewGuid().ToString("N"),
            CreatedAt = now,
            UpdatedAt = now
        };
        database.CanonicalWorks.AddRange(first, second);
        await database.SaveChangesWithSourceSeedAsync(fixture.ConnectionString);

        DateTimeOffset createdAt = DateTimeOffset.UtcNow;
        database.ArticleSourceSnapshots.AddRange(
            Snapshot(first.Id, new string('a', 64), "parser-v1", createdAt),
            Snapshot(first.Id, new string('a', 64), "parser-v2", createdAt),
            Snapshot(first.Id, new string('b', 64), "parser-v1", createdAt),
            Snapshot(second.Id, new string('a', 64), "parser-v1", createdAt));
        await database.SaveChangesWithSourceSeedAsync(fixture.ConnectionString);

        Assert.Equal(4, await database.ArticleSourceSnapshots.AsNoTracking()
            .CountAsync(snapshot => snapshot.CanonicalWorkId == first.Id ||
                snapshot.CanonicalWorkId == second.Id));
        database.ArticleSourceSnapshots.Add(
            Snapshot(first.Id, new string('a', 64), "parser-v1", createdAt));
        await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
    }

    private static ArticleSourceSnapshot Snapshot(
        int canonicalWorkId, string hash, string extractionVersion, DateTimeOffset createdAt) => new()
    {
        CanonicalWorkId = canonicalWorkId,
        ExtractedTextHash = hash,
        SourceKind = "abstract",
        ExtractionVersion = extractionVersion,
        CreatedAt = createdAt
    };
}
