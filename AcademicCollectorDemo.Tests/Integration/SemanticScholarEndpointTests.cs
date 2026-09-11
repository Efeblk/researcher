using System.Net.Http.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.SemanticScholar;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using AcademicCollectorDemo.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Text;

namespace AcademicCollectorDemo.Tests.Integration;
[Collection("SQL Server")]
public sealed class SemanticScholarEndpointTests(SqlServerFixture fixture)
{
    [Fact] public async Task Enrich_SecondPage429_NextCallResumesAndCompletes()
    {
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        string id = "resume-" + Guid.NewGuid().ToString("N"); db.Researchers.Add(new Researcher { PersonelId = id });
        db.AcademicWorks.Add(new AcademicWork { PersonelId = id, ProviderWorkId = Guid.NewGuid().ToString("N"), Doi = "10.1/resume", SyncedAt = DateTime.UtcNow });
        SemanticScholarPaper old = new() { NormalizedDoi = "10.1/resume", PaperId = "target", Found = true,
            FetchedAt = DateTime.UtcNow.AddYears(-1), CitationsComplete = true, CitationsFetched = 1 };
        old.Citations.Add(new() { CitingPaperId = "obsolete", RefreshGeneration = "old",
            Contexts = [new() { Ordinal = 0, Context = "prior complete context" }] });
        db.SemanticScholarPapers.Add(old);
        await db.SaveChangesAsync();
        QueueHandler handler = new(
            Json("""{"paperId":"target","externalIds":{"DOI":"10.1/resume"},"title":"Target"}"""),
            Json("""{"total":2,"next":1,"data":[{"contextsWithIntent":[{"context":"old","intents":[]}],"citingPaper":{"paperId":"c1"}}]}"""),
            new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            Json("""{"total":2,"data":[{"contextsWithIntent":[{"context":"new","intents":["background"]}],"citingPaper":{"paperId":"c2"}}]}"""));
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:AcademicDatabase"] = fixture.ConnectionString, ["ProviderRequestLimits:SemanticScholar:Enabled"] = "true" }).Build();
        SemanticScholarOptions options = new() { ApiBaseUrl = "https://example.test", CitationPageSize = 1, MaximumCitationsPerPaper = 10 };
        SemanticScholarEnrichmentService service = new(db,
            new SemanticScholarClient(new HttpClient(handler), Options.Create(options)), config, Options.Create(options));
        await Assert.ThrowsAsync<HttpRequestException>(() => service.EnrichAsync(id));
        db.ChangeTracker.Clear();
        SemanticScholarPaper partial = await db.SemanticScholarPapers.SingleAsync(x => x.NormalizedDoi == "10.1/resume");
        Assert.False(partial.CitationsComplete); Assert.Equal(1, partial.CitationNextOffset); Assert.NotNull(partial.RefreshGeneration);
        Assert.Equal(2, await db.SemanticScholarCitations.CountAsync(x => x.TargetPaperId == partial.Id));
        Assert.True(await db.SemanticScholarCitations.AnyAsync(x => x.TargetPaperId == partial.Id && x.CitingPaperId == "obsolete"));
        Assert.Equal(1, await service.EnrichAsync(id)); db.ChangeTracker.Clear();
        SemanticScholarPaper complete = await db.SemanticScholarPapers.SingleAsync(x => x.NormalizedDoi == "10.1/resume");
        Assert.True(complete.CitationsComplete); Assert.Null(complete.RefreshGeneration);
        Assert.Equal(2, await db.SemanticScholarCitations.CountAsync(x => x.TargetPaperId == complete.Id));
        Assert.False(await db.SemanticScholarCitations.AnyAsync(x => x.TargetPaperId == complete.Id && x.CitingPaperId == "obsolete"));
        Assert.Contains("offset=1", handler.Urls.Last());
    }

    [Fact] public async Task ListCitations_OwnedWork_ReturnsRelationship()
    {
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        string id = "s2-" + Guid.NewGuid().ToString("N"); db.Researchers.Add(new Researcher { PersonelId = id });
        AcademicWork work = new() { PersonelId = id, ProviderWorkId = Guid.NewGuid().ToString("N"), Doi = "10.1/x", SyncedAt = DateTime.UtcNow }; db.AcademicWorks.Add(work);
        SemanticScholarPaper paper = new() { NormalizedDoi = "10.1/x", PaperId = "p", Found = true, FetchedAt = DateTime.UtcNow, CitationsComplete = true };
        paper.Citations.Add(new() { CitingPaperId = "c", RefreshGeneration = "g", Contexts = [new() { Context = "ctx", Ordinal = 0 }] }); db.SemanticScholarPapers.Add(paper);
        await db.SaveChangesAsync();
        using HostProcess host = new(fixture.ConnectionString); await host.WaitUntilReadyAsync();
        using var collected = await host.Client.PostAsJsonAsync("/Services/AcademicPerformance/V1/CollectSemanticScholar", new { PersonelID = id });
        collected.EnsureSuccessStatusCode();
        using var wrongOwner = await host.Client.PostAsJsonAsync("/Services/AcademicPerformance/V1/ListSemanticScholarCitations",
            new { PersonelID = "someone-else", AcademicWorkId = work.Id, Skip = 0, Take = 10 });
        Assert.Equal(System.Net.HttpStatusCode.NotFound, wrongOwner.StatusCode);
        using var response = await host.Client.PostAsJsonAsync("/Services/AcademicPerformance/V1/ListSemanticScholarCitations", new { PersonelID = id, AcademicWorkId = work.Id, Skip = 0, Take = 10 });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var result = (await response.Content.ReadFromJsonAsync<SemanticScholarPaperDto>())!; Assert.Equal("ctx", result.Citations.Single().Contexts.Single().Context);
    }

    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    private sealed class QueueHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        public List<string> Urls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Urls.Add(request.RequestUri!.ToString()); return Task.FromResult(_responses.Dequeue()); }
    }
}
