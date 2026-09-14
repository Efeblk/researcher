using System.Security.Claims;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AcademicCollector.Analysis.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Configuration;
using ResearcherAnalysisService.Integrations.Gemini;
using ResearcherAnalysisService.Products.ArticleSummaries;
using ResearcherAnalysisService.Products.Data;
using ResearcherAnalysisService.Products.Evaluations;
using ResearcherAnalysisService.Products.ProductAccess;
using ResearcherAnalysisService.SourceData.Works;
using ResearcherAnalysisService.Tests.Infrastructure;
using AnalysisEvaluationOptions = ResearcherAnalysisService.Configuration.ArticleEvaluationOptions;
using ProductEvaluationOptions = ResearcherAnalysisService.Products.Evaluations.ArticleEvaluationOptions;

namespace ResearcherAnalysisService.Tests;

[Collection("Analysis Product SQL Server")]
public sealed class ArticleEvaluationProcessorTests(AnalysisProductSqlServerFixture fixture)
{
    [Fact]
    public async Task Enqueue_RealSourceExceedsSelectedProfileLimit_RejectsWithoutQueueing()
    {
        SyntheticCanonicalSource source = await fixture.SeedCanonicalSourceAsync(
            "evaluation-limit-" + Guid.NewGuid().ToString("N"));
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        await AddLargeAnalysisRunAsync(database, source);
        EvaluationHarness harness = EvaluationClient(new StubHandler((_, _) =>
            throw new InvalidOperationException("Input bounds must be enforced before execution.")), 4096);
        ArticleEvaluationScheduler scheduler = new(database, harness.Client,
            Options.Create(new ResearcherAnalysisService.Products.Evaluations.ArticleEvaluationOptions()));
        int before = await database.ArticleEvaluationRuns.CountAsync();

        await Assert.ThrowsAsync<ArticleEvaluationValidationException>(() => scheduler.EnqueueAsync(
            new("grant", "actor", source.PersonelId, AcademicProductOperation.ArticleEvaluationStart),
            new()
            {
                ProfileIds = [harness.Profile.ProfileId],
                PersonelId = source.PersonelId,
                RealCases = [new() { CanonicalWorkId = source.CanonicalWorkId, Language = "en" }]
            }, default));

        Assert.Equal(before, await database.ArticleEvaluationRuns.CountAsync());
    }

    [Fact]
    public async Task ProcessNext_ProfileFingerprintChanged_FailsBeforeExecute()
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        int providerCalls = 0;
        EvaluationHarness harness = EvaluationClient(new StubHandler((_, _) =>
        {
            providerCalls++;
            throw new InvalidOperationException("Profile drift must be rejected before execution.");
        }));
        ArticleEvaluationProfile stale = harness.Profile with
        {
            SettingsFingerprint = new string('0', 64)
        };
        ArticleEvaluationWorkItem item = await SeedCalibrationAsync(database, stale);

        Assert.True(await Processor(scope, database, new AllowingAccess(), harness.Client)
            .ProcessNextAsync(default));

