using System.Net;
using System.Net.Http.Json;
using AcademicCollector.Analysis.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using AcademicCollectorDemo.Tests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class ArticleSummaryEndpointTests(SqlServerFixture fixture)
{
    private const string Api = "/Services/AcademicPerformance/V1/";

    [Fact]
    public async Task SummarizeArticle_AbstractFallback_PersistsRetrievesAndChecksOwnership()
    {
        using var scope = fixture.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        Researcher owner = new() { PersonelId = "test-" + Guid.NewGuid().ToString("N"), FirstName = "Owner" };
        Researcher other = new() { PersonelId = "test-" + Guid.NewGuid().ToString("N"), FirstName = "Other" };
        database.Researchers.AddRange(owner, other);
        AcademicWork work = new()
        {
            PersonelId = owner.PersonelId, ProviderWorkId = Guid.NewGuid().ToString("N"),
            Abstract = "This synthetic\r\nabstract\tdescribes\u00a0a controlled study and its measured finding.", SyncedAt = DateTime.UtcNow
        };
        database.AcademicWorks.Add(work);
        await database.SaveChangesAsync();

        bool fail = false;
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Logging.ClearProviders();
        await using var analysis = builder.Build();
        analysis.MapPost("/api/v1/articles/summarize", (SummarizeArticleRequest request) =>
        {
            if (fail) return Results.StatusCode(502);
            ArticleClaim claim = new("Sentetik çalışma.", [new("This synthetic abstract describes a controlled study", null)]);
            ArticleSourceSpan span = request.SourceSpans!.First(x => !string.IsNullOrWhiteSpace(x.Text));
            ArticleClaim resolvedClaim = new("Sentetik \u00e7al\u0131\u015fma.",
                [new(span.Text, null) { SourceId = span.SourceId, StartOffset = span.StartOffset, EndOffset = span.EndOffset }])
                { ClaimId = "chunk-1:c1" };
            ArticleCoverage coverage = new(1, 1, 1, 1, 1, 0, true, request.ScopeReason)
            {
                CandidateClaims = 1, AutomaticallyCheckedClaims = 1, SupportedClaims = 1,
                UnsupportedClaims = 0, UncertainClaims = 0, DuplicateOrCappedClaims = 0,
                BudgetUnverifiedClaims = 0, OmissionReasons = []
            };
            return Results.Json(new ArticleSummaryReport(request.Language, request.SourceKind, request.SourceHash, request.ExtractionVersion,
                coverage, new([resolvedClaim], [], [], [], []), "synthetic", "v1")
                { Verification = new("automatically_checked", "synthetic", "verify-v1", true, "Synthetic test verifier.") });
        });
        await analysis.StartAsync();
        var input = new { PersonelID = owner.PersonelId, AcademicWorkId = work.Id, Language = "tr" };
        using (var host = new HostProcess(fixture.ConnectionString, analysis.Urls.Single()))
        {
            await host.WaitUntilReadyAsync();
            using var wrongOwner = await host.Client.PostAsJsonAsync(Api + "SummarizeArticle", new { PersonelID = other.PersonelId, AcademicWorkId = work.Id });
            Assert.Equal(HttpStatusCode.NotFound, wrongOwner.StatusCode);
            using var generated = await host.Client.PostAsJsonAsync(Api + "SummarizeArticle", input);
            generated.EnsureSuccessStatusCode();
            SavedArticleSummaryResponse saved = (await generated.Content.ReadFromJsonAsync<SavedArticleSummaryResponse>())!;
            Assert.Equal("abstract", saved.Report.SourceKind);
            Assert.True(saved.Report.Coverage.IsPartial);
            Assert.Contains("No saved full-text URL exists", saved.Report.Coverage.ScopeReason);
            fail = true;
            using var failed = await host.Client.PostAsJsonAsync(Api + "SummarizeArticle", input);
            Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
            Assert.Equal(1, await database.ArticleSummaries.CountAsync(x => x.PersonelId == owner.PersonelId));
        }
        await analysis.StopAsync();
        using var restarted = new HostProcess(fixture.ConnectionString, "http://127.0.0.1:1/");
        await restarted.WaitUntilReadyAsync();
        using var retrieved = await restarted.Client.PostAsJsonAsync(Api + "GetArticleSummary", input);
        retrieved.EnsureSuccessStatusCode();
        SavedArticleSummaryResponse roundTrip = (await retrieved.Content.ReadFromJsonAsync<SavedArticleSummaryResponse>())!;
        Assert.Equal("automatically_checked", roundTrip.Report.Verification!.Status);
        Assert.Equal(1, roundTrip.Report.Coverage.SupportedClaims);
        database.AcademicWorks.Remove(work);
        await database.SaveChangesAsync();
        Assert.Null((await database.ArticleSummaries.SingleAsync(x => x.PersonelId == owner.PersonelId)).AcademicWorkId);
    }
}
