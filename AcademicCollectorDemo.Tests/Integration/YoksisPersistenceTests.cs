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
        var researcher = new Researcher
        {
            PersonelId = "test-" + Guid.NewGuid().ToString("N") };
        db.Researchers.Add(researcher);
        await db.SaveChangesAsync();
        db.YoksisRecords.Add(new() { PersonelId = researcher.PersonelId, CategoryName = "Makaleler", OperationName = "getMakaleBilgisiDetayV1", ExternalRecordId = "old", RecordJson = "{}", CollectedAt = DateTime.UtcNow });
        db.AcademicWorks.Add(new() { PersonelId = researcher.PersonelId, Provider = AcademicWorkProvider.Yoksis, ProviderWorkId = "Makale:old", SourceType = "Makale", Title = "Existing publication", SyncedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var summaries = new PublicationSummarySynchronizer(db);
        await summaries.SyncAsync(researcher.PersonelId);
        var summary = await db.PublicationSummaries.SingleAsync(x => x.PersonelId == researcher.PersonelId);
        db.PublicationDisplayApprovals.Add(new() { PersonelId = researcher.PersonelId, PublicationSummaryId = summary.Id, ApprovedAt = DateTime.UtcNow });
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
            PersonelId = researcher.PersonelId, TcKimlikNo = "  " + new string('1', 11) + "  ", UpdatedAfter = DateTime.UtcNow.AddDays(-1)
        });

        Assert.True(response.IsSaved, string.Join("\n", response.Messages));
        Assert.Equal(new string('1', 11),
            (await db.Researchers.FindAsync(researcher.PersonelId))!.TcKimlikNo);
        Assert.True(await db.YoksisRecords.AnyAsync(x => x.PersonelId == researcher.PersonelId));
        Assert.True(await db.AcademicWorks.AnyAsync(x => x.PersonelId == researcher.PersonelId));
        Assert.True(await db.PublicationDisplayApprovals.AnyAsync(x => x.PublicationSummaryId == summary.Id));
    }

    [Fact]
    public async Task CollectAsync_IdentityResponseHasDifferentResearcherId_PersistsRequestedTcKimlikNo()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        string personelId = "test-" + Guid.NewGuid().ToString("N");
        string tcKimlikNo = new string('2', 11);
        var http = new HttpClient(new StubHttpHandler(_ => StubHttpHandler.Json(
            "<Envelope><Body><Response><Sonuc><SonucKod>1</SonucKod></Sonuc>" +
            "<Record><ARASTIRMACI_ID>provider-researcher-id</ARASTIRMACI_ID></Record>" +
            "</Response></Body></Envelope>")));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Yoksis:Username"] = Guid.NewGuid().ToString("N"),
            ["Yoksis:Password"] = Guid.NewGuid().ToString("N")
        }).Build();
        var summaries = new PublicationSummarySynchronizer(db);
        var handler = new YoksisCollectionHandler(new YoksisCollectionService(new(http, config)),
            new(db), new(db), new ResearcherRepository(db), summaries, db);

        var response = await handler.CollectAsync(new()
        {
            PersonelId = personelId,
            TcKimlikNo = tcKimlikNo
        });

        Assert.True(response.IsSaved, string.Join("\n", response.Messages));
        Researcher saved = (await db.Researchers.FindAsync(personelId))!;
        Assert.Equal(tcKimlikNo, saved.TcKimlikNo);
        Assert.NotEqual("provider-researcher-id", saved.TcKimlikNo);
    }

    [Fact]
    public async Task SaveAsync_DuplicateTcKimlikNo_RejectsDifferentPersonnel()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        string tcKimlikNo = new string('3', 11);
        db.Researchers.Add(new Researcher
        {
            PersonelId = "test-" + Guid.NewGuid().ToString("N"),
            TcKimlikNo = tcKimlikNo
        });
        await db.SaveChangesAsync();
        var repository = new ResearcherRepository(db);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => repository.SaveAsync(new Researcher
        {
            PersonelId = "test-" + Guid.NewGuid().ToString("N"),
            TcKimlikNo = tcKimlikNo
        }));

        Assert.Contains("PersonelID", exception.Message);
    }

    [Fact]
    public async Task SyncAsync_RecordsWithoutProviderIds_PreservesDistinctWorks()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        var researcher = new Researcher { PersonelId = "test-" + Guid.NewGuid().ToString("N") };
        db.Researchers.Add(researcher);
        await db.SaveChangesAsync();
        var response = new YoksisCollectResponse
        {
            Categories = [new() { OperationName = "getMakaleBilgisiDetayV1", IsSuccess = true,
                Records = [new() { ["MAKALE_ADI"] = "First work" }, new() { ["MAKALE_ADI"] = "Second work" }] }]
        };
        var sync = new YoksisAcademicWorkSynchronizer(db);
        Assert.Equal(2, await sync.SyncAsync(researcher.PersonelId, response));
        Assert.Equal(2, await sync.SyncAsync(researcher.PersonelId, response));
    }

    [Theory]
    [InlineData("YAZAR_ADI")]
    [InlineData("DERGI_ADI")]
    [InlineData("ERISIM_LINKI")]
    public async Task SyncAsync_SameTitleWithoutIdsAndDifferentMetadata_PreservesBothRecords(string field)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        var researcher = new Researcher { PersonelId = "test-" + Guid.NewGuid().ToString("N") };
        db.Researchers.Add(researcher);
        await db.SaveChangesAsync();
        var first = new Dictionary<string, string?> { ["MAKALE_ADI"] = "Shared title", ["YIL"] = "2026", [field] = "First value" };
        var second = new Dictionary<string, string?>(first) { [field] = "Second value" };
        var response = new YoksisCollectResponse
        {
            Categories = [new() { OperationName = "getMakaleBilgisiDetayV1", IsSuccess = true, Records = [first, second] }]
        };
        var sync = new YoksisAcademicWorkSynchronizer(db);
        Assert.Equal(2, await sync.SyncAsync(researcher.PersonelId, response));
        var ids = await db.AcademicWorks.Where(x => x.PersonelId == researcher.PersonelId).OrderBy(x => x.Id).Select(x => x.Id).ToListAsync();

        response.Categories[0].Records = response.Categories[0].Records
            .Select(record => record.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value)).Reverse().ToList();
        Assert.Equal(2, await sync.SyncAsync(researcher.PersonelId, response, isIncremental: true));
        Assert.Equal(ids, await db.AcademicWorks.Where(x => x.PersonelId == researcher.PersonelId).OrderBy(x => x.Id).Select(x => x.Id).ToListAsync());
    }
}
