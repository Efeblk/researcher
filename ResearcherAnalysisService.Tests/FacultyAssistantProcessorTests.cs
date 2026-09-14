using System.Security.Claims;
using System.Text.Json;
using AcademicCollector.Analysis.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Products.Api.Contracts;
using ResearcherAnalysisService.Products.ArticleSummaries;
using ResearcherAnalysisService.Products.Data;
using ResearcherAnalysisService.Products.FacultyAssistant;
using ResearcherAnalysisService.Products.Knowledge;
using ResearcherAnalysisService.Products.ProductAccess;
using ResearcherAnalysisService.Tests.Infrastructure;

namespace ResearcherAnalysisService.Tests;

[Collection("Analysis Product SQL Server")]
public sealed class FacultyAssistantProcessorTests(AnalysisProductSqlServerFixture fixture)
{
    [Fact]
    public async Task ProcessReadAndReplay_EmptySupportedSetCompletesOnceWithPinnedInput()
    {
        SyntheticCanonicalSource source = await fixture.SeedCanonicalSourceAsync(
            "faculty-complete-" + Guid.NewGuid().ToString("N"));
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        await CompleteExistingRunnableAsync(database);
        FacultyAssistantScheduler scheduler = new(database,
            new FakeSearch(source.CanonicalWorkId), Options.Create(new FacultyAssistantOptions()));
        AcademicProductAccessGrant startGrant = new("grant", "actor", source.PersonelId,
            AcademicProductOperation.FacultyAssistantStart);
        StartFacultyAssistantRequest request = new()
        {
            PersonelId = source.PersonelId,
            ClientRequestId = Guid.NewGuid(),
            Mode = "OwnPaperMethods",
            Language = "en",
            Query = "method",
            CanonicalWorkIds = [source.CanonicalWorkId]
        };
        FacultyAssistantRunResponse queued = await scheduler.EnqueueAsync(startGrant, request, default);
        int generations = 0;
        FacultyAssistantProcessor processor = Processor(scope, database, new FakeAccess(false),
            Client((_, _) =>
            {
                generations++;
                return Task.FromResult(EmptyGeneration());
            }));

        Assert.True(await processor.ProcessNextAsync(default));

        database.ChangeTracker.Clear();
        FacultyAssistantRunResponse saved = (await new FacultyAssistantReadService(database).GetAsync(
            startGrant with { Operation = AcademicProductOperation.FacultyAssistantRead },
            queued.RunId, default))!;
        Assert.Equal("Completed", saved.Status);
        Assert.Equal("no_supported_items", saved.Report!.Outcome);
        Assert.Equal(queued.InputFingerprint, saved.InputFingerprint);
        Assert.Equal(queued.Retrieval.Evidence.Single().EvidenceId,
            saved.Retrieval.Evidence.Single().EvidenceId);
        FacultyAssistantRunResponse replay = await scheduler.EnqueueAsync(startGrant, request, default);
        Assert.True(replay.Reused);
        Assert.Equal(saved.RunId, replay.RunId);
        Assert.Equal(saved.Report.Outcome, replay.Report!.Outcome);
        Assert.False(await processor.ProcessNextAsync(default));
        Assert.Equal(1, generations);
    }

    [Fact]
    public async Task GetAsync_LegacyStoredReportWithoutCoverageRemainsReadable()
    {
        SyntheticCanonicalSource source = await fixture.SeedCanonicalSourceAsync(
            "faculty-legacy-" + Guid.NewGuid().ToString("N"));
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        FacultyAssistantRun run = Run(source);
        run.Status = "Completed";
        run.ReportJson = JsonSerializer.Serialize(new
        {
            mode = "OwnPaperMethods",
            language = "en",
            items = Array.Empty<object>(),
            model = "legacy-model",
            promptVersion = "faculty-evidence-assistant-v2",
            verification = new
            {
                status = "not_run",
                model = "",
                promptVersion = "",
                usesSameModelFamily = false,
                limitation = "Legacy empty report."
            },
            outcome = "no_supported_items"
        });
        database.Add(run);
        await database.SaveChangesAsync();

        FacultyAssistantRunResponse response = (await new FacultyAssistantReadService(database).GetAsync(
            new("grant", "actor", source.PersonelId, AcademicProductOperation.FacultyAssistantRead),
            run.RunId, default))!;

        Assert.Equal("Completed", response.Status);
        Assert.Equal("no_supported_items", response.Report!.Outcome);
        Assert.Null(response.Report.Coverage);
        Assert.Null(response.Report.RequestCoverage);
        Assert.Null(response.Report.SourceChecks);
        Assert.Null(response.Report.Repair);
    }

