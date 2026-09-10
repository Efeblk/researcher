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
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Collection;
using AcademicCollectorDemo.Modules.AcademicPerformance.Application;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Orcid;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.GoogleScholar;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;

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
    public async Task CollectAsync_DisabledTrDizin_MakesNoTrDizinRequest()
    {
        int trDizinRequests = 0;
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["TrDizin:ApiBaseUrl"] = "https://trdizin.example",
                ["ProviderRequestLimits:TrDizin:Enabled"] = "false"
            }).Build();
        using HttpClient http = new(new StubHttpHandler(request =>
        {
            if (request.RequestUri!.Host == "trdizin.example")
            {
                trDizinRequests++;
            }
            return new(HttpStatusCode.NotFound);
        }));
        ResearcherCollectionService service = new(
            new OrcidClient(http, configuration),
            new GoogleScholarClient(http, configuration),
            new OpenAlexClient(http, configuration),
            new WebOfScienceClient(http, configuration),
            new TrDizinClient(http, configuration),
            new(),
            new(),
            configuration);
        List<string> messages = [];

        await service.CollectAsync(
            new Researcher { PersonelId = "disabled", Orcid = "0000-0001-8560-7482" },
            new Researcher { Orcid = "0000-0001-8560-7482" },
            messages);

        Assert.Equal(0, trDizinRequests);
        Assert.Contains(messages, message => message.Contains("TR Dizin") && message.StartsWith("[ATLANDI]"));
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
    public async Task EnrichAsync_SecondRequestCancelled_PropagatesCancellationAndPreservesFirstResult()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        string personelId = "crossref-cancel-" + Guid.NewGuid().ToString("N");
        database.Researchers.Add(new Researcher { PersonelId = personelId });
        database.AcademicWorks.AddRange(
            new AcademicWork
            {
                PersonelId = personelId,
                Provider = AcademicWorkProvider.Yoksis,
                ProviderWorkId = "first",
                Doi = "10.1234/first",
                SyncedAt = DateTime.UtcNow
            },
            new AcademicWork
            {
                PersonelId = personelId,
                Provider = AcademicWorkProvider.Yoksis,
                ProviderWorkId = "second",
                Doi = "10.1234/second",
                SyncedAt = DateTime.UtcNow
            });
        await database.SaveChangesAsync();

        using CancellationTokenSource cancellation = new();
        int requests = 0;
        using HttpClient http = new(new StubHttpHandler(request =>
        {
            requests++;
            if (requests == 2)
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }
            string doi = Uri.UnescapeDataString(request.RequestUri!.Segments[^1]);
            return StubHttpHandler.Json(
                """{"message":{"DOI":"DOI_VALUE","title":["Saved before cancellation"]}}"""
                    .Replace("DOI_VALUE", doi, StringComparison.Ordinal));
        }));
        IConfiguration configuration = Config("Crossref", "https://crossref.example");
        CrossrefEnrichmentService service = new(database, new CrossrefClient(http, configuration), configuration);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.EnrichAsync(personelId, cancellation.Token));

        Assert.Equal(2, requests);
        CrossrefWork saved = Assert.Single(await database.CrossrefWorks
            .Where(x => x.PersonelId == personelId).ToListAsync());
        Assert.Equal("Saved before cancellation", saved.Title);
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

    [Theory]
    [InlineData(false, "9999-0000-0000-0019", 1)]
    [InlineData(true, "9999-0000-0000-0027", 0)]
    [InlineData(true, "9999-0000-0000-0035", 1)]
    public async Task CollectAsync_TrDizinDoi_CrossrefSuccessOrOutage_PreservesSavedBaseWorkflow(
        bool crossrefFails, string orcid, int successfulCrossrefRequests)
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        string personelId = "trdizin-handler-" + Guid.NewGuid().ToString("N");
        Dictionary<string, string?> settings = new()
        {
            ["Orcid:ApiBaseUrl"] = "https://orcid.example",
            ["OpenAlex:ApiBaseUrl"] = "https://openalex.example",
            ["TrDizin:ApiBaseUrl"] = "https://trdizin.example",
            ["Crossref:ApiBaseUrl"] = "https://crossref.example",
            ["ProviderCache:MaxAgeHours"] = "24"
        };
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        int crossrefRequests = 0;
        using HttpClient http = new(new StubHttpHandler(request =>
        {
            string host = request.RequestUri!.Host;
            string path = request.RequestUri.AbsolutePath;
            if (host == "orcid.example")
            {
                return StubHttpHandler.Json(
                    """{"orcid-identifier":{"path":"ORCID_VALUE"},"person":{},"activities-summary":{"works":{"group":[]}}}"""
                        .Replace("ORCID_VALUE", orcid, StringComparison.Ordinal));
            }
            if (host == "openalex.example")
            {
                return new(HttpStatusCode.NotFound);
            }
            if (host == "trdizin.example" && path == "/api/public/yazar/orcid")
            {
                return StubHttpHandler.Json(
                    """{"id":42,"orcid":"ORCID_VALUE","fullName":"Ada Test","orderPublicationCount":1}"""
                        .Replace("ORCID_VALUE", orcid, StringComparison.Ordinal));
            }
            if (host == "trdizin.example" && path == "/api/authorPublicationsById/42")
            {
                return StubHttpHandler.Json(successfulCrossrefRequests > 0 && crossrefFails
                    ? """{"hits":{"total":{"value":2},"hits":[{"_id":"7","fields":{"id":["7"]}},{"_id":"8","fields":{"id":["8"]}}]}}"""
                    : """{"hits":{"total":{"value":1},"hits":[{"_id":"7","fields":{"id":["7"]}}]}}""");
            }
            if (host == "trdizin.example" && path == "/api/publicationById/7")
            {
                return StubHttpHandler.Json(
                        """{"hits":{"hits":[{"_source":{"orderTitle":"Source title","doi":"10.1234/handler","publicationYear":"2024"}}]}}""");
            }
            if (host == "trdizin.example" && path == "/api/publicationById/8")
            {
                return StubHttpHandler.Json(
                    """{"hits":{"hits":[{"_source":{"orderTitle":"Second source title","doi":"10.1234/second","publicationYear":"2023"}}]}}""");
            }
            if (host == "crossref.example")
            {
                crossrefRequests++;
                if (crossrefFails && crossrefRequests == successfulCrossrefRequests + 1)
                {
                    throw new HttpRequestException("Synthetic Crossref outage");
                }
                string responseDoi = path.Contains("second", StringComparison.Ordinal)
                    ? "10.1234/second" : "10.1234/handler";
                return StubHttpHandler.Json(
                    """{"message":{"DOI":"DOI_VALUE","title":["Crossref title"]}}"""
                        .Replace("DOI_VALUE", responseDoi, StringComparison.Ordinal));
            }
            throw new InvalidOperationException(request.RequestUri.ToString());
        }));

        AcademicWorkSynchronizer works = new(database);
        CrossrefEnrichmentService enrichment = new(database, new CrossrefClient(http, configuration), configuration);
        ResearcherCollectionService collection = new(
            new OrcidClient(http, configuration),
            new GoogleScholarClient(http, configuration),
            new OpenAlexClient(http, configuration),
            new WebOfScienceClient(http, configuration),
            new TrDizinClient(http, configuration),
            new(),
            new(),
            configuration);
        ResearcherCollectionHandler handler = new(
            new(),
            collection,
            new ResearcherRepository(database),
            works,
            new PublicationSummarySynchronizer(database),
            enrichment,
            database);
        AcademicPerformanceApplicationService application = new(handler, database,
            new ResearcherProviderInputNormalizer(new ResearcherIdentifierParser()));

        AcademicDataResponse response = await application.CollectAsync(new()
        {
            PersonelId = personelId,
            Orcid = orcid
        });

        Assert.True(response.IsSaved, string.Join(Environment.NewLine, response.Messages));
        Assert.NotNull(response.Researcher!.TrDizinProfile);
        int expectedPublications = crossrefFails && successfulCrossrefRequests > 0 ? 2 : 1;
        Assert.Equal(expectedPublications, response.PublicationCount);
        Assert.Equal(crossrefFails, response.Messages.Any(message => message.StartsWith("[HATA] Crossref")));

        var summary = await database.PublicationSummaries.SingleAsync(x =>
            x.PersonelId == personelId && x.Doi == "10.1234/handler");
        Assert.Contains("TrDizin", summary.Sources);
        Assert.Equal(!crossrefFails || successfulCrossrefRequests > 0,
            summary.Sources.Contains("Crossref", StringComparison.Ordinal));

        AcademicDataResponse retrieved = await application.GetResearcherAsync(new() { PersonelId = personelId });
        Assert.Equal(42, retrieved.Researcher!.TrDizinProfile!.AuthorId);
        Assert.Equal(expectedPublications, retrieved.PublicationCount);

        if (!crossrefFails)
        {
            AcademicDataResponse repeated = await application.CollectAsync(new()
            {
                PersonelId = personelId,
                Orcid = orcid
            });
            Assert.True(repeated.IsSaved);
            Assert.Equal(1, crossrefRequests);
            Assert.DoesNotContain(repeated.Messages, message => message.StartsWith("[HATA] Crossref"));
        }
        else if (successfulCrossrefRequests > 0)
        {
            AcademicDataResponse repeated = await application.CollectAsync(new()
            {
                PersonelId = personelId,
                Orcid = orcid
            });
            Assert.True(repeated.IsSaved);
            Assert.Equal(3, crossrefRequests);
            Assert.DoesNotContain(repeated.Messages, message => message.StartsWith("[HATA] Crossref"));
            Assert.All(await database.PublicationSummaries.Where(x => x.PersonelId == personelId).ToListAsync(),
                item => Assert.Contains("Crossref", item.Sources));
        }
    }

    private static IConfiguration Config(string provider, string url) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { [$"{provider}:ApiBaseUrl"] = url }).Build();
}
