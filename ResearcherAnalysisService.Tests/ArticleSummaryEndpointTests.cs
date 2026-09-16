using System.Net;
using System.Net.Http.Json;
using System.Text;
using AcademicCollector.Analysis.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Configuration;
using ResearcherAnalysisService.Products.Api.Contracts;
using ResearcherAnalysisService.Products.ArticleSummaries;
using ResearcherAnalysisService.Products.Data;
using ResearcherAnalysisService.SourceData.Researchers;
using ResearcherAnalysisService.SourceData.Works;
using ResearcherAnalysisService.Tests.Infrastructure;

namespace ResearcherAnalysisService.Tests;

[Collection("Analysis Product SQL Server")]
public sealed class ArticleSummaryEndpointTests(AnalysisProductSqlServerFixture fixture)
{
    private const string Api = "/api/v1/";

    [Fact]
    public async Task CanonicalAnalysis_AssociatedWithoutGeneratedArtifacts_ReturnsExplicitNullsAndValidatesInput()
    {
        SyntheticCanonicalSource source = await fixture.SeedCanonicalSourceAsync(
            NewPersonelId(), "Unproduced canonical article");
        CountingSummaryGenerator generator = new();
        await using EndpointHost host = await EndpointHost.StartAsync(
            fixture.ConnectionString, generator, new SupportingVerifier());

        using HttpResponseMessage response = await host.Client.PostAsJsonAsync(
            Api + "articles/analysis", new
            {
                PersonelID = source.PersonelId,
                source.CanonicalWorkId,
                Language = "tr",
                Skip = 0,
                Take = 25
            });
        response.EnsureSuccessStatusCode();
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"Status\":", body, StringComparison.Ordinal);
        Assert.Contains("\"Evidence\":null", body, StringComparison.Ordinal);
        Assert.Contains("\"Review\":null", body, StringComparison.Ordinal);
        Assert.Equal(0, generator.Calls);

