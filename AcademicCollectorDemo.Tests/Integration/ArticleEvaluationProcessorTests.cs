using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using AcademicCollector.Analysis.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Analysis;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Evaluations;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;
using AcademicCollectorDemo.Tests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using AcademicCollectorDemo.Modules.AcademicPerformance.ProductAccess;
using System.Security.Claims;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class ArticleEvaluationProcessorTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Enqueue_RealSourceExceedsSelectedProfileLimit_RejectsBeforeCreatingRun()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        (string personelId, int canonicalWorkId) = await SeedLargeCanonicalSourceAsync(database);
        ArticleEvaluationProfile constrained = Profile(FingerprintA) with
        {
            ExecutionSettings = new Dictionary<string, string>(Settings)
                { ["maximumInputBytes"] = "1024" }
        };
        StubHttpHandler handler = new(_ => StubHttpHandler.Json(JsonSerializer.Serialize(
            new ArticleEvaluationProfilesResponse([constrained]), JsonOptions)));
        ArticleEvaluationScheduler scheduler = new(database,
            new(new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1/") },
                Options.Create(new AnalysisServiceOptions())),
            Options.Create(new AcademicCollectorDemo.Modules.AcademicPerformance.Evaluations.ArticleEvaluationOptions()));
        int before = await database.ArticleEvaluationRuns.CountAsync();

        await Assert.ThrowsAsync<ArticleEvaluationValidationException>(() => scheduler.EnqueueAsync(Grant(personelId), new()
        {
            ProfileIds = [constrained.ProfileId], PersonelId = personelId,
            RealCases = [new() { CanonicalWorkId = canonicalWorkId, Language = "en" }]
        }, default));

        Assert.Equal(before, await database.ArticleEvaluationRuns.CountAsync());
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task ProcessNext_ProfileFingerprintChanged_FailsBeforeExecute()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        ArticleEvaluationWorkItem item = await SeedCalibrationAsync(database, FingerprintA);
        StubHttpHandler handler = new(request => request.Method == HttpMethod.Get
            ? StubHttpHandler.Json(JsonSerializer.Serialize(new ArticleEvaluationProfilesResponse(
                [Profile(FingerprintB)]), JsonOptions))
            : throw new InvalidOperationException("Drift must be rejected before execute."));

        Assert.True(await Processor(scope, database, handler).ProcessNextAsync(default));

        database.ChangeTracker.Clear();
        ArticleEvaluationWorkItem saved = await database.ArticleEvaluationWorkItems
            .Include(value => value.Attempts).SingleAsync(value => value.Id == item.Id);
        Assert.Equal(ArticleEvaluationStatus.Failed, saved.Status);
        Assert.Equal("ProfileDrift", saved.OutcomeCode);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal("Unknown", Assert.Single(saved.Attempts).CostStatus);
    }

    [Fact]
    public async Task ProcessNext_AuthorizationRevoked_FailsBeforeProviderUse()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        ArticleEvaluationWorkItem item = await SeedCalibrationAsync(database, FingerprintA);
        StubHttpHandler handler = new(_ => throw new InvalidOperationException(
            "Revoked authorization must precede provider access."));

        Assert.True(await Processor(scope, database, handler, new DenyingAccess()).ProcessNextAsync(default));

        database.ChangeTracker.Clear();
        ArticleEvaluationWorkItem saved = await database.ArticleEvaluationWorkItems.SingleAsync(value => value.Id == item.Id);
        Assert.Equal(ArticleEvaluationStatus.Failed, saved.Status);
        Assert.Equal("AccessRevoked", saved.OutcomeCode);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task ProcessNext_RealCaseAssociationMissing_DoesNotContactAnalysisService()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        CanonicalWork work = new() { SourceScopedKey = Guid.NewGuid().ToString("N") + new string('0', 32),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        database.Add(work);
        await database.SaveChangesAsync();
        ArticleEvaluationRun run = Run("missing-owner", true);
        ArticleEvaluationCase evaluationCase = Case(run, "real", ArticleEvaluationTaskKinds.Review);
        evaluationCase.CanonicalWorkId = work.Id;
        ArticleEvaluationWorkItem item = Item(evaluationCase, Profile(FingerprintA), ArticleEvaluationTaskKinds.Review);
        database.Add(run);
        await database.SaveChangesAsync();
        StubHttpHandler handler = new(_ => throw new InvalidOperationException("Association loss must precede network access."));

        Assert.True(await Processor(scope, database, handler).ProcessNextAsync(default));

        database.ChangeTracker.Clear();
        Assert.Equal("AssociationLost", (await database.ArticleEvaluationWorkItems.SingleAsync(value => value.Id == item.Id)).OutcomeCode);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task ProcessNext_FailedCrossCheckDependency_IsExplicitlySkipped()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        ArticleEvaluationRun run = Run("evaluation-owner", false);
        run.Status = ArticleEvaluationStatus.Running;
        ArticleEvaluationCase evaluationCase = Case(run, "dependency", ArticleEvaluationTaskKinds.Review);
        ArticleEvaluationWorkItem failed = Item(evaluationCase, Profile(FingerprintA), ArticleEvaluationTaskKinds.Review);
        failed.Status = ArticleEvaluationStatus.Failed;
        ArticleEvaluationWorkItem checker = Item(evaluationCase, Profile(FingerprintA), ArticleEvaluationTaskKinds.CrossCheck);
        checker.DependsOn = failed;
        database.Add(run);
        await database.SaveChangesAsync();
        StubHttpHandler handler = new(_ => throw new InvalidOperationException("A blocked checker must not call the service."));

        Assert.False(await Processor(scope, database, handler).ProcessNextAsync(default));

        database.ChangeTracker.Clear();
        ArticleEvaluationWorkItem saved = await database.ArticleEvaluationWorkItems.SingleAsync(value => value.Id == checker.Id);
        Assert.Equal(ArticleEvaluationStatus.Skipped, saved.Status);
        Assert.Equal("DependencyFailed", saved.OutcomeCode);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task ProcessNext_ExecutionTokenChangesAfterCall_DoesNotOverwriteAndThenMarksInterrupted()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        ArticleEvaluationWorkItem item = await SeedCalibrationAsync(database, FingerprintA);
        StubHttpHandler handler = new(request =>
        {
            if (request.Method == HttpMethod.Get)
                return StubHttpHandler.Json(JsonSerializer.Serialize(
                    new ArticleEvaluationProfilesResponse([Profile(FingerprintA)]), JsonOptions));
            ArticleEvaluationRequest payload = request.Content!.ReadFromJsonAsync<ArticleEvaluationRequest>().Result!;
            using SqlConnection connection = new(fixture.ConnectionString);
            connection.Open();
            using SqlCommand command = connection.CreateCommand();
            command.CommandText = "UPDATE [analysis].[ArticleEvaluationWorkItems] SET [ExecutionToken]=NEWID() WHERE [Id]=@id;";
            command.Parameters.AddWithValue("@id", item.Id);
            command.ExecuteNonQuery();
            return StubHttpHandler.Json(JsonSerializer.Serialize(Response(payload), JsonOptions));
        });
        ArticleEvaluationProcessor processor = Processor(scope, database, handler);

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
    public async Task ProcessNext_StructuredFailurePersistsSafeReasonStageAndRole()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        ArticleEvaluationWorkItem item = await SeedCalibrationAsync(database, FingerprintA);
        StubHttpHandler handler = new(request => request.Method == HttpMethod.Get
            ? StubHttpHandler.Json(JsonSerializer.Serialize(
                new ArticleEvaluationProfilesResponse([Profile(FingerprintA)]), JsonOptions))
            : StubHttpHandler.Json(JsonSerializer.Serialize(FailedResponse(
                request.Content!.ReadFromJsonAsync<ArticleEvaluationRequest>().Result!), JsonOptions)));

        Assert.True(await Processor(scope, database, handler).ProcessNextAsync(default));

        database.ChangeTracker.Clear();
        ArticleEvaluationWorkItem saved = await database.ArticleEvaluationWorkItems
            .Include(value => value.Attempts).SingleAsync(value => value.Id == item.Id);
        Assert.Equal(ArticleEvaluationStatus.Failed, saved.Status);
        Assert.Equal("invalid_provider_response", saved.OutcomeCode);
        Assert.Equal("output_limit (verification/quantitative)", saved.OutcomeMessage);
        ArticleEvaluationAttempt attempt = Assert.Single(saved.Attempts);
        Assert.Equal("invalid_provider_response", attempt.ErrorCode);
        Assert.Equal("output_limit (verification/quantitative)", attempt.ErrorMessage);
        Assert.Contains("\"reason\":\"output_limit\"", attempt.ResponseJson);
    }

    private static ArticleEvaluationProcessor Processor(IServiceScope scope, AcademicDbContext database,
        StubHttpHandler handler, IAcademicProductAccessService? access = null) => new(database, new(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1/")
        }, Options.Create(new AnalysisServiceOptions())),
        scope.ServiceProvider.GetRequiredService<CanonicalWorkSynchronizer>(),
        access ?? new AllowingAccess(),
        NullLogger<ArticleEvaluationProcessor>.Instance);

    private static async Task<ArticleEvaluationWorkItem> SeedCalibrationAsync(AcademicDbContext database,
        string fingerprint)
    {
        CalibrationCaseDefinition definition = ArticleEvaluationCalibrationCatalog.Create()[0];
        List<ArticlePage> pages = [new(null, string.Concat(definition.Snapshot.SourceSpans.Select(value => value.Text)))];
        ReviewArticleRequest source = new("en", "abstract", definition.Snapshot.SourceHash,
            definition.Snapshot.ExtractionVersion, "article-evaluation-policy-v1", pages, 1, true,
            "Synthetic calibration excerpt; controlled source-reading only.")
        { SourceSpans = ArticleSourceCatalog.Create(pages) };
        ArticleEvaluationRun run = Run("evaluation-owner", false);
        ArticleEvaluationCase evaluationCase = Case(run, "calibration", ArticleEvaluationTaskKinds.Calibration);
        evaluationCase.SourceSnapshotJson = JsonSerializer.Serialize(source, JsonOptions);
        evaluationCase.SourceHash = source.SourceHash;
        evaluationCase.ExpectedVerdictsJson = JsonSerializer.Serialize(definition.References, JsonOptions);
        evaluationCase.RequestPayloadJson = JsonSerializer.Serialize(new
        {
            Source = source,
            CalibrationClaims = definition.Snapshot.Claims.Select(value => new ArticleEvaluationCalibrationClaim(
                value.ClaimId, value.Text, "findings", source.SourceSpans!.Select(span => span.SourceId).ToList())).ToList()
        }, JsonOptions);
        ArticleEvaluationWorkItem item = Item(evaluationCase, Profile(fingerprint), ArticleEvaluationTaskKinds.Calibration);
        database.Add(run);
        await database.SaveChangesAsync();
        return item;
    }

    private static ArticleEvaluationRun Run(string owner, bool real) => new()
    {
        RunId = Guid.NewGuid(), OwnerPersonelId = owner, ActorAuditId = "evaluation-actor",
        AuthorizationGrantId = "evaluation-grant", IncludesRealCases = real,
        Status = ArticleEvaluationStatus.Pending, DatasetVersion = ArticleEvaluationCalibrationCatalog.DatasetVersion,
        EvaluatorVersion = "test-v1", PolicyVersion = "article-evaluation-policy-v1",
        ProfilesJson = JsonSerializer.Serialize(new[] { Profile(FingerprintA) }, JsonOptions),
        TotalCases = 1, TotalWorkItems = 1, WorstCaseModelCalls = 1,
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
    };

    private static AcademicProductAccessGrant Grant(string personelId) =>
        new("evaluation-grant", "evaluation-actor", personelId, AcademicProductOperation.ArticleEvaluationStart);

    private sealed class AllowingAccess : IAcademicProductAccessService
    {
        public Task<AcademicProductAccessGrant> AuthorizeAsync(ClaimsPrincipal principal,
            AcademicProductAccessRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AcademicProductAccessGrant> ReauthorizeAsync(AcademicProductAccessGrant persistedGrant,
            CancellationToken cancellationToken) => Task.FromResult(persistedGrant);
    }

    private sealed class DenyingAccess : IAcademicProductAccessService
    {
        public Task<AcademicProductAccessGrant> AuthorizeAsync(ClaimsPrincipal principal,
            AcademicProductAccessRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AcademicProductAccessGrant> ReauthorizeAsync(AcademicProductAccessGrant persistedGrant,
            CancellationToken cancellationToken) => throw new AcademicProductAccessDeniedException();
    }

    private static ArticleEvaluationCase Case(ArticleEvaluationRun run, string id, string kind)
    {
        ArticleEvaluationCase value = new() { Ordinal = 0, CaseId = id + Guid.NewGuid().ToString("N"), Kind = kind,
            Language = "en", SourceHash = new string('c', 64), SourceSnapshotJson = "{}",
            ReferenceJson = "{}", RequestPayloadJson = "{}" };
        run.Cases.Add(value);
        return value;
    }

    private static ArticleEvaluationWorkItem Item(ArticleEvaluationCase evaluationCase,
        ArticleEvaluationProfile profile, string phase)
    {
        ArticleEvaluationWorkItem value = new() { Ordinal = evaluationCase.WorkItems.Count, Phase = phase,
            ProfileId = profile.ProfileId, ProfileFingerprint = profile.SettingsFingerprint,
            ProfileSnapshotJson = JsonSerializer.Serialize(profile, JsonOptions) };
        evaluationCase.WorkItems.Add(value);
        return value;
    }

    private static ArticleEvaluationProfile Profile(string fingerprint) => new("test-profile", "Test profile",
        "fake", "configured-alias", "revision", fingerprint, "settings-v1",
        [ArticleEvaluationTaskKinds.Calibration, ArticleEvaluationTaskKinds.Review, ArticleEvaluationTaskKinds.CrossCheck],
        "configured", null, true) { ExecutionSettings = Settings };

    private static AcademicCollector.Analysis.Contracts.ArticleEvaluationResponse Response(ArticleEvaluationRequest request)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string sourceIdentity = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            request.Source, JsonOptions))).ToLowerInvariant();
        return new(request.ProfileId, request.TaskKind, "fake", "configured-alias", ["actual-revision"],
            request.ExpectedSettingsFingerprint, "settings-v1", sourceIdentity, ArticleEvaluationOutcomes.Completed,
            null, new(1, 1, [new(1, "calibration", null, "fake", "configured-alias", "actual-revision",
                "completed", now, now.AddMilliseconds(1), 1, 10, 2, null, null, null, null, null, null)]))
        {
            Verdicts = request.CalibrationClaims!.Select(value =>
                new ArticleEvaluationVerdict(value.ClaimId, "uncertain", "test")).ToList(),
            ExecutionSettings = Settings
        };
    }

    private static AcademicCollector.Analysis.Contracts.ArticleEvaluationResponse FailedResponse(
        ArticleEvaluationRequest request)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string sourceIdentity = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            request.Source, JsonOptions))).ToLowerInvariant();
        return new(request.ProfileId, request.TaskKind, "fake", "configured-alias", ["actual-revision"],
            request.ExpectedSettingsFingerprint, "settings-v1", sourceIdentity, ArticleEvaluationOutcomes.Failed,
            "invalid_provider_response", new(1, 1,
            [new(1, "verification", "quantitative", "fake", "configured-alias", "actual-revision",
                "failed", now, now.AddMilliseconds(1), 1, 10, 2, null, null, null, null, null,
                "output_limit")]))
        {
            ExecutionSettings = Settings,
            Failure = new("output_limit", "verification", "quantitative")
        };
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlyDictionary<string, string> Settings =
        new Dictionary<string, string>
        {
            ["promptVersion"] = "test-v1",
            ["maximumInputBytes"] = "100000"
        };
    private static string FingerprintA => new('a', 64);
    private static string FingerprintB => new('b', 64);

    private static async Task<(string PersonelId, int CanonicalWorkId)> SeedLargeCanonicalSourceAsync(
        AcademicDbContext database)
    {
        string personelId = "evaluation-limit-" + Guid.NewGuid().ToString("N");
        DateTime now = DateTime.UtcNow;
        Researcher researcher = new() { PersonelId = personelId };
        CanonicalWork work = new() { NormalizedDoi = "10.9200/" + Guid.NewGuid().ToString("N"),
            CreatedAt = now, UpdatedAt = now };
        database.AddRange(researcher, work);
        await database.SaveChangesAsync();
        database.CanonicalResearcherWorks.Add(new()
            { CanonicalWorkId = work.Id, PersonelId = personelId, LastObservedAt = now });
        List<ArticlePage> pages = [new(1, new string('x', 1500))];
        IReadOnlyList<ArticleSourceSpan> spans = ArticleSourceCatalog.Create(pages);
        string hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            pages, JsonOptions))).ToLowerInvariant();
        ArticleSourceSnapshot source = new()
        {
            CanonicalWorkId = work.Id, ExtractedTextHash = hash, SourceKind = "pdf",
            ExtractionVersion = "pdf-v1", CreatedAt = DateTimeOffset.UtcNow,
            Pages = [new() { Ordinal = 0, PageNumber = 1, Text = pages[0].Text }],
            Spans = spans.Select((span, index) => new ArticleSourceSpanSnapshot
            {
                Ordinal = index, SourceId = span.SourceId, PageNumber = span.PageNumber,
                StartOffset = span.StartOffset, EndOffset = span.EndOffset, Text = span.Text
            }).ToList()
        };
        SummarizeArticleRequest original = new("en", "pdf", hash, "pdf-v1", pages, 1, false, null)
            { SourceSpans = spans };
        SavedArticleSummary summary = new()
        {
            OriginalAcademicWorkId = 1, PersonelId = personelId, SavedAt = DateTimeOffset.UtcNow,
            SourceHash = hash, SourceKind = "pdf", ExtractionVersion = "pdf-v1",
            SnapshotJson = JsonSerializer.Serialize(original, JsonOptions), ReportJson = "{}"
        };
        database.AddRange(source, summary);
        await database.SaveChangesAsync();
        database.CanonicalArticleAnalysisRuns.Add(new()
        {
            CanonicalWorkId = work.Id, ArticleSourceSnapshotId = source.Id, SavedArticleSummaryId = summary.Id,
            AnalyzedAt = DateTimeOffset.UtcNow, SourceAcquiredAt = DateTimeOffset.UtcNow,
            SourceOrigin = "Synthetic", Language = "en", PolicyVersion = "summary-v1",
            Model = "synthetic", PromptVersion = "summary-v1", ExtractionMethod = "pdf_text",
            ProcessedChunks = 1, TotalChunks = 1, ProcessedPages = 1, TextBearingPages = 1,
            TotalPages = 1, OmissionReasonsJson = "[]", VerificationStatus = "automatically_checked",
            VerificationModel = "synthetic", VerificationPromptVersion = "verify-v1"
        });
        await database.SaveChangesAsync();
        return (personelId, work.Id);
    }
}
