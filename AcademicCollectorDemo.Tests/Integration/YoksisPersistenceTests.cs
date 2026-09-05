using AcademicCollectorDemo.Tests.Infrastructure;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis.Collection;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis.Persistence;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Persistence;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class YoksisPersistenceTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task CollectAsync_IncrementalEmptyResponse_PreservesExistingRecordsAndSelections()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        var researcher = new Researcher { YoksisResearcherId = "synthetic-" + Guid.NewGuid().ToString("N") };
        db.Researchers.Add(researcher);
        await db.SaveChangesAsync();
        db.YoksisRecords.Add(new() { ResearcherId = researcher.Id, CategoryName = "Makaleler", OperationName = "getMakaleBilgisiDetayV1", ExternalRecordId = "old", RecordJson = "{}", CollectedAt = DateTime.UtcNow });
        db.AcademicWorks.Add(new() { ResearcherId = researcher.Id, Provider = AcademicWorkProvider.Yoksis, ProviderWorkId = "Makale:old", SourceType = "Makale", Title = "Existing publication", SyncedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var summaries = new PublicationSummarySynchronizer(db);
        await summaries.SyncAsync(researcher.Id);
        var summary = await db.PublicationSummaries.SingleAsync(x => x.ResearcherId == researcher.Id);
        db.PublicationDisplayApprovals.Add(new() { ResearcherId = researcher.Id, PublicationSummaryId = summary.Id, ApprovedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var http = new HttpClient(new StubHttpHandler(_ => StubHttpHandler.Json(
            "<Envelope><Body><Response><Sonuc><SonucKod>1</SonucKod></Sonuc></Response></Body></Envelope>")));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Yoksis:Username"] = Guid.NewGuid().ToString("N"),
            ["Yoksis:Password"] = Guid.NewGuid().ToString("N")
        }).Build();
        var handler = new YoksisCollectionHandler(new YoksisCollectionService(new(http, config)),
            new(db), new(db), new ResearcherRepository(db), summaries, db);
        var response = await handler.CollectAsync(new()
        {
            ResearcherId = researcher.Id, TcKimlikNo = new string('1', 11), UpdatedAfter = DateTime.UtcNow.AddDays(-1)
        });

        Assert.True(response.IsSaved, string.Join("\n", response.Messages));
        Assert.True(await db.YoksisRecords.AnyAsync(x => x.ResearcherId == researcher.Id));
        Assert.True(await db.AcademicWorks.AnyAsync(x => x.ResearcherId == researcher.Id));
        Assert.True(await db.PublicationDisplayApprovals.AnyAsync(x => x.PublicationSummaryId == summary.Id));
    }

    [Fact]
    public async Task SyncAsync_RecordsWithoutProviderIds_PreservesDistinctWorks()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        var researcher = new Researcher();
        db.Researchers.Add(researcher);
        await db.SaveChangesAsync();
        var response = new YoksisCollectResponse
        {
            Categories = [new() { OperationName = "getMakaleBilgisiDetayV1", IsSuccess = true,
                Records = [new() { ["MAKALE_ADI"] = "First work" }, new() { ["MAKALE_ADI"] = "Second work" }] }]
        };
        var sync = new YoksisAcademicWorkSynchronizer(db);
        Assert.Equal(2, await sync.SyncAsync(researcher.Id, response));
        Assert.Equal(2, await sync.SyncAsync(researcher.Id, response));
    }

    [Theory]
    [InlineData("YAZAR_ADI")]
    [InlineData("DERGI_ADI")]
    [InlineData("ERISIM_LINKI")]
    public async Task SyncAsync_SameTitleWithoutIdsAndDifferentMetadata_PreservesBothRecords(string field)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        var researcher = new Researcher();
        db.Researchers.Add(researcher);
        await db.SaveChangesAsync();
        var first = new Dictionary<string, string?> { ["MAKALE_ADI"] = "Shared title", ["YIL"] = "2026", [field] = "First value" };
        var second = new Dictionary<string, string?>(first) { [field] = "Second value" };
        var response = new YoksisCollectResponse
        {
            Categories = [new() { OperationName = "getMakaleBilgisiDetayV1", IsSuccess = true, Records = [first, second] }]
        };
        var sync = new YoksisAcademicWorkSynchronizer(db);
        Assert.Equal(2, await sync.SyncAsync(researcher.Id, response));
        var ids = await db.AcademicWorks.Where(x => x.ResearcherId == researcher.Id).OrderBy(x => x.Id).Select(x => x.Id).ToListAsync();

        response.Categories[0].Records = response.Categories[0].Records
            .Select(record => record.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value)).Reverse().ToList();
        Assert.Equal(2, await sync.SyncAsync(researcher.Id, response, isIncremental: true));
        Assert.Equal(ids, await db.AcademicWorks.Where(x => x.ResearcherId == researcher.Id).OrderBy(x => x.Id).Select(x => x.Id).ToListAsync());
    }
}
