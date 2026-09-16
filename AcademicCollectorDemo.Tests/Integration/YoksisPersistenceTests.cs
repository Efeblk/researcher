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
using AcademicCollectorDemo.Modules.AcademicPerformance;
using AcademicCollectorDemo.Modules.AcademicPerformance.Application;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class YoksisPersistenceTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task CollectAsync_TcOnly_UsesYoksisAndPersistsIdentityWithoutNormalProviderCalls()
    {
        int requestCount = 0;
        List<Uri> requestedUris = [];
        string personelId = "test-tc-only-" + Guid.NewGuid().ToString("N");
        string tcKimlikNo = new('4', 11);
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:AcademicDatabase"] = fixture.ConnectionString,
                ["BulkCollection:WorkerEnabled"] = "false",
                ["Yoksis:Username"] = Guid.NewGuid().ToString("N"),
                ["Yoksis:Password"] = Guid.NewGuid().ToString("N"),
                ["ProviderRequestLimits:Yoksis:MinimumIntervalMilliseconds"] = "0"
            }).Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddAcademicPerformanceModule(configuration);
        services.AddSingleton(new HttpClient(new StubHttpHandler(request =>
        {
            requestCount++;
            requestedUris.Add(request.RequestUri!);
            return StubHttpHandler.Json(
                "<Envelope><Body><Response><Sonuc><SonucKod>1</SonucKod></Sonuc>" +
                "<Record><ORCID>0000-0002-1825-0097</ORCID>" +
                "<RESEARCHER_ID>A-1009-2008</RESEARCHER_ID></Record>" +
                "</Response></Body></Envelope>");
        })));
        await using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        var response = await scope.ServiceProvider
            .GetRequiredService<IAcademicPerformanceApplicationService>()
            .CollectAsync(new() { PersonelId = personelId, TcKimlikNo = tcKimlikNo });

        Assert.True(requestCount > 0);
        Assert.All(requestedUris, uri =>
            Assert.Equal("servisler.yok.gov.tr", uri.Host));
        Assert.True(response.IsSaved, string.Join("\n", response.Messages));
        var saved = await scope.ServiceProvider.GetRequiredService<AcademicDbContext>()
            .Researchers.AsNoTracking().SingleAsync(item => item.PersonelId == personelId);
        Assert.Equal(tcKimlikNo, saved.TcKimlikNo);
        Assert.Null(saved.Orcid);
        Assert.Null(saved.GoogleScholarId);
        Assert.Null(saved.WebOfScienceResearcherId);
    }

    [Fact]
    public async Task CollectAsync_ProviderIdentityResponse_DoesNotChangeManuallySuppliedIdentifiers()
    {
        string personelId = "test-manual-identity-" + Guid.NewGuid().ToString("N");
        string tcKimlikNo = new('6', 11);
        string identitySuffix = Random.Shared.Next(1000, 9999).ToString();
        string manualOrcid = $"9999-9999-9999-{identitySuffix}";
        string manualResearcherId = $"Z-{identitySuffix}-{identitySuffix}";
        List<Uri> requestedUris = [];
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:AcademicDatabase"] = fixture.ConnectionString,
                ["BulkCollection:WorkerEnabled"] = "false",
                ["Yoksis:Username"] = Guid.NewGuid().ToString("N"),
                ["Yoksis:Password"] = Guid.NewGuid().ToString("N"),
                ["ProviderRequestLimits:Yoksis:MinimumIntervalMilliseconds"] = "0"
            }).Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddAcademicPerformanceModule(configuration);
        services.AddSingleton(new HttpClient(new StubHttpHandler(request =>
        {
            requestedUris.Add(request.RequestUri!);
            return StubHttpHandler.Json(
                "<Envelope><Body><Response><Sonuc><SonucKod>1</SonucKod></Sonuc>" +
                "<Record><ORCID>0000-0002-1825-0097</ORCID>" +
                "<RESEARCHER_ID>A-1009-2008</RESEARCHER_ID>" +
                "<PERSONEL_ADI>Ada</PERSONEL_ADI></Record>" +
                "</Response></Body></Envelope>");
        })));
        await using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        db.Researchers.Add(new Researcher
        {
            PersonelId = personelId,
            TcKimlikNo = tcKimlikNo,
            Orcid = manualOrcid,
            WebOfScienceResearcherId = manualResearcherId,
            WebOfScienceProfile = new WebOfScienceProfile
            {
                LastUpdatedAt = DateTime.UtcNow,
                DocumentPagesJson = "{\"WOS\":[{}]}",
                Works = []
            }
        });
        await db.SaveChangesAsync();

        var service = scope.ServiceProvider
            .GetRequiredService<IAcademicPerformanceApplicationService>();
        var tcOnlyResponse = await service
            .CollectAsync(new()
            {
                PersonelId = personelId,
                TcKimlikNo = tcKimlikNo
            });
        var explicitIdResponse = await service
            .CollectAsync(new()
            {
                PersonelId = personelId,
                TcKimlikNo = tcKimlikNo,
                WebOfScienceResearcherId = manualResearcherId
            });

        Assert.True(tcOnlyResponse.IsSaved, string.Join("\n", tcOnlyResponse.Messages));
        Assert.True(explicitIdResponse.IsSaved, string.Join("\n", explicitIdResponse.Messages));
        Assert.All(requestedUris, uri =>
            Assert.Equal("servisler.yok.gov.tr", uri.Host));
        db.ChangeTracker.Clear();
        Researcher saved = await db.Researchers.SingleAsync(item => item.PersonelId == personelId);
        Assert.Equal(manualOrcid, saved.Orcid);
        Assert.Equal(manualResearcherId, saved.WebOfScienceResearcherId);
        Assert.Equal("Ada", saved.FirstName);
    }

    [Fact]
    public async Task CollectAsync_TcOwnedByOtherPersonnel_RejectsBeforeHttp()
    {
        int requestCount = 0;
        string tcKimlikNo = new('5', 11);
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:AcademicDatabase"] = fixture.ConnectionString,
                ["BulkCollection:WorkerEnabled"] = "false",
                ["Yoksis:Username"] = Guid.NewGuid().ToString("N"),
                ["Yoksis:Password"] = Guid.NewGuid().ToString("N")
            }).Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddAcademicPerformanceModule(configuration);
        services.AddSingleton(new HttpClient(new StubHttpHandler(_ =>
        {
            requestCount++;
            return StubHttpHandler.Json("<Envelope />");
        })));
        await using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        database.Researchers.Add(new Researcher
        {
            PersonelId = "test-owner-" + Guid.NewGuid().ToString("N"),
            TcKimlikNo = tcKimlikNo
        });
        await database.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => scope.ServiceProvider
            .GetRequiredService<IAcademicPerformanceApplicationService>()
            .CollectAsync(new()
            {
                PersonelId = "test-other-" + Guid.NewGuid().ToString("N"),
                TcKimlikNo = tcKimlikNo
            }));

        Assert.Equal(0, requestCount);
    }

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
        await new CanonicalWorkSynchronizer(db).SyncAsync(researcher.PersonelId);
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