    [Fact]
    public async Task EnqueueAsync_ReplayUsesPinnedEvidenceAndContext()
    {
        SyntheticCanonicalSource source = await fixture.SeedCanonicalSourceAsync(
            "faculty-" + Guid.NewGuid().ToString("N"));
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        database.FacultyAssistantContextVersions.Add(new()
        {
            PersonelId = source.PersonelId,
            Version = 3,
            ContextJson = "{}",
            ContextFingerprint = new string('d', 64),
            CreatedByActorId = "actor",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await database.SaveChangesAsync();
        FacultyAssistantScheduler scheduler = new(database,
            new FakeSearch(source.CanonicalWorkId), Options.Create(new FacultyAssistantOptions()));
        AcademicProductAccessGrant grant = new("grant", "actor", source.PersonelId,
            AcademicProductOperation.FacultyAssistantStart);
        StartFacultyAssistantRequest request = new()
        {
            PersonelId = source.PersonelId,
            ClientRequestId = Guid.NewGuid(),
            Mode = "OwnPaperMethods",
            Language = "en",
            Query = "method",
            ContextVersion = 3,
            CanonicalWorkIds = [source.CanonicalWorkId]
        };

        FacultyAssistantRunResponse queued = await scheduler.EnqueueAsync(grant, request, default);
        FacultyAssistantRunResponse replay = await scheduler.EnqueueAsync(grant, request, default);

        Assert.True(replay.Reused);
        Assert.Equal(queued.RunId, replay.RunId);
        Assert.Equal(queued.InputFingerprint, replay.InputFingerprint);
        Assert.Equal(3, queued.Context!.Version);
        Assert.StartsWith($"work:{source.CanonicalWorkId}:",
            Assert.Single(queued.Retrieval.Evidence).EvidenceId, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EnqueueAsync_InvalidAuthorizedEvidenceIsRejectedWithoutQueue(bool stale)
    {
        SyntheticCanonicalSource source = await fixture.SeedCanonicalSourceAsync(
            "faculty-invalid-" + Guid.NewGuid().ToString("N"));
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        FacultyAssistantScheduler scheduler = new(database,
            stale
                ? new FakeSearch(source.CanonicalWorkId, freshness: AnalysisFreshnessStatus.Stale)
                : new FakeSearch(source.CanonicalWorkId, hitCount: 9, evidenceLength: 12000),
            Options.Create(new FacultyAssistantOptions()));
        StartFacultyAssistantRequest request = new()
        {
            PersonelId = source.PersonelId,
            ClientRequestId = Guid.NewGuid(),
            Mode = "OwnPaperMethods",
            Language = "en",
            Query = "method",
            CanonicalWorkIds = [source.CanonicalWorkId]
        };

        await Assert.ThrowsAsync<FacultyAssistantInputException>(() => scheduler.EnqueueAsync(
            new("grant", "actor", source.PersonelId,
                AcademicProductOperation.FacultyAssistantStart), request, default));
        Assert.False(await database.FacultyAssistantRuns.AnyAsync(value =>
            value.PersonelId == source.PersonelId));
    }

    [Fact]
    public async Task ProcessNextAsync_AbandonedRunningRunBecomesInterruptedWithoutDispatch()
    {
        SyntheticCanonicalSource source = await fixture.SeedCanonicalSourceAsync(
            "faculty-abandoned-" + Guid.NewGuid().ToString("N"));
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        await CompleteExistingRunnableAsync(database);
        FacultyAssistantRun run = Run(source);
        run.Status = "Running";
        run.AttemptCount = 1;
        run.AttemptToken = Guid.NewGuid();
        database.Add(run);
        await database.SaveChangesAsync();

        Assert.False(await Processor(scope, database, new FakeAccess(false))
            .ProcessNextAsync(default));

        database.ChangeTracker.Clear();
        FacultyAssistantRun saved = await database.FacultyAssistantRuns.SingleAsync(value => value.Id == run.Id);
        Assert.Equal("Interrupted", saved.Status);
        Assert.Equal("Interrupted", saved.ErrorCode);
        Assert.Equal(1, saved.AttemptCount);
    }

    [Fact]
    public async Task ProcessNextAsync_UpstreamCancellationBecomesInterruptedAndIsNotRetried()
    {
        SyntheticCanonicalSource source = await fixture.SeedCanonicalSourceAsync(
            "faculty-cancel-" + Guid.NewGuid().ToString("N"));
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        await CompleteExistingRunnableAsync(database);
        FacultyAssistantRun run = Run(source);
        database.Add(run);
        await database.SaveChangesAsync();
        int generations = 0;
        FacultyAssistantProcessor processor = Processor(scope, database, new FakeAccess(false),
            Client((_, _) =>
            {
                generations++;
                throw new OperationCanceledException("Synthetic upstream timeout.");
            }));

        Assert.True(await processor.ProcessNextAsync(default));

        database.ChangeTracker.Clear();
        FacultyAssistantRun saved = await database.FacultyAssistantRuns.SingleAsync(value => value.Id == run.Id);
        Assert.Equal("Interrupted", saved.Status);
        Assert.Equal("Interrupted", saved.ErrorCode);
        Assert.Equal(1, saved.AttemptCount);
        Assert.False(await processor.ProcessNextAsync(default));
        Assert.Equal(1, generations);
    }

    [Fact]
    public async Task ProcessNextAsync_NewerAnalysisMakesPinnedEvidenceStaleBeforeDispatch()
    {
        SyntheticCanonicalSource source = await fixture.SeedCanonicalSourceAsync(
            "faculty-drift-" + Guid.NewGuid().ToString("N"));
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        await CompleteExistingRunnableAsync(database);
        CanonicalArticleAnalysisRun pinned = await AddAnalysisRunAsync(database, source);
        _ = await AddAnalysisRunAsync(database, source);
        FacultyAssistantRun run = Run(source);
        run.RetrievalManifestJson = JsonSerializer.Serialize(new AcademicEvidenceSearchResponse
        {
            Hits = [new()
            {
                AnalysisRunId = pinned.Id,
                CanonicalWorkId = source.CanonicalWorkId,
                SourceCoverage = new()
            }]
        });
        database.Add(run);
        await database.SaveChangesAsync();
        int generations = 0;

        Assert.True(await Processor(scope, database, new FakeAccess(false),
            Client((_, _) =>
            {
                generations++;
                return Task.FromResult(EmptyGeneration());
            })).ProcessNextAsync(default));

        database.ChangeTracker.Clear();
        FacultyAssistantRun saved = await database.FacultyAssistantRuns.SingleAsync(value => value.Id == run.Id);
        Assert.Equal("Failed", saved.Status);
        Assert.Equal("EvidenceChanged", saved.ErrorCode);
        Assert.Equal(0, generations);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ProcessNextAsync_RevokedPermissionOrAssociationFailsBeforeDispatch(
        bool revokePermission, bool removeAssociation)
    {
        SyntheticCanonicalSource source = await fixture.SeedCanonicalSourceAsync(
            "faculty-access-" + Guid.NewGuid().ToString("N"));
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        await CompleteExistingRunnableAsync(database);
        if (removeAssociation)
        {
            await using DbContext sourceSeed = fixture.CreateSeedContext();
            var association = await sourceSeed.Set<ResearcherAnalysisService.SourceData.Works.CanonicalResearcherWork>()
                .SingleAsync(value => value.PersonelId == source.PersonelId &&
                    value.CanonicalWorkId == source.CanonicalWorkId);
            sourceSeed.Remove(association);
            await sourceSeed.SaveChangesAsync();
        }
        FacultyAssistantRun run = Run(source);
        database.Add(run);
        await database.SaveChangesAsync();

        Assert.True(await Processor(scope, database, new FakeAccess(revokePermission))
            .ProcessNextAsync(default));

        database.ChangeTracker.Clear();
        FacultyAssistantRun saved = await database.FacultyAssistantRuns.SingleAsync(value => value.Id == run.Id);
        Assert.Equal("Failed", saved.Status);
        Assert.Equal("AccessRevoked", saved.ErrorCode);
    }

    private static FacultyAssistantProcessor Processor(
        AsyncServiceScope scope,
        AnalysisDbContext database,
        IAcademicProductAccessService access,
        FacultyAssistantServiceClient? client = null) => new(database,
            client ?? new FacultyAssistantServiceClient(null!, Options.Create(new FacultyAssistantOptions())),
            access, NullLogger<FacultyAssistantProcessor>.Instance,
            Options.Create(new ArticleSummaryAutomationOptions()));

    private static FacultyAssistantServiceClient Client(
        Func<FacultyAssistantAnalysisRequest, CancellationToken,
            Task<GeneratedFacultyAssistantAnswer>> generate) => new(
        new ResearcherAnalysisService.Analysis.FacultyAssistant(
            new StubFacultyGenerator(generate), new NeverFacultyVerifier(),
            new NeverRepairGenerator(), new NeverCoverageVerifier()),
        Options.Create(new FacultyAssistantOptions()));

    private static GeneratedFacultyAssistantAnswer EmptyGeneration() => new([], "synthetic",
        FacultyAssistantPrompt.Version,
        [new(1, "high", "Success", "synthetic", 1, 0.00001m, "synthetic-v1", true)
        {
            AttemptId = Guid.NewGuid()
        }]);

    private static async Task<CanonicalArticleAnalysisRun> AddAnalysisRunAsync(
        AnalysisDbContext database, SyntheticCanonicalSource source)
    {
        ArticleSourceSnapshot snapshot = new()
        {
            CanonicalWorkId = source.CanonicalWorkId,
            ExtractedTextHash = Guid.NewGuid().ToString("N").PadRight(64, 'a'),
            SourceKind = "abstract",
            ExtractionVersion = "synthetic-v1",
            CreatedAt = DateTimeOffset.UtcNow
        };
        CanonicalArticleAnalysisRun run = new()
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
                SnapshotJson = "{}",
                ReportJson = "{}"
            },
            AnalyzedAt = DateTimeOffset.UtcNow,
            SourceAcquiredAt = DateTimeOffset.UtcNow,
            SourceOrigin = "Synthetic",
            Language = "tr",
            PolicyVersion = new ArticleSummaryAutomationOptions().PolicyVersion,
            Model = "synthetic",
            PromptVersion = "synthetic-v1",
            ExtractionMethod = "abstract",
            ProcessedChunks = 1,
            TotalChunks = 1,
            ProcessedPages = 1,
            TextBearingPages = 1,
            TotalPages = 1,
            OmissionReasonsJson = "[]",
            VerificationStatus = "automatically_checked",
            VerificationModel = "synthetic",
            VerificationPromptVersion = "synthetic-v1"
        };
        database.Add(run);
        await database.SaveChangesAsync();
        return run;
    }

    private static Task<int> CompleteExistingRunnableAsync(AnalysisDbContext database) =>
        database.FacultyAssistantRuns
            .Where(value => value.Status == "Pending" || value.Status == "Running")
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Status, "Completed"));

    private static FacultyAssistantRun Run(SyntheticCanonicalSource source)
    {
        AcademicCollector.Analysis.Contracts.FacultyAssistantAnalysisRequest input = new()
        {
            Mode = "OwnPaperMethods",
            Language = "en",
            Query = "method",
            EvidenceCatalogHash = new string('a', 64),
            Evidence = [new($"work:{source.CanonicalWorkId}:snapshot:1:span:1",
                source.CanonicalWorkId, 1, "src-1", 1, 0, 26,
                "Synthetic source evidence.", "pdf", false)]
        };
        return new()
        {
            RunId = Guid.NewGuid(),
            PersonelId = source.PersonelId,
            ActorAuditId = "actor",
            AuthorizationGrantId = "grant",
            ClientRequestId = Guid.NewGuid(),
            Mode = input.Mode,
            Language = input.Language,
            RetrievalPolicyVersion = "v1",
            Status = "Pending",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            RequestJson = "{}",
            RetrievalManifestJson = "{}",
            AuthorizedInputJson = JsonSerializer.Serialize(input),
            InputFingerprint = new string('b', 64)
        };
    }

    private sealed class FakeSearch(
        int workId,
        int hitCount = 1,
        int evidenceLength = 7,
        string freshness = AnalysisFreshnessStatus.Unknown) : IAcademicEvidenceSearchService
    {
        public Task<AcademicEvidenceSearchResponse> SearchAsync(string personelId,
            AcademicEvidenceSearchRequest request, CancellationToken cancellationToken)
        {
            string exact = evidenceLength == 7 ? "Synthetic source evidence." : new string('x', evidenceLength);
            return Task.FromResult(new AcademicEvidenceSearchResponse
            {
                QueryHash = new string('a', 64),
                CorpusHash = new string('b', 64),
                InputHash = new string('c', 64),
                RequestedCanonicalWorkCount = 1,
                EligibleCanonicalWorkCount = 1,
                CoveredCanonicalWorkCount = 1,
                CandidateSpanCount = hitCount,
                Hits = Enumerable.Range(0, hitCount).Select(index => new AcademicEvidenceSearchHitDto
                {
                    EvidenceId = $"work:{workId}:snapshot:1:span:{index + 1}",
                    CanonicalWorkId = workId,
                    ArticleSourceSpanId = index + 1,
                    SourceId = $"src-{index + 1}",
                    ExactText = exact,
                    EndOffset = exact.Length,
                    SourceKind = "abstract",
                    AnalysisFreshnessStatus = freshness
                }).ToList()
            });
        }
    }

    private sealed class FakeAccess(bool deny) : IAcademicProductAccessService
    {
        public Task<AcademicProductAccessGrant> AuthorizeAsync(ClaimsPrincipal principal,
            AcademicProductAccessRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AcademicProductAccessGrant> ReauthorizeAsync(
            AcademicProductAccessGrant persistedGrant, CancellationToken cancellationToken) =>
            deny ? throw new AcademicProductAccessDeniedException() : Task.FromResult(persistedGrant);
    }

    private sealed class StubFacultyGenerator(
        Func<FacultyAssistantAnalysisRequest, CancellationToken,
            Task<GeneratedFacultyAssistantAnswer>> generate) : IFacultyAssistantGenerator
    {
        public Task<GeneratedFacultyAssistantAnswer> GenerateAsync(
            FacultyAssistantAnalysisRequest request, CancellationToken cancellationToken) =>
            generate(request, cancellationToken);
    }

    private sealed class NeverFacultyVerifier : IFacultyAssistantVerifier
    {
        public Task<GeneratedFacultyAssistantSourceCheck> VerifyAsync(string role, string language,
            GeneratedArticleReviewFinding finding, IReadOnlyList<ArticleSourceSpan> sourceSpans,
            CancellationToken cancellationToken) => throw new InvalidOperationException(
                "An empty generation must not invoke verification.");
    }

    private sealed class NeverRepairGenerator : IFacultyAssistantRepairGenerator
    {
        public Task<GeneratedFacultyAssistantRepair> RepairAsync(FacultyAssistantAnalysisRequest request,
            IReadOnlyList<GeneratedFacultyAssistantRepairCandidate> omittedCandidates,
            IReadOnlyList<GeneratedFacultyAssistantItem> supportedItems,
            CancellationToken cancellationToken) => throw new InvalidOperationException(
                "An empty generation must not invoke repair.");
    }

    private sealed class NeverCoverageVerifier : IFacultyRequestCoverageVerifier
    {
        public Task<GeneratedFacultyRequestCoverage> VerifyAsync(string mode, string language,
            string query, IReadOnlyList<FacultyAssistantAnswerItem> retainedItems,
            CancellationToken cancellationToken) => throw new InvalidOperationException(
                "An empty generation must not invoke request coverage verification.");
    }
}
