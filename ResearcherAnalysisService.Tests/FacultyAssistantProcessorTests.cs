using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
        IAcademicProductAccessService access) => new(database,
            new FacultyAssistantServiceClient(null!, Options.Create(new FacultyAssistantOptions())),
            access, NullLogger<FacultyAssistantProcessor>.Instance,
            Options.Create(new ArticleSummaryAutomationOptions()));

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
}
