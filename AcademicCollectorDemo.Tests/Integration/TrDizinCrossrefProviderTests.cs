using System.Net;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Crossref;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.TrDizin;
using AcademicCollectorDemo.Tests.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Persistence;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class TrDizinCrossrefProviderTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task GetByOrcidAsync_ExactAuthor_MapsPublicationDetails()
    {
        IConfiguration config = Config("TrDizin", "https://tr.example");
        using HttpClient http = new(new StubHttpHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/public/yazar/orcid" => StubHttpHandler.Json("""{"id":42,"orcid":"0000-0002-1825-0097","fullName":"Ada Test","orderPublicationCount":1}"""),
            "/api/authorPublicationsById/42" => StubHttpHandler.Json("""{"hits":{"total":{"value":1},"hits":[{"_id":"7","fields":{"id":["7"]}}]}}"""),
            "/api/publicationById/7" => StubHttpHandler.Json("""{"hits":{"hits":[{"_source":{"orderTitle":"A paper","doi":"10.1/test","publicationYear":"2024","orderCitationCount":null,"journal":{"name":"Test Journal"},"issue":{"year":"2024"},"authors":[{"inPublicationName":"Ada Test"}]}}]}}"""),
            _ => throw new InvalidOperationException(request.RequestUri.ToString())
        }));
        TrDizinProfile? result = await new TrDizinClient(http, config).GetByOrcidAsync("0000-0002-1825-0097");
        Assert.Equal(42, result!.AuthorId); Assert.Equal("A paper", Assert.Single(result.Works!).Title);
        Assert.Contains("Ada Test", result.Works![0].Authors);
        Assert.Equal("Test Journal", result.Works[0].Journal);
    }

    [Fact]
    public async Task GetByOrcidAsync_MissingOrMismatchedOrcid_DoesNotFollowAuthorId()
    {
        int calls = 0;
        using HttpClient http = new(new StubHttpHandler(_ => { calls++; return StubHttpHandler.Json("""{"id":42}"""); }));
        Assert.Null(await new TrDizinClient(http, Config("TrDizin", "https://tr.example"))
            .GetByOrcidAsync("0000-0002-1825-0097"));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task GetAsync_CrossrefEnvelope_UsesCitingCountAndIgnoresReferences()
    {
        using HttpClient http = new(new StubHttpHandler(_ => StubHttpHandler.Json("""
            {"message":{"DOI":"10.1234/TEST","title":["Better metadata"],"container-title":["Journal"],
            "author":[{"given":"Ada","family":"Test"}],"published":{"date-parts":[[2025,2,3]]},
            "is-referenced-by-count":11,"reference-count":99,"reference":[{"DOI":"10.9/not-authored"}]}}
            """)));
        CrossrefWork result = await new CrossrefClient(http, Config("Crossref", "https://crossref.example"))
            .GetAsync("p1", "10.1234/test");
        Assert.True(result.Found); Assert.Equal(11, result.CitedByCount); Assert.Equal("10.1234/test", result.Doi);
    }

    [Fact]
    public async Task GetAsync_DifferentReturnedDoi_RejectsResponse()
    {
        using HttpClient http = new(new StubHttpHandler(_ => StubHttpHandler.Json(
            """{"message":{"DOI":"10.1234/other"}}""")));
        CrossrefClient client = new(http, Config("Crossref", "https://crossref.example"));
        await Assert.ThrowsAsync<InvalidDataException>(() => client.GetAsync("p1", "10.1234/requested"));
    }

    [Fact]
    public async Task GetAsync_NotFound_ReturnsTimestampedNegativeCache()
    {
        using HttpClient http = new(new StubHttpHandler(_ => new(HttpStatusCode.NotFound)));
        CrossrefWork result = await new CrossrefClient(http, Config("Crossref", "https://crossref.example"))
            .GetAsync("p1", "10.1234/missing");
        Assert.False(result.Found); Assert.True(result.FetchedAt > DateTime.UtcNow.AddMinutes(-1));
    }

    [Theory]
    [InlineData("HTTP://DX.DOI.ORG/10.1234/ABC", "10.1234/abc")]
    [InlineData("doi:10.1234/ABC", "10.1234/abc")]
    public void NormalizeDoi_CommonForms_ReturnCanonical(string input, string expected) =>
        Assert.Equal(expected, CrossrefClient.NormalizeDoi(input));

    [Fact]
    public async Task EnrichAsync_SecondRun_UsesPersistedPositiveCache()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        string personelId = "crossref-cache-" + Guid.NewGuid().ToString("N");
        database.Researchers.Add(new Researcher { PersonelId = personelId });
        database.AcademicWorks.Add(new AcademicWork
        {
            PersonelId = personelId,
            Provider = AcademicWorkProvider.Yoksis,
            ProviderWorkId = "source",
            Doi = "https://doi.org/10.1234/CACHED",
            SyncedAt = DateTime.UtcNow
        });
        await database.SaveChangesAsync();
        int requests = 0;
        using HttpClient http = new(new StubHttpHandler(_ =>
        {
            requests++;
            return StubHttpHandler.Json("""{"message":{"DOI":"10.1234/cached","title":["Cached"]}}""");
        }));
        IConfiguration configuration = Config("Crossref", "https://crossref.example");
        CrossrefEnrichmentService service = new(database, new CrossrefClient(http, configuration), configuration);

        Assert.Equal(1, await service.EnrichAsync(personelId));
        Assert.Equal(0, await service.EnrichAsync(personelId));
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task SyncAsync_SourceDoiRemoved_RemovesHistoricalCrossrefPublication()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        string personelId = "crossref-remove-" + Guid.NewGuid().ToString("N");
        Researcher researcher = new() { PersonelId = personelId };
        database.Researchers.Add(researcher);
        database.CrossrefWorks.Add(new CrossrefWork
        {
            PersonelId = personelId,
            Doi = "10.1234/removed",
            Found = true,
            FetchedAt = DateTime.UtcNow,
            Title = "Removed source"
        });
        database.AcademicWorks.Add(new AcademicWork
        {
            PersonelId = personelId,
            Provider = AcademicWorkProvider.Crossref,
            ProviderWorkId = "10.1234/removed",
            Doi = "10.1234/removed",
            SyncedAt = DateTime.UtcNow
        });
        await database.SaveChangesAsync();

        await new AcademicWorkSynchronizer(database).SyncAsync(researcher);

        Assert.False(await database.AcademicWorks.AnyAsync(x => x.PersonelId == personelId));
    }

    [Fact]
    public async Task PersistAndEnrich_TrDizinDoi_CreatesDeduplicatedMultiSourceSummaryAndCachesRequest()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        string personelId = "trdizin-flow-" + Guid.NewGuid().ToString("N");
        Researcher researcher = new()
        {
            PersonelId = personelId,
            Orcid = "9999-9999-9999-9999",
            TrDizinProfile = new()
            {
                Orcid = "9999-9999-9999-9999",
                AuthorId = 42,
                LastUpdatedAt = DateTime.UtcNow,
                RawAuthorJson = "{}",
                RawPublicationsJson = "{}",
                Works = [new() { PublicationId = "7", Title = "Source title", Doi = "10.1234/flow", RawDataJson = "{}" }]
            }
        };
        await new ResearcherRepository(database).SaveAsync(researcher);
        AcademicWorkSynchronizer works = new(database);
        await works.SyncAsync(researcher);
        int requests = 0;
        using HttpClient http = new(new StubHttpHandler(_ =>
        {
            requests++;
            return StubHttpHandler.Json("""{"message":{"DOI":"10.1234/flow","title":["Crossref title"]}}""");
        }));
        IConfiguration configuration = Config("Crossref", "https://crossref.example");
        CrossrefEnrichmentService enrichment = new(database, new CrossrefClient(http, configuration), configuration);
        Assert.Equal(1, await enrichment.EnrichAsync(personelId));
        await works.SyncAsync(researcher);
        await new PublicationSummarySynchronizer(database).SyncAsync(personelId);
        Assert.Equal(0, await enrichment.EnrichAsync(personelId));

        var summary = await database.PublicationSummaries.SingleAsync(x => x.PersonelId == personelId);
        Assert.Contains("TrDizin", summary.Sources);
        Assert.Contains("Crossref", summary.Sources);
        Assert.Equal(1, requests);
    }

    private static IConfiguration Config(string provider, string url) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { [$"{provider}:ApiBaseUrl"] = url }).Build();
}
