using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.GoogleScholar;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Orcid;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Collection;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Persistence;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;
using AcademicCollectorDemo.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class PersonnelCollectionPersistenceTests(SqlServerFixture fixture)
{
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
            new(), new(), configuration);
        var works = new AcademicWorkSynchronizer(database);
        var summaries = new PublicationSummarySynchronizer(database);
        var handler = new ResearcherCollectionHandler(
            new ResearcherIdentifierParser(), collectionService,
            new ResearcherRepository(database), works, summaries, database);

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
