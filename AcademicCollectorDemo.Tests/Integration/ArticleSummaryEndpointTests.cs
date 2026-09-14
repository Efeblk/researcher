using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AcademicCollector.Analysis.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Analysis;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Metrics;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;
using AcademicCollectorDemo.Tests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class ArticleSummaryEndpointTests(SqlServerFixture fixture)
{
    private const string Api = "/Services/AcademicPerformance/V1/";

    [Fact]
    public async Task SummarizeArticle_ValidReport_PersistsCanonicalEvidenceAndPreservesItAfterRefresh()
    {
        using var scope = fixture.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        Researcher owner = new() { PersonelId = "test-" + Guid.NewGuid().ToString("N"), FirstName = "Owner" };
        Researcher other = new() { PersonelId = "test-" + Guid.NewGuid().ToString("N"), FirstName = "Other" };
        database.Researchers.AddRange(owner, other);
        AcademicWork work = new()
        {
            PersonelId = owner.PersonelId,
            ProviderWorkId = Guid.NewGuid().ToString("N"),
            Provider = AcademicWorkProvider.OpenAlex,
            ProviderPayload = "{\"abstract_inverted_index\":{\"This\":[0],\"synthetic\":[1],\"abstract\":[2],\"describes\":[3],\"a\":[4],\"controlled\":[5],\"study\":[6],\"and\":[7],\"its\":[8],\"measured\":[9],\"finding.\":[10]}}",
            SyncedAt = DateTime.UtcNow
        };
        database.AcademicWorks.Add(work);
        await database.SaveChangesAsync();

        bool fail = false;
        bool invalidHash = false;
        bool invalidEvidence = false;
        int analysisCalls = 0;
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        await using var analysis = builder.Build();
        analysis.MapPost("/api/v1/articles/summarize", (SummarizeArticleRequest request) =>
        {
            analysisCalls++;
            if (fail) return Results.StatusCode(502);
            ArticleSourceSpan span = request.SourceSpans!.First(x => !string.IsNullOrWhiteSpace(x.Text));
            ArticleClaim Claim(string id, string text) => new(text,
                [new(span.Text, span.PageNumber)
                {
                    SourceId = span.SourceId,
                    StartOffset = span.StartOffset,
                    EndOffset = invalidEvidence ? span.EndOffset - 1 : span.EndOffset
                }]) { ClaimId = id };
            ArticleCoverage coverage = new(1, 1, 1, 1, 1, 0, true, request.ScopeReason)
            {
                CandidateClaims = 5,
                AutomaticallyCheckedClaims = 5,
                SupportedClaims = 5,
                UnsupportedClaims = 0,
                UncertainClaims = 0,
                DuplicateOrCappedClaims = 0,
                BudgetUnverifiedClaims = 0,
                OmissionReasons = []
            };
            ArticleSummaryReport report = new(request.Language, request.SourceKind,
                invalidHash ? "0".PadLeft(64, '0') : request.SourceHash, request.ExtractionVersion,
                coverage, new([Claim("p1", "Purpose")], [Claim("m1", "Methods")],
                    [Claim("d1", "Data")], [Claim("f1", "Findings")], [Claim("l1", "Limitations")]),
                "synthetic", "v1")
            {
                Verification = new("automatically_checked", "synthetic", "verify-v1", true,
                    "Synthetic test verifier."),
                SourceFidelity = ArticleSourceFidelity.Create(request.SourceKind, request.ExtractionVersion)
            };
            return Results.Json(report);
        });
        await analysis.StartAsync();
        var input = new { PersonelID = owner.PersonelId, AcademicWorkId = work.Id, Language = "tr" };
        int canonicalWorkId;
        using (var host = new HostProcess(fixture.ConnectionString, analysis.Urls.Single(),
            disablePublicationEnrichmentProviders: true))
        {
            host.Client.Timeout = TimeSpan.FromSeconds(30);
            await host.WaitUntilReadyAsync();
            using var wrongOwner = await host.Client.PostAsJsonAsync(Api + "SummarizeArticle",
                new { PersonelID = other.PersonelId, AcademicWorkId = work.Id });
            Assert.Equal(HttpStatusCode.NotFound, wrongOwner.StatusCode);

            using var generated = await host.Client.PostAsJsonAsync(Api + "SummarizeArticle", input);
            generated.EnsureSuccessStatusCode();
            SavedArticleSummaryResponse saved =
                (await generated.Content.ReadFromJsonAsync<SavedArticleSummaryResponse>())!;
            Assert.Equal("abstract", saved.Report.SourceKind);
            Assert.True(saved.Report.Coverage.IsPartial);
            Assert.Contains("abstract", saved.Report.Coverage.ScopeReason!, StringComparison.OrdinalIgnoreCase);
            PublicationMetricsRefreshState metricsState = await database
                .PublicationMetricsRefreshStates.AsNoTracking()
                .SingleAsync(value => value.PersonelId == owner.PersonelId);
            Assert.True(metricsState.RequestedRevision >= 2,
                "Canonical mapping and the saved recovered abstract must each invalidate metrics.");

            canonicalWorkId = await database.CanonicalWorkObservations.AsNoTracking()
                .Where(x => x.AcademicWorkId == work.Id)
                .Select(x => x.CanonicalWorkId)
                .SingleAsync();
            database.CanonicalResearcherWorks.Add(new()
            {
                CanonicalWorkId = canonicalWorkId,
                PersonelId = other.PersonelId,
                LastObservedAt = DateTime.UtcNow
            });
            await database.SaveChangesAsync();
            var evidenceRequest = new
            {
                PersonelID = owner.PersonelId,
                CanonicalWorkId = canonicalWorkId,
                Language = "tr",
                Skip = 0,
                Take = 100
            };
            using var evidenceResponse = await host.Client.PostAsJsonAsync(
                Api + "GetCanonicalArticleEvidence", evidenceRequest);
            Assert.True(evidenceResponse.IsSuccessStatusCode,
                await evidenceResponse.Content.ReadAsStringAsync());
            CanonicalArticleEvidenceResponse evidence =
                (await evidenceResponse.Content.ReadFromJsonAsync<CanonicalArticleEvidenceResponse>())!;
            Assert.Equal(5, evidence.TotalClaimCount);
            Assert.Equal(["Purpose", "Methods", "Data", "Findings", "Limitations"],
                evidence.Claims.Select(x => x.Section));
            Assert.All(evidence.Claims.SelectMany(x => x.Evidence), item =>
            {
                Assert.StartsWith("src-", item.SourceId);
                Assert.Equal(item.Quote.Length, item.EndOffset - item.StartOffset);
            });
            Assert.Equal(saved.Report.SourceHash, evidence.Source.ExtractedTextHash);
            Assert.Equal("DatabaseAbstract", evidence.Source.Origin);

            using var sharedRead = await host.Client.PostAsJsonAsync(Api + "GetCanonicalArticleEvidence",
                new { PersonelID = other.PersonelId, CanonicalWorkId = canonicalWorkId, Language = "tr" });
            sharedRead.EnsureSuccessStatusCode();
            using var repeatedRead = await host.Client.PostAsJsonAsync(
                Api + "GetCanonicalArticleEvidence", evidenceRequest);
            repeatedRead.EnsureSuccessStatusCode();
            Assert.Equal(1, analysisCalls);

            using var englishGenerated = await host.Client.PostAsJsonAsync(Api + "SummarizeArticle",
                new { PersonelID = owner.PersonelId, AcademicWorkId = work.Id, Language = "en" });
            englishGenerated.EnsureSuccessStatusCode();
            using var englishRead = await host.Client.PostAsJsonAsync(Api + "GetCanonicalArticleEvidence",
                new { PersonelID = owner.PersonelId, CanonicalWorkId = canonicalWorkId, Language = "en" });
            englishRead.EnsureSuccessStatusCode();
            CanonicalArticleEvidenceResponse englishEvidence =
                (await englishRead.Content.ReadFromJsonAsync<CanonicalArticleEvidenceResponse>())!;
            Assert.Equal("en", englishEvidence.Language);
            Assert.NotEqual(evidence.AnalysisRunId, englishEvidence.AnalysisRunId);
            Assert.Equal(1, await database.ArticleSourceSnapshots.CountAsync(
                x => x.CanonicalWorkId == canonicalWorkId));
            Assert.Equal(2, analysisCalls);

            invalidEvidence = true;
            using var invalidCoordinates = await host.Client.PostAsJsonAsync(Api + "SummarizeArticle", input);
            Assert.Equal(HttpStatusCode.BadGateway, invalidCoordinates.StatusCode);
            invalidEvidence = false;
            invalidHash = true;
            using var invalidReport = await host.Client.PostAsJsonAsync(Api + "SummarizeArticle", input);
            Assert.Equal(HttpStatusCode.BadGateway, invalidReport.StatusCode);
            invalidHash = false;
            fail = true;
            using var failed = await host.Client.PostAsJsonAsync(Api + "SummarizeArticle", input);
            Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
            Assert.Equal(2, await database.ArticleSummaries.CountAsync(x => x.PersonelId == owner.PersonelId));
            Assert.Equal(2, await database.CanonicalArticleAnalysisRuns.CountAsync(
                x => x.CanonicalWorkId == canonicalWorkId));

            database.AcademicWorks.Remove(work);
            await database.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<CanonicalWorkSynchronizer>()
                .SyncAsync(owner.PersonelId);
            Assert.All(await database.ArticleSummaries.Where(
                x => x.PersonelId == owner.PersonelId).ToListAsync(),
                summary => Assert.Null(summary.AcademicWorkId));
            Assert.Equal(2, await database.CanonicalArticleAnalysisRuns.CountAsync(
                x => x.CanonicalWorkId == canonicalWorkId));
            Assert.Equal(1, await database.ArticleSourceSnapshots.CountAsync(
                x => x.CanonicalWorkId == canonicalWorkId));
            Assert.Empty(await database.CanonicalArticleClaimEvidence.AsNoTracking()
                .Where(item => item.CanonicalArticleClaim!.CanonicalArticleAnalysisRunId != 0 &&
                    item.ArticleSourceSpan!.ArticleSourceSnapshotId !=
                    item.CanonicalArticleClaim.CanonicalArticleAnalysisRun!.ArticleSourceSnapshotId)
                .ToListAsync());
            using var formerOwnerRead = await host.Client.PostAsJsonAsync(
                Api + "GetCanonicalArticleEvidence", evidenceRequest);
            Assert.Equal(HttpStatusCode.NotFound, formerOwnerRead.StatusCode);
            using var retainedSharedRead = await host.Client.PostAsJsonAsync(Api + "GetCanonicalArticleEvidence",
                new { PersonelID = other.PersonelId, CanonicalWorkId = canonicalWorkId, Language = "tr" });
            retainedSharedRead.EnsureSuccessStatusCode();
        }
        await analysis.StopAsync();

        using var restarted = new HostProcess(fixture.ConnectionString, "http://127.0.0.1:1/");
        await restarted.WaitUntilReadyAsync();
        using var retrieved = await restarted.Client.PostAsJsonAsync(Api + "GetArticleSummary", input);
        retrieved.EnsureSuccessStatusCode();
        SavedArticleSummaryResponse roundTrip =
            (await retrieved.Content.ReadFromJsonAsync<SavedArticleSummaryResponse>())!;
        Assert.Equal("automatically_checked", roundTrip.Report.Verification!.Status);
        Assert.Equal(5, roundTrip.Report.Coverage.SupportedClaims);
    }

    [Fact]
    public async Task SummarizeArticle_IdentityChangesDuringModelCall_RejectsEntireNewGraph()
    {
        using (var seedScope = fixture.Services.CreateScope())
        {
            var database = seedScope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            Researcher researcher = new()
            {
                PersonelId = "test-" + Guid.NewGuid().ToString("N"),
                FirstName = "Changing"
            };
            database.Researchers.Add(researcher);
            database.AcademicWorks.Add(new()
            {
                PersonelId = researcher.PersonelId,
                Provider = AcademicWorkProvider.OpenAlex,
                ProviderWorkId = "old-provider-id",
                Abstract = "A synthetic immutable input with sufficient detail for an evidence claim.",
                SyncedAt = DateTime.UtcNow
            });
            await database.SaveChangesAsync();
        }

        string personelId;
        int workId;
        using (var readScope = fixture.Services.CreateScope())
        {
            var database = readScope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            AcademicWork work = await database.AcademicWorks.AsNoTracking()
                .OrderByDescending(x => x.Id).FirstAsync();
            personelId = work.PersonelId;
            workId = work.Id;
            Assert.False(await database.CanonicalWorkObservations.AsNoTracking()
                .AnyAsync(x => x.AcademicWorkId == workId));
        }

        TaskCompletionSource<SummarizeArticleRequest> received =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        await using var analysis = builder.Build();
        analysis.MapPost("/api/v1/articles/summarize", async (SummarizeArticleRequest request) =>
        {
            received.TrySetResult(request);
            await release.Task;
            ArticleSourceSpan span = request.SourceSpans!.Single();
            ArticleClaim claim = new("Supported claim", [new(span.Text, span.PageNumber)
            {
                SourceId = span.SourceId,
                StartOffset = span.StartOffset,
                EndOffset = span.EndOffset
            }]);
            ArticleCoverage coverage = new(1, 1, 1, 1, 1, 0, true, request.ScopeReason)
            {
                CandidateClaims = 1,
                AutomaticallyCheckedClaims = 1,
                SupportedClaims = 1,
                UnsupportedClaims = 0,
                UncertainClaims = 0,
                DuplicateOrCappedClaims = 0,
                BudgetUnverifiedClaims = 0,
                OmissionReasons = []
            };
            return Results.Json(new ArticleSummaryReport(request.Language, request.SourceKind,
                request.SourceHash, request.ExtractionVersion, coverage,
                new([claim], [], [], [], []), "synthetic", "v1")
            {
                Verification = new("automatically_checked", "synthetic", "verify-v1", true, null),
                SourceFidelity = ArticleSourceFidelity.Create(request.SourceKind, request.ExtractionVersion)
            });
        });
        await analysis.StartAsync();

        using var host = new HostProcess(fixture.ConnectionString, analysis.Urls.Single(),
            disablePublicationEnrichmentProviders: true);
        host.Client.Timeout = TimeSpan.FromSeconds(30);
        await host.WaitUntilReadyAsync();
        Task<HttpResponseMessage> pending = host.Client.PostAsJsonAsync(Api + "SummarizeArticle",
            new { PersonelID = personelId, AcademicWorkId = workId, Language = "tr" });
        await received.Task.WaitAsync(TimeSpan.FromSeconds(15));
        int originalCanonicalWorkId;
        using (var originalScope = fixture.Services.CreateScope())
        {
            var database = originalScope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            originalCanonicalWorkId = await database.CanonicalWorkObservations.AsNoTracking()
                .Where(x => x.AcademicWorkId == workId)
                .Select(x => x.CanonicalWorkId)
                .SingleAsync();
        }

        using (var mutationScope = fixture.Services.CreateScope())
        {
            var database = mutationScope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            var synchronizer = mutationScope.ServiceProvider.GetRequiredService<CanonicalWorkSynchronizer>();
            await using var transaction = await database.Database.BeginTransactionAsync();
            await synchronizer.AcquireWriteGateAsync();
            AcademicWork current = await database.AcademicWorks.SingleAsync(x => x.Id == workId);
            current.Doi = "10.5555/new-" + Guid.NewGuid().ToString("N");
            await database.SaveChangesAsync();
            await synchronizer.SyncAsync(personelId);
            await transaction.CommitAsync();
        }
        release.TrySetResult();

        using HttpResponseMessage response = await pending;
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using (var verifyScope = fixture.Services.CreateScope())
        {
            var database = verifyScope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            Assert.False(await database.ArticleSummaries.AsNoTracking()
                .AnyAsync(x => x.PersonelId == personelId));
            Assert.False(await database.CanonicalArticleAnalysisRuns.AsNoTracking()
                .AnyAsync(x => x.CanonicalWorkId == originalCanonicalWorkId));
            Assert.False(await database.ArticleSourceSnapshots.AsNoTracking()
                .AnyAsync(x => x.CanonicalWorkId == originalCanonicalWorkId));
        }
        await analysis.StopAsync();
    }

    [Fact]
    public async Task SummarizeAsync_SameDoiResearchers_SharesCanonicalEvidenceWithoutExternalCalls()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        Researcher first = new() { PersonelId = "test-" + Guid.NewGuid().ToString("N"), FirstName = "First" };
        Researcher second = new() { PersonelId = "test-" + Guid.NewGuid().ToString("N"), FirstName = "Second" };
        string doi = "10.8000/shared-" + Guid.NewGuid().ToString("N");
        AcademicWork firstWork = new()
        {
            PersonelId = first.PersonelId,
            Provider = AcademicWorkProvider.OpenAlex,
            ProviderWorkId = Guid.NewGuid().ToString("N"),
            Doi = doi,
            SyncedAt = DateTime.UtcNow
        };
        AcademicWork secondWork = new()
        {
            PersonelId = second.PersonelId,
            Provider = AcademicWorkProvider.WebOfScience,
            ProviderWorkId = Guid.NewGuid().ToString("N"),
            Doi = "https://doi.org/" + doi.ToUpperInvariant(),
            SyncedAt = DateTime.UtcNow
        };
        database.AddRange(first, second, firstWork, secondWork);
        await database.SaveChangesAsync();
        CanonicalWorkSynchronizer synchronizer =
            scope.ServiceProvider.GetRequiredService<CanonicalWorkSynchronizer>();
        await synchronizer.SyncAsync(first.PersonelId);
        await synchronizer.SyncAsync(second.PersonelId);

        string paragraphs = string.Join("", Enumerable.Range(1, 12).Select(index =>
            $"<p>Section {index} describes controlled methods, source data, measured findings, limitations, and reproducible analysis in enough detail for independent assessment.</p>"));
        string html = $"<article><div itemprop='articleBody'><h2>Methods</h2>{paragraphs}</div></article>";
        IOptions<ArticleSummaryOptions> summaryOptions = Options.Create(new ArticleSummaryOptions());
        int sourceRequests = 0;
        SafeArticleFetcher fetcher = new(summaryOptions, (_, _) =>
        {
            sourceRequests++;
            return Task.FromResult(new HttpClient(new StubHttpHandler(_ => new(HttpStatusCode.OK)
            {
                Content = new StringContent(html, Encoding.UTF8, "text/html")
            })));
        });
        var analysisHandler = new StubHttpHandler(message =>
        {
            SummarizeArticleRequest request = message.Content!
                .ReadFromJsonAsync<SummarizeArticleRequest>().GetAwaiter().GetResult()!;
            ArticleSourceSpan span = request.SourceSpans!.First();
            ArticleClaim claim = new("Shared DOI evidence", [new(span.Text, span.PageNumber)
            {
                SourceId = span.SourceId,
                StartOffset = span.StartOffset,
                EndOffset = span.EndOffset
            }]);
            ArticleCoverage coverage = new(1, 1, 1, 1, 1, 0, false, null)
            {
                CandidateClaims = 1,
                AutomaticallyCheckedClaims = 1,
                SupportedClaims = 1,
                UnsupportedClaims = 0,
                UncertainClaims = 0,
                DuplicateOrCappedClaims = 0,
                BudgetUnverifiedClaims = 0,
                OmissionReasons = []
            };
            ArticleSummaryReport report = new(request.Language, request.SourceKind, request.SourceHash,
                request.ExtractionVersion, coverage, new([], [], [], [claim], []), "synthetic", "v1")
            {
                Verification = new("automatically_checked", "synthetic", "verify-v1", true, null),
                SourceFidelity = ArticleSourceFidelity.Create(request.SourceKind, request.ExtractionVersion)
            };
            return StubHttpHandler.Json(JsonSerializer.Serialize(report));
        });
        using HttpClient analysisHttp = new(analysisHandler) { BaseAddress = new Uri("http://127.0.0.1/") };
        ArticleSummaryWorkflow workflow = new(database, fetcher, new ArticlePdfExtractor(summaryOptions),
            new ArticleHtmlExtractor(summaryOptions), null!,
            new ArticleSummaryServiceClient(analysisHttp, Options.Create(new AnalysisServiceOptions())),
            summaryOptions, synchronizer);

        SavedArticleSummaryResponse? saved = await workflow.SummarizeAsync(
            first.PersonelId, firstWork.Id, "tr", CancellationToken.None);

        Assert.NotNull(saved);
        Assert.Equal(1, sourceRequests);
        Assert.Equal(1, analysisHandler.RequestCount);
        int canonicalWorkId = await database.CanonicalWorkObservations.AsNoTracking()
            .Where(x => x.AcademicWorkId == firstWork.Id)
            .Select(x => x.CanonicalWorkId)
            .SingleAsync();
        using var host = new HostProcess(fixture.ConnectionString, "http://127.0.0.1:1/");
        await host.WaitUntilReadyAsync();
        using HttpResponseMessage response = await host.Client.PostAsJsonAsync(
            Api + "GetCanonicalArticleEvidence",
            new { PersonelID = second.PersonelId, CanonicalWorkId = canonicalWorkId, Language = "tr" });
        response.EnsureSuccessStatusCode();
        CanonicalArticleEvidenceResponse evidence =
            (await response.Content.ReadFromJsonAsync<CanonicalArticleEvidenceResponse>())!;
        Assert.Equal("html", evidence.Source.SourceKind);
        Assert.Equal("Doi", evidence.Source.Origin);
        Assert.Equal("Findings", Assert.Single(evidence.Claims).Section);
    }

    [Fact]
    public async Task SummarizeArticle_UnusableSavedSource_ReportsColumnAndSafeCause()
    {
        using var scope = fixture.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        Researcher owner = new() { PersonelId = "test-" + Guid.NewGuid().ToString("N"), FirstName = "Owner" };
        database.Researchers.Add(owner);
        AcademicWork work = new()
        {
            PersonelId = owner.PersonelId, ProviderWorkId = Guid.NewGuid().ToString("N"),
            FullTextUrl = "http://127.0.0.1/paper.pdf?token=secret", SyncedAt = DateTime.UtcNow
        };
        database.AcademicWorks.Add(work);
        await database.SaveChangesAsync();

        using var host = new HostProcess(fixture.ConnectionString, "http://127.0.0.1:1/");
        await host.WaitUntilReadyAsync();
        using var response = await host.Client.PostAsJsonAsync(Api + "SummarizeArticle",
            new { PersonelID = owner.PersonelId, AcademicWorkId = work.Id, Language = "tr" });
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("FullTextUrl: The saved article URL is not a permitted public HTTP(S) URL.", body);
        Assert.DoesNotContain("127.0.0.1", body);
        Assert.DoesNotContain("secret", body);
    }
}
