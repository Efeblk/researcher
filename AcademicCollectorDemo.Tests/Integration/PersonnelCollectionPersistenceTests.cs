using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.GoogleScholar;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Orcid;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Collection;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Persistence;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;
using AcademicCollectorDemo.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class PersonnelCollectionPersistenceTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task SyncAsync_LongOrcidAuthors_PreservesFullAuthorListThroughPublicationSummary()
    {
        using var scope = fixture.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        string personelId = "long-authors-" + Guid.NewGuid().ToString("N");
        string authors = string.Join(", ", Enumerable.Range(1, 700).Select(index => $"Synthetic Author {index}"));
        Researcher researcher = new()
        {
            PersonelId = personelId,
            Orcid = "0000-0002-1825-0097",
            OrcidProfile = new()
            {
                PersonelId = personelId,
                LastUpdatedAt = DateTime.UtcNow,
                Works = [new() { PutCode = 1, Title = "Large collaboration", Authors = authors }]
            }
        };
        database.Researchers.Add(researcher);
        await database.SaveChangesAsync();

        await new AcademicWorkSynchronizer(database).SyncAsync(researcher);
        await new PublicationSummarySynchronizer(database).SyncAsync(personelId);
        database.ChangeTracker.Clear();

        Assert.Equal(authors, (await database.OrcidWorks.AsNoTracking().SingleAsync(work =>
            work.OrcidProfile!.PersonelId == personelId)).Authors);
        Assert.Equal(authors, (await database.AcademicWorks.AsNoTracking().SingleAsync(work =>
            work.PersonelId == personelId)).Authors);
        Assert.Equal(authors, (await database.PublicationSummaries.AsNoTracking().SingleAsync(summary =>
            summary.PersonelId == personelId)).Authors);
    }

    [Fact]
    public async Task CollectAsync_OversizedBoundedProviderField_ReturnsSafePersistenceFailureCode()
    {
        using var scope = fixture.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["WebOfScience:ApiKey"] = "synthetic-key",
                ["WebOfScience:DatabaseIds:0"] = "WOS"
            }).Build();
        string oversizedTitle = new('T', 2100);
        string responseJson = $$"""
            {"metadata":{"total":1,"limit":50},"hits":[{"uid":"WOS:oversized","title":{{JsonSerializer.Serialize(oversizedTitle)}},"types":["Article"]}]}
            """;
        using var http = new HttpClient(new StubHttpHandler(_ => StubHttpHandler.Json(responseJson)));
        var collectionService = new ResearcherCollectionService(
            new OrcidClient(http, configuration), new GoogleScholarClient(http, configuration),
            new OpenAlexClient(http, configuration), new WebOfScienceClient(http, configuration),
            new AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.TrDizin.TrDizinClient(http, configuration), new(), new(), configuration);
        var handler = new ResearcherCollectionHandler(new ResearcherIdentifierParser(), collectionService,
            new ResearcherRepository(database), new AcademicWorkSynchronizer(database),
            new PublicationSummarySynchronizer(database), new AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Crossref.CrossrefEnrichmentService(database,
                new AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Crossref.CrossrefClient(http, configuration), configuration), database);

        ResearcherCollectResponse response = await handler.CollectAsync(new()
        {
            PersonelId = "oversized-field-" + Guid.NewGuid().ToString("N"),
            Identifiers = ["--researcherid", "Z-9999-2099"]
        });

        Assert.False(response.IsSaved);
        Assert.Equal("PersistenceDataTooLong", response.FailureCode);
        Assert.Contains(response.Messages, message => message.Contains("metaverisi veritabanı alanına sığmadı"));
        Assert.DoesNotContain(response.Messages, message => message.Contains(oversizedTitle));
    }

    [Fact]
    public async Task CollectAsync_NewResearcher_PreservesOpaquePersonelIdAcrossStoredData()
    {
        using var scope = fixture.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["WebOfScience:ApiKey"] = "synthetic-key",
                ["WebOfScience:DatabaseIds:0"] = "WOS"
            }).Build();
        var httpHandler = new StubHttpHandler(_ => StubHttpHandler.Json("""
            {"metadata":{"total":1,"limit":50},"hits":[{"uid":"WOS:001","title":"Synthetic work","types":["Article"],"source":{"publishYear":2025,"sourceTitle":"Synthetic journal"},"names":{"authors":[{"researcherId":"A-1009-2008","displayName":"Synthetic Researcher"}]},"citations":[{"db":"WOS","count":2}]}]}
            """));
        using var http = new HttpClient(httpHandler);
        var collectionService = new ResearcherCollectionService(
            new OrcidClient(http, configuration),
            new GoogleScholarClient(http, configuration),
            new OpenAlexClient(http, configuration),
            new WebOfScienceClient(http, configuration),
            new AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.TrDizin.TrDizinClient(http, configuration), new(), new(), configuration);
        var works = new AcademicWorkSynchronizer(database);
        var summaries = new PublicationSummarySynchronizer(database);
        var handler = new ResearcherCollectionHandler(
            new ResearcherIdentifierParser(), collectionService,
            new ResearcherRepository(database), works, summaries, new AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Crossref.CrossrefEnrichmentService(database,
                new AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Crossref.CrossrefClient(http, configuration), configuration), database);

        const string personelId = "00123-B";
        var response = await handler.CollectAsync(new()
        {
            PersonelId = personelId,
            Identifiers = ["--researcherid", "A-1009-2008"]
        });

        Assert.True(response.IsSaved, string.Join("\n", response.Messages));
        Assert.Equal(personelId, response.Researcher!.PersonelId);
        Assert.True(await database.Researchers.AnyAsync(value => value.PersonelId == personelId));
        Assert.True(await database.WebOfScienceProfiles.AnyAsync(value => value.PersonelId == personelId));
        Assert.True(await database.AcademicWorks.AnyAsync(value => value.PersonelId == personelId));
        Assert.True(await database.PublicationSummaries.AnyAsync(value => value.PersonelId == personelId));
    }
}