        using HttpResponseMessage missing = await host.Client.PostAsJsonAsync(
            Api + "articles/analysis", new
            {
                PersonelID = NewPersonelId(),
                source.CanonicalWorkId,
                Language = "tr",
                Skip = 0,
                Take = 25
            });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        foreach (object invalid in new object[]
        {
            new { PersonelID = source.PersonelId, source.CanonicalWorkId, Language = "de", Skip = 0, Take = 25 },
            new { PersonelID = source.PersonelId, source.CanonicalWorkId, Language = "tr", Skip = -1, Take = 25 },
            new { PersonelID = source.PersonelId, source.CanonicalWorkId, Language = "tr", Skip = 0, Take = 201 }
        })
        {
            using HttpResponseMessage invalidResponse = await host.Client.PostAsJsonAsync(
                Api + "articles/analysis", invalid);
            Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        }
        Assert.Equal(0, generator.Calls);
    }

    [Fact]
    public async Task SummarizeArticle_AbstractSource_PersistsReadAndEvidenceAcrossRestartAndSharedResearcher()
    {
        SyntheticCanonicalSource source = await fixture.SeedCanonicalSourceAsync(
            NewPersonelId(), "Persistent synthetic article");
        CountingSummaryGenerator generator = new();

        await using (EndpointHost host = await EndpointHost.StartAsync(
            fixture.ConnectionString, generator, new SupportingVerifier()))
        {
            using HttpResponseMessage wrongOwner = await host.Client.PostAsJsonAsync(
                Api + "articles/summary/generate",
                new { PersonelID = NewPersonelId(), source.AcademicWorkId, Language = "tr" });
            Assert.Equal(HttpStatusCode.NotFound, wrongOwner.StatusCode);

            using HttpResponseMessage generated = await host.Client.PostAsJsonAsync(
                Api + "articles/summary/generate",
                new { PersonelID = source.PersonelId, source.AcademicWorkId, Language = "tr" });
            generated.EnsureSuccessStatusCode();
            SavedArticleSummaryResponse saved =
                (await generated.Content.ReadFromJsonAsync<SavedArticleSummaryResponse>())!;
            Assert.Equal("abstract", saved.Report.SourceKind);
            Assert.Equal("automatically_checked", saved.Report.Verification!.Status);

            using HttpResponseMessage read = await host.Client.PostAsJsonAsync(
                Api + "articles/summary",
                new { PersonelID = source.PersonelId, source.AcademicWorkId, Language = "tr" });
            read.EnsureSuccessStatusCode();
            Assert.Equal(saved.Id,
                (await read.Content.ReadFromJsonAsync<SavedArticleSummaryResponse>())!.Id);

            CanonicalArticleEvidenceResponse evidence = await ReadEvidenceAsync(
                host.Client, source.PersonelId, source.CanonicalWorkId, 0, 1);
            Assert.Equal(saved.Report.SourceHash, evidence.Source.ExtractedTextHash);
            Assert.Equal("DatabaseAbstract", evidence.Source.Origin);
            Assert.Equal("Methods", Assert.Single(evidence.Claims).Section);
            Assert.Equal(0, evidence.Skip);
            Assert.Equal(1, evidence.Take);
            Assert.Equal(1, evidence.TotalClaimCount);
            Assert.Equal(1, generator.Calls);

            string secondPersonelId = NewPersonelId();
            await using (DbContext seed = fixture.CreateSeedContext())
            {
                Researcher researcher = new() { PersonelId = secondPersonelId };
                seed.AddRange(researcher, new CanonicalResearcherWork
                {
                    CanonicalWorkId = source.CanonicalWorkId,
                    Researcher = researcher,
                    PersonelId = secondPersonelId,
                    LastObservedAt = DateTime.UtcNow
                });
                await seed.SaveChangesAsync();
            }
            CanonicalArticleEvidenceResponse shared = await ReadEvidenceAsync(
                host.Client, secondPersonelId, source.CanonicalWorkId);
            Assert.Equal(evidence.AnalysisRunId, shared.AnalysisRunId);
            Assert.Equal(1, generator.Calls);
        }

        await using EndpointHost restarted = await EndpointHost.StartAsync(
            fixture.ConnectionString, new CountingSummaryGenerator(), new SupportingVerifier());
        using HttpResponseMessage roundTrip = await restarted.Client.PostAsJsonAsync(
            Api + "articles/summary",
            new { PersonelID = source.PersonelId, source.AcademicWorkId, Language = "tr" });
        roundTrip.EnsureSuccessStatusCode();
        Assert.Equal("automatically_checked",
            (await roundTrip.Content.ReadFromJsonAsync<SavedArticleSummaryResponse>())!
            .Report.Verification!.Status);
    }

    [Fact]
    public async Task SummarizeAsync_HtmlExtractionAndFailedDownloadFallback_UseSyntheticSourcesOnly()
    {
        SyntheticCanonicalSource htmlSource = await fixture.SeedCanonicalSourceAsync(
            NewPersonelId(), "HTML source article");
        SyntheticCanonicalSource fallbackSource = await fixture.SeedCanonicalSourceAsync(
            NewPersonelId(), "Fallback source article");
        await using (DbContext seed = fixture.CreateSeedContext())
        {
            AcademicWork htmlWork = await seed.Set<AcademicWork>()
                .SingleAsync(value => value.Id == htmlSource.AcademicWorkId);
            htmlWork.FullTextUrl = "https://example.com/synthetic-article";
            AcademicWork fallbackWork = await seed.Set<AcademicWork>()
                .SingleAsync(value => value.Id == fallbackSource.AcademicWorkId);
            fallbackWork.FullTextUrl = "https://example.com/unavailable-article";
            fallbackWork.Abstract = "A controlled fallback abstract with enough evidence for the synthetic test.";
            await seed.SaveChangesAsync();
        }

        CountingSummaryGenerator generator = new();
        string paragraphs = string.Concat(Enumerable.Range(1, 16).Select(index =>
            $"<p>Section {index} describes a controlled method, source data, measured findings, limitations, and reproducible analysis with enough detail for independent assessment.</p>"));
        SyntheticSourceHandler sourceHandler = new(request =>
            request.RequestUri!.AbsolutePath == "/synthetic-article"
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $"<article><div itemprop='articleBody'><h2>Methods</h2>{paragraphs}</div></article>",
                        Encoding.UTF8, "text/html")
                }
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        IOptions<ArticleSummaryOptions> summaryOptions = Options.Create(new ArticleSummaryOptions
        {
            OcrEnabled = false
        });

        async Task<SavedArticleSummaryResponse> SummarizeAsync(SyntheticCanonicalSource current)
        {
            using IServiceScope scope = fixture.Services.CreateScope();
            AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
            SafeArticleFetcher fetcher = new(summaryOptions, (_, _) => Task.FromResult(
                new HttpClient(sourceHandler, disposeHandler: false)));
            ArticleSummarizer summarizer = new(generator, new SupportingVerifier(),
                Options.Create(new AiOptions()));
            ArticleSummaryWorkflow workflow = new(database, fetcher,
                new ArticlePdfExtractor(summaryOptions), new ArticleHtmlExtractor(summaryOptions),
                new ArticleSummaryServiceClient(summarizer), summaryOptions,
                scope.ServiceProvider.GetRequiredService<AnalysisSourceLock>());
            return (await workflow.SummarizeAsync(current.PersonelId,
                current.AcademicWorkId, "tr", default))!;
        }

        SavedArticleSummaryResponse html = await SummarizeAsync(htmlSource);
        SavedArticleSummaryResponse fallback = await SummarizeAsync(fallbackSource);

        Assert.Equal("html", html.Report.SourceKind);
        Assert.Equal("html", html.Report.ExtractionMethod);
        Assert.Equal("abstract", fallback.Report.SourceKind);
        Assert.Contains("unavailable", fallback.Report.Coverage.ScopeReason!,
            StringComparison.OrdinalIgnoreCase);
        Assert.True(sourceHandler.RequestCount >= 3);
        Assert.Equal(2, generator.Calls);
    }

    [Fact]
    public async Task SummarizeArticle_SourceIdentityChangesDuringGeneration_RejectsEntireGraph()
    {
        SyntheticCanonicalSource source = await fixture.SeedCanonicalSourceAsync(
            NewPersonelId(), "Changing identity article");
        BlockingSummaryGenerator generator = new();
        await using EndpointHost host = await EndpointHost.StartAsync(
            fixture.ConnectionString, generator, new SupportingVerifier());

        Task<HttpResponseMessage> pending = host.Client.PostAsJsonAsync(
            Api + "articles/summary/generate",
            new { PersonelID = source.PersonelId, source.AcademicWorkId, Language = "tr" });
        await generator.Received.Task.WaitAsync(TimeSpan.FromSeconds(15));

        string replacementDoi = "10.5555/changed-" + Guid.NewGuid().ToString("N");
        await using (DbContext seed = fixture.CreateSeedContext())
        {
            (await seed.Set<AcademicWork>().SingleAsync(value =>
                value.Id == source.AcademicWorkId)).Doi = replacementDoi;
            (await seed.Set<CanonicalWork>().SingleAsync(value =>
                value.Id == source.CanonicalWorkId)).NormalizedDoi = replacementDoi;
            (await seed.Set<CanonicalWorkObservation>().SingleAsync(value =>
                value.AcademicWorkId == source.AcademicWorkId)).DoiObserved = replacementDoi;
            await seed.SaveChangesAsync();
        }
        generator.Release.TrySetResult();

        using HttpResponseMessage response = await pending;
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using IServiceScope verifyScope = fixture.Services.CreateScope();
        AnalysisDbContext database = verifyScope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        Assert.False(await database.ArticleSummaries.AsNoTracking().AnyAsync(value =>
            value.PersonelId == source.PersonelId));
        Assert.False(await database.CanonicalArticleAnalysisRuns.AsNoTracking().AnyAsync(value =>
            value.CanonicalWorkId == source.CanonicalWorkId));
        Assert.False(await database.ArticleSourceSnapshots.AsNoTracking().AnyAsync(value =>
            value.CanonicalWorkId == source.CanonicalWorkId));
    }

    [Fact]
    public async Task SummarizeArticle_UnusableSavedSource_ReturnsSafeValidationMessage()
    {
        SyntheticCanonicalSource source = await fixture.SeedCanonicalSourceAsync(
            NewPersonelId(), "Unsafe source article");
        await using (DbContext seed = fixture.CreateSeedContext())
        {
            AcademicWork work = await seed.Set<AcademicWork>()
                .SingleAsync(value => value.Id == source.AcademicWorkId);
            work.FullTextUrl = "http://127.0.0.1/paper.pdf?token=secret";
            work.Abstract = null;
            await seed.SaveChangesAsync();
        }
        await using EndpointHost host = await EndpointHost.StartAsync(
            fixture.ConnectionString, new CountingSummaryGenerator(), new SupportingVerifier());

        using HttpResponseMessage response = await host.Client.PostAsJsonAsync(
            Api + "articles/summary/generate",
            new { PersonelID = source.PersonelId, source.AcademicWorkId, Language = "tr" });
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("not a permitted public HTTP(S) URL", body, StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1", body, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", body, StringComparison.Ordinal);
    }

    private static async Task<CanonicalArticleEvidenceResponse> ReadEvidenceAsync(
        HttpClient client, string personelId, int canonicalWorkId, int skip = 0, int take = 100)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            Api + "articles/analysis",
            new { PersonelID = personelId, CanonicalWorkId = canonicalWorkId, Language = "tr", Skip = skip, Take = take });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CanonicalArticleAnalysisResponse>())!.Evidence!;
    }

    private static string NewPersonelId() => "summary-test-" + Guid.NewGuid().ToString("N");

    private sealed class EndpointHost(WebApplication application, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public static async Task<EndpointHost> StartAsync(string connectionString,
            IArticleSummaryGenerator generator, IArticleClaimVerifier verifier)
        {
            WebApplication application = Program.CreateApplication(
                ["--environment", "Testing"], builder =>
                {
                    builder.Configuration.Sources.Clear();
                    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Urls"] = "http://127.0.0.1:0",
                        ["ConnectionStrings:UsageDatabase"] = connectionString,
                        ["DatabaseMigrations:Enabled"] = "false",
                        ["Service:ApiKey"] = "synthetic-service-key",
                        ["CollectionChanges:WorkerEnabled"] = "false",
                        ["ArticleSummaryAutomation:Enabled"] = "false",
                        ["ArticleSummaryAutomation:WorkerEnabled"] = "false",
                        ["PublicationMetrics:WorkerEnabled"] = "false",
                        ["ArticleEvaluation:WorkerEnabled"] = "false",
                        ["FacultyAssistant:WorkerEnabled"] = "false",
                        ["ArticleSummary:OcrEnabled"] = "false"
                    });
                    builder.Logging.ClearProviders();
                    builder.Services.RemoveAll<IArticleSummaryGenerator>();
                    builder.Services.RemoveAll<IArticleClaimVerifier>();
                    builder.Services.AddSingleton(generator);
                    builder.Services.AddSingleton(verifier);
                });
            await application.StartAsync();
            string address = application.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            HttpClient client = new() { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.Add("X-Analysis-Key", "synthetic-service-key");
            return new EndpointHost(application, client);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await application.StopAsync();
            await application.DisposeAsync();
        }
    }

    private class CountingSummaryGenerator : IArticleSummaryGenerator
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);

        public virtual Task<GeneratedArticleChunk> GenerateAsync(string language,
            string sourceKind, IReadOnlyList<ArticleSourceSpan> sourceSpans,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            ArticleSourceSpan source = sourceSpans.First(value =>
                !string.IsNullOrWhiteSpace(value.Text));
            GeneratedArticleClaim claim = new("claim-1", "A synthetic supported method claim.",
                [source.SourceId]);
            return Task.FromResult(new GeneratedArticleChunk(
                new([], [claim], [], [], []), "synthetic", "summary-v1"));
        }
    }

    private sealed class BlockingSummaryGenerator : CountingSummaryGenerator
    {
        public TaskCompletionSource Received { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task<GeneratedArticleChunk> GenerateAsync(string language,
            string sourceKind, IReadOnlyList<ArticleSourceSpan> sourceSpans,
            CancellationToken cancellationToken)
        {
            Received.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return await base.GenerateAsync(language, sourceKind, sourceSpans, cancellationToken);
        }
    }

    private sealed class SupportingVerifier : IArticleClaimVerifier
    {
        public Task<GeneratedVerificationBatch> VerifyAsync(string language,
            IReadOnlyList<GeneratedArticleClaim> claims,
            IReadOnlyList<ArticleSourceSpan> sourceSpans,
            CancellationToken cancellationToken) => Task.FromResult(new GeneratedVerificationBatch(
                claims.Select(value => new GeneratedClaimVerdict(
                    value.ClaimId, "supported", "Synthetic source supports the claim.")).ToArray(),
                "synthetic-verifier", "verify-v1"));
    }

    private sealed class SyntheticSourceHandler(
        Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        private int _requestCount;
        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            return Task.FromResult(response(request));
        }
    }
}