        database.ChangeTracker.Clear();
        ArticleEvaluationWorkItem saved = await database.ArticleEvaluationWorkItems
            .Include(value => value.Attempts).SingleAsync(value => value.Id == item.Id);
        Assert.Equal(ArticleEvaluationStatus.Failed, saved.Status);
        Assert.Equal("ProfileDrift", saved.OutcomeCode);
        Assert.Equal("Unknown", Assert.Single(saved.Attempts).CostStatus);
        Assert.Equal(0, providerCalls);
    }

    [Fact]
    public async Task ProcessNext_AuthorizationRevoked_FailsBeforeProviderUse()
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        ArticleEvaluationWorkItem item = await SeedCalibrationAsync(database);

        Assert.True(await Processor(scope, database, new DenyingAccess()).ProcessNextAsync(default));

        database.ChangeTracker.Clear();
        ArticleEvaluationWorkItem saved = await database.ArticleEvaluationWorkItems
            .SingleAsync(value => value.Id == item.Id);
        Assert.Equal(ArticleEvaluationStatus.Failed, saved.Status);
        Assert.Equal("AccessRevoked", saved.OutcomeCode);
    }

    [Fact]
    public async Task ProcessNext_RealCaseAssociationMissing_FailsBeforeProviderUse()
    {
        int canonicalWorkId;
        await using (DbContext source = fixture.CreateSeedContext())
        {
            CanonicalWork work = new()
            {
                SourceScopedKey = Guid.NewGuid().ToString("N").PadRight(64, '0'),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            source.Add(work);
            await source.SaveChangesAsync();
            canonicalWorkId = work.Id;
        }
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        ArticleEvaluationRun run = Run("missing-owner", true);
        ArticleEvaluationCase evaluationCase = Case(run, "real", ArticleEvaluationTaskKinds.Review);
        evaluationCase.CanonicalWorkId = canonicalWorkId;
        ArticleEvaluationWorkItem item = Item(evaluationCase, ArticleEvaluationTaskKinds.Review);
        database.Add(run);
        await database.SaveChangesAsync();

        Assert.True(await Processor(scope, database, new AllowingAccess()).ProcessNextAsync(default));

        database.ChangeTracker.Clear();
        Assert.Equal("AssociationLost", (await database.ArticleEvaluationWorkItems
            .SingleAsync(value => value.Id == item.Id)).OutcomeCode);
    }

    [Fact]
    public async Task ProcessNext_FailedCrossCheckDependency_IsExplicitlySkipped()
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        ArticleEvaluationRun run = Run("evaluation-owner", false);
        run.Status = ArticleEvaluationStatus.Running;
        ArticleEvaluationCase evaluationCase = Case(run, "dependency", ArticleEvaluationTaskKinds.Review);
        ArticleEvaluationWorkItem failed = Item(evaluationCase, ArticleEvaluationTaskKinds.Review);
        failed.Status = ArticleEvaluationStatus.Failed;
        ArticleEvaluationWorkItem checker = Item(evaluationCase, ArticleEvaluationTaskKinds.CrossCheck);
        checker.DependsOn = failed;
        database.Add(run);
        await database.SaveChangesAsync();

        Assert.False(await Processor(scope, database, new AllowingAccess()).ProcessNextAsync(default));

        database.ChangeTracker.Clear();
        ArticleEvaluationWorkItem saved = await database.ArticleEvaluationWorkItems
            .SingleAsync(value => value.Id == checker.Id);
        Assert.Equal(ArticleEvaluationStatus.Skipped, saved.Status);
        Assert.Equal("DependencyFailed", saved.OutcomeCode);
    }

    [Fact]
    public async Task ProcessNext_ExecutionTokenChangesDuringCall_DoesNotOverwriteAndMarksInterrupted()
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        ArticleEvaluationWorkItem? item = null;
        EvaluationHarness harness = EvaluationClient(new StubHandler((_, _) =>
        {
            using Microsoft.Data.SqlClient.SqlConnection connection = new(fixture.ConnectionString);
            connection.Open();
            using Microsoft.Data.SqlClient.SqlCommand command = connection.CreateCommand();
            command.CommandText =
                "UPDATE [analysis].[ArticleEvaluationWorkItems] SET [ExecutionToken]=NEWID() WHERE [Id]=@id;";
            command.Parameters.AddWithValue("@id", item!.Id);
            command.ExecuteNonQuery();
            return Task.FromResult(GeminiResponse(
                "{\"verdicts\":[{\"claimId\":\"claim-1\",\"verdict\":\"uncertain\",\"reason\":\"Synthetic.\"}]}"));
        }));
        item = await SeedCalibrationAsync(database, harness.Profile);
        ArticleEvaluationProcessor processor = Processor(scope, database, new AllowingAccess(), harness.Client);

        Assert.True(await processor.ProcessNextAsync(default));
        database.ChangeTracker.Clear();
        ArticleEvaluationWorkItem fenced = await database.ArticleEvaluationWorkItems
            .Include(value => value.Result).SingleAsync(value => value.Id == item.Id);
        Assert.Equal(ArticleEvaluationStatus.Running, fenced.Status);
        Assert.Null(fenced.Result);

        Assert.False(await processor.ProcessNextAsync(default));
        database.ChangeTracker.Clear();
        Assert.Equal(ArticleEvaluationStatus.Interrupted,
            (await database.ArticleEvaluationWorkItems.SingleAsync(value => value.Id == item.Id)).Status);
    }

    [Fact]
    public async Task ProcessNext_InvalidProviderPayload_PersistsSafeStructuredFailure()
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        EvaluationHarness harness = EvaluationClient(new StubHandler((_, _) =>
            Task.FromResult(GeminiResponse("not-json"))));
        ArticleEvaluationWorkItem item = await SeedCalibrationAsync(database, harness.Profile);

        Assert.True(await Processor(scope, database, new AllowingAccess(), harness.Client)
            .ProcessNextAsync(default));

        database.ChangeTracker.Clear();
        ArticleEvaluationWorkItem saved = await database.ArticleEvaluationWorkItems
            .Include(value => value.Attempts).SingleAsync(value => value.Id == item.Id);
        Assert.Equal(ArticleEvaluationStatus.Failed, saved.Status);
        Assert.Equal("invalid_provider_response", saved.OutcomeCode);
        Assert.DoesNotContain("not-json", saved.OutcomeMessage ?? string.Empty, StringComparison.Ordinal);
        ArticleEvaluationAttempt attempt = Assert.Single(saved.Attempts);
        Assert.Equal("invalid_provider_response", attempt.ErrorCode);
        Assert.Contains("calibration", attempt.ErrorMessage ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"reason\":\"invalid_json\"", attempt.ResponseJson);
    }

    private static ArticleEvaluationProcessor Processor(
        AsyncServiceScope scope,
        AnalysisDbContext database,
        IAcademicProductAccessService access,
        ArticleEvaluationServiceClient? client = null) => new(
            database,
            client ?? new ArticleEvaluationServiceClient(null!, Options.Create(new ProductEvaluationOptions())),
            scope.ServiceProvider.GetRequiredService<AnalysisSourceLock>(),
            access,
            NullLogger<ArticleEvaluationProcessor>.Instance);

    private static async Task<ArticleEvaluationWorkItem> SeedCalibrationAsync(
        AnalysisDbContext database, ArticleEvaluationProfile? profile = null)
    {
        CalibrationCaseDefinition definition = ArticleEvaluationCalibrationCatalog.Create()[0];
        List<ArticlePage> pages =
            [new(null, string.Concat(definition.Snapshot.SourceSpans.Select(value => value.Text)))];
        ReviewArticleRequest source = new("en", "abstract", definition.Snapshot.SourceHash,
            definition.Snapshot.ExtractionVersion, "article-evaluation-policy-v1", pages, 1,
            true, "Synthetic calibration excerpt; controlled source-reading only.")
        {
            SourceSpans = ArticleSourceCatalog.Create(pages)
        };
        ArticleEvaluationRun run = Run("evaluation-owner", false);
        ArticleEvaluationCase evaluationCase = Case(run, "calibration",
            ArticleEvaluationTaskKinds.Calibration);
        evaluationCase.SourceSnapshotJson = JsonSerializer.Serialize(source);
        evaluationCase.SourceHash = source.SourceHash;
        evaluationCase.ExpectedVerdictsJson = JsonSerializer.Serialize(definition.References);
        evaluationCase.RequestPayloadJson = JsonSerializer.Serialize(new
        {
            Source = source,
            CalibrationClaims = definition.Snapshot.Claims.Select(value =>
                new ArticleEvaluationCalibrationClaim(value.ClaimId, value.Text, "findings",
                    source.SourceSpans!.Select(span => span.SourceId).ToList())).ToList()
        });
        ArticleEvaluationWorkItem item = Item(evaluationCase,
            ArticleEvaluationTaskKinds.Calibration, profile);
        database.Add(run);
        await database.SaveChangesAsync();
        return item;
    }

    private static ArticleEvaluationRun Run(string owner, bool real) => new()
    {
        RunId = Guid.NewGuid(),
        OwnerPersonelId = owner,
        ActorAuditId = "evaluation-actor",
        AuthorizationGrantId = "evaluation-grant",
        IncludesRealCases = real,
        Status = ArticleEvaluationStatus.Pending,
        DatasetVersion = ArticleEvaluationCalibrationCatalog.DatasetVersion,
        EvaluatorVersion = "test-v1",
        PolicyVersion = "article-evaluation-policy-v1",
        ProfilesJson = "[]",
        TotalCases = 1,
        TotalWorkItems = 1,
        WorstCaseModelCalls = 1,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static ArticleEvaluationCase Case(ArticleEvaluationRun run, string id, string kind)
    {
        ArticleEvaluationCase value = new()
        {
            Ordinal = 0,
            CaseId = id + Guid.NewGuid().ToString("N"),
            Kind = kind,
            Language = "en",
            SourceHash = new string('c', 64),
            SourceSnapshotJson = "{}",
            ReferenceJson = "{}",
            RequestPayloadJson = "{}"
        };
        run.Cases.Add(value);
        return value;
    }

    private static ArticleEvaluationWorkItem Item(ArticleEvaluationCase evaluationCase, string phase,
        ArticleEvaluationProfile? profile = null)
    {
        profile ??= new("missing-test-profile", "Test profile", "fake",
            "configured-alias", "revision", new string('a', 64), "settings-v1",
            [phase], "configured", null, true);
        ArticleEvaluationWorkItem value = new()
        {
            Ordinal = evaluationCase.WorkItems.Count,
            Phase = phase,
            ProfileId = profile.ProfileId,
            ProfileFingerprint = profile.SettingsFingerprint,
            ProfileSnapshotJson = JsonSerializer.Serialize(profile)
        };
        evaluationCase.WorkItems.Add(value);
        return value;
    }

    private static EvaluationHarness EvaluationClient(HttpMessageHandler handler,
        int maximumInputBytes = 100_000)
    {
        AnalysisEvaluationOptions analysisOptions = new()
        {
            MaximumInputBytes = maximumInputBytes
        };
        AiOptions ai = new();
        GeminiOptions gemini = new() { ApiKey = "synthetic-key" };
        ArticleEvaluationProfileCatalog catalog = new(Options.Create(ai), Options.Create(gemini),
            Options.Create(analysisOptions));
        HttpClient http = new(handler) { BaseAddress = new Uri("https://generativelanguage.googleapis.com/") };
        ResearcherAnalysisService.Analysis.ArticleEvaluationService service = new(catalog,
            new SingleHttpClientFactory(http), Options.Create(ai), Options.Create(gemini),
            Options.Create(analysisOptions), new TestGeminiUsageRepository());
        ArticleEvaluationServiceClient client = new(service, Options.Create(new ProductEvaluationOptions()));
        return new(client, catalog.GetRequired(ArticleEvaluationProfileCatalog.GeminiProfileId).ToContract());
    }

    private static HttpResponseMessage GeminiResponse(string answer) => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(new
        {
            candidates = new[]
            {
                new { content = new { parts = new[] { new { text = answer } } }, finishReason = "STOP" }
            },
            usageMetadata = new
            {
                promptTokenCount = 10,
                cachedContentTokenCount = 0,
                candidatesTokenCount = 5,
                thoughtsTokenCount = 0,
                totalTokenCount = 15
            },
            modelVersion = ArticleEvaluationProfileCatalog.GeminiModel
        })
    };

    private static async Task AddLargeAnalysisRunAsync(AnalysisDbContext database,
        SyntheticCanonicalSource source)
    {
        string firstText = new('x', 3_000);
        string secondText = new('y', 3_000);
        ArticleSourceSnapshot snapshot = new()
        {
            CanonicalWorkId = source.CanonicalWorkId,
            ExtractedTextHash = new string('d', 64),
            SourceKind = "abstract",
            ExtractionVersion = "synthetic-v1",
            CreatedAt = DateTimeOffset.UtcNow,
            Pages =
            [
                new() { Ordinal = 0, PageNumber = 1, Text = firstText },
                new() { Ordinal = 1, PageNumber = 2, Text = secondText }
            ],
            Spans =
            [
                new()
                {
                    Ordinal = 0,
                    SourceId = "src-1",
                    PageNumber = 1,
                    StartOffset = 0,
                    EndOffset = firstText.Length,
                    Text = firstText
                },
                new()
                {
                    Ordinal = 1,
                    SourceId = "src-2",
                    PageNumber = 2,
                    StartOffset = 0,
                    EndOffset = secondText.Length,
                    Text = secondText
                }
            ]
        };
        database.Add(new CanonicalArticleAnalysisRun
        {
            CanonicalWorkId = source.CanonicalWorkId,
            SourceIdentityHash = source.SourceIdentityHash,
            ArticleSourceSnapshot = snapshot,
            SavedArticleSummary = new()
            {
                PersonelId = source.PersonelId,
                OriginalAcademicWorkId = source.AcademicWorkId,
                SavedAt = DateTimeOffset.UtcNow,
                SourceHash = snapshot.ExtractedTextHash,
                SourceKind = snapshot.SourceKind,
                ExtractionVersion = snapshot.ExtractionVersion,
                SnapshotJson = JsonSerializer.Serialize(new SummarizeArticleRequest("en", "abstract",
                    snapshot.ExtractedTextHash, snapshot.ExtractionVersion,
                    [new(1, firstText), new(2, secondText)], 2, false, null)
                {
                    SourceSpans =
                    [
                        new("src-1", 1, 0, firstText.Length, firstText),
                        new("src-2", 2, 0, secondText.Length, secondText)
                    ]
                }),
                ReportJson = "{}"
            },
            AnalyzedAt = DateTimeOffset.UtcNow,
            SourceAcquiredAt = DateTimeOffset.UtcNow,
            SourceOrigin = "Synthetic",
            Language = "en",
            PolicyVersion = "summary-v1",
            Model = "synthetic",
            PromptVersion = "synthetic-v1",
            ExtractionMethod = "abstract",
            ProcessedChunks = 1,
            TotalChunks = 1,
            ProcessedPages = 2,
            TextBearingPages = 2,
            TotalPages = 2,
            OmissionReasonsJson = "[]",
            VerificationStatus = "automatically_checked",
            VerificationModel = "synthetic",
            VerificationPromptVersion = "synthetic-v1"
        });
        await database.SaveChangesAsync();
    }

    private sealed record EvaluationHarness(
        ArticleEvaluationServiceClient Client,
        ArticleEvaluationProfile Profile);

    private sealed class SingleHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    private sealed class AllowingAccess : IAcademicProductAccessService
    {
        public Task<AcademicProductAccessGrant> AuthorizeAsync(ClaimsPrincipal principal,
            AcademicProductAccessRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AcademicProductAccessGrant> ReauthorizeAsync(
            AcademicProductAccessGrant persistedGrant, CancellationToken cancellationToken) =>
            Task.FromResult(persistedGrant);
    }

    private sealed class DenyingAccess : IAcademicProductAccessService
    {
        public Task<AcademicProductAccessGrant> AuthorizeAsync(ClaimsPrincipal principal,
            AcademicProductAccessRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AcademicProductAccessGrant> ReauthorizeAsync(
            AcademicProductAccessGrant persistedGrant, CancellationToken cancellationToken) =>
            throw new AcademicProductAccessDeniedException();
    }
}
