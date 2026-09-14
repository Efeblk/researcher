using System.Security.Claims;
using System.Text.Json;
using AcademicCollector.Analysis.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ResearcherAnalysisService.Products.ArticleSummaries;
using ResearcherAnalysisService.Products.Data;
using ResearcherAnalysisService.Products.Evaluations;
using ResearcherAnalysisService.Products.ProductAccess;
using ResearcherAnalysisService.SourceData.Works;
using ResearcherAnalysisService.Tests.Infrastructure;

namespace ResearcherAnalysisService.Tests;

[Collection("Analysis Product SQL Server")]
public sealed class ArticleEvaluationProcessorTests(AnalysisProductSqlServerFixture fixture)
{
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

    private static ArticleEvaluationProcessor Processor(
        AsyncServiceScope scope,
        AnalysisDbContext database,
        IAcademicProductAccessService access) => new(
            database,
            new ArticleEvaluationServiceClient(null!,
                Microsoft.Extensions.Options.Options.Create(new ArticleEvaluationOptions())),
            scope.ServiceProvider.GetRequiredService<AnalysisSourceLock>(),
            access,
            NullLogger<ArticleEvaluationProcessor>.Instance);

    private static async Task<ArticleEvaluationWorkItem> SeedCalibrationAsync(
        AnalysisDbContext database)
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
        evaluationCase.RequestPayloadJson = JsonSerializer.Serialize(new { Source = source });
        ArticleEvaluationWorkItem item = Item(evaluationCase,
            ArticleEvaluationTaskKinds.Calibration);
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

    private static ArticleEvaluationWorkItem Item(ArticleEvaluationCase evaluationCase, string phase)
    {
        ArticleEvaluationProfile profile = new("missing-test-profile", "Test profile", "fake",
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
