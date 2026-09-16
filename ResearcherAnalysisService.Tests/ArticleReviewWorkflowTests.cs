using System.Text.Json;
using System.Net.Http.Json;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using AcademicCollector.Analysis.Contracts;
using ResearcherAnalysisService.Products.Api.Contracts;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Configuration;
using ResearcherAnalysisService.Integrations.Gemini;
using ResearcherAnalysisService.Products.ArticleReviews;
using ResearcherAnalysisService.Products.ArticleSummaries;
using ResearcherAnalysisService.Products.Data;
using ResearcherAnalysisService.SourceData.Researchers;
using ResearcherAnalysisService.SourceData.Works;

using ResearcherAnalysisService.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ResearcherAnalysisService.Tests;

[Collection("Analysis Product SQL Server")]
public sealed class ArticleReviewWorkflowTests(AnalysisProductSqlServerFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task ReviewAsync_CachesByBaseAndPolicy_ForceAppendsAndPreservesSourceCoverage()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        SeededArticle seeded = await SeedAsync(database, "en", false, null, baseRunMarkedPartial: true);
        StagedReviewHarness handler = ReviewHandler();
        ArticleReviewWorkflow workflow = Workflow(database, handler);

        var first = await workflow.ReviewAsync(seeded.PersonelId, seeded.CanonicalWorkId, "en", false, default);
        var reused = await workflow.ReviewAsync(seeded.PersonelId, seeded.CanonicalWorkId, "en", false, default);
        var forced = await workflow.ReviewAsync(seeded.PersonelId, seeded.CanonicalWorkId, "en", true, default);

        Assert.False(first.Reused);
        Assert.True(reused.Reused);
        Assert.Equal(first.ReviewRunId, reused.ReviewRunId);
        Assert.NotEqual(first.ReviewRunId, forced.ReviewRunId);
        Assert.Equal(10, handler.DispatchCount);
        Assert.False(first.Report.SourceCoverage.IsPartial);
        Assert.Null(first.Report.SourceCoverage.ScopeReason);
        Assert.Equal(2, await database.CanonicalArticleReviewRuns.CountAsync(run => run.BaseAnalysisRunId == seeded.BaseRunId));
        Assert.Equal(2, await database.CanonicalArticleReviewFindings.CountAsync(finding =>
            finding.CanonicalArticleReviewRun!.BaseAnalysisRunId == seeded.BaseRunId));
        Assert.Equal(2, await database.CanonicalArticleReviewEvidence.CountAsync(evidence =>
            evidence.CanonicalArticleReviewFinding!.CanonicalArticleReviewRun!.BaseAnalysisRunId == seeded.BaseRunId));
    }

    [Fact]
    public async Task ReviewAsync_OldPolicyRunIsNotReusedByCurrentPolicyWithSameModelSettings()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        SeededArticle seeded = await SeedAsync(database, "en", false, null, false);
        StagedReviewHarness handler = ReviewHandler();

        var old = await Workflow(database, handler, "article-specialist-review-policy-v2")
            .ReviewAsync(seeded.PersonelId, seeded.CanonicalWorkId, "en", false, default);
        var current = await Workflow(database, handler)
            .ReviewAsync(seeded.PersonelId, seeded.CanonicalWorkId, "en", false, default);
        int requestsBeforeReuse = handler.DispatchCount;
        var reused = await Workflow(database, handler)
            .ReviewAsync(seeded.PersonelId, seeded.CanonicalWorkId, "en", false, default);

        Assert.False(old.Reused);
        Assert.False(current.Reused);
        Assert.True(reused.Reused);
        Assert.NotEqual(old.ReviewRunId, current.ReviewRunId);
        Assert.Equal(current.ReviewRunId, reused.ReviewRunId);
        Assert.Equal(10, requestsBeforeReuse);
        Assert.Equal(requestsBeforeReuse, handler.DispatchCount);
    }

    [Fact]
    public async Task GetLatestAsync_NewBaseOrPolicyMarksSavedReviewStaleWithoutNetworkOrWrites()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        SeededArticle seeded = await SeedAsync(database, "en", false, null, false);
        StagedReviewHarness handler = ReviewHandler();
        ArticleReviewWorkflow workflow = Workflow(database, handler);
        await workflow.ReviewAsync(seeded.PersonelId, seeded.CanonicalWorkId, "en", false, default);
        await AddNewBaseAsync(database, seeded, "en");
        int before = await database.CanonicalArticleReviewRuns.CountAsync();

        CanonicalArticleReviewResponse staleBase = (await workflow.GetLatestAsync(
            seeded.PersonelId, seeded.CanonicalWorkId, "en", default))!;
        ArticleReviewWorkflow changedPolicy = Workflow(database, handler, "article-specialist-review-policy-v2");
        CanonicalArticleReviewResponse staleBoth = (await changedPolicy.GetLatestAsync(
            seeded.PersonelId, seeded.CanonicalWorkId, "en", default))!;

        Assert.True(staleBase.IsStale);
        Assert.Single(staleBase.StaleReasons);
        Assert.True(staleBoth.IsStale);
        Assert.Equal(2, staleBoth.StaleReasons.Count);
        Assert.Equal(5, handler.DispatchCount);
        Assert.Equal(before, await database.CanonicalArticleReviewRuns.CountAsync());

        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(
            geminiHandler: new SuccessfulReviewGeminiHandler(),
            usageDatabase: fixture.ConnectionString);
        using HttpResponseMessage combined = await host.Client.PostAsJsonAsync(
            "/api/v1/articles/analysis",
            new { PersonelID = seeded.PersonelId, seeded.CanonicalWorkId, Language = "en" });
        combined.EnsureSuccessStatusCode();
        CanonicalArticleAnalysisResponse envelope =
            (await combined.Content.ReadFromJsonAsync<CanonicalArticleAnalysisResponse>())!;
        Assert.True(envelope.Review!.IsStale);
        Assert.Single(envelope.Review.StaleReasons);
        Assert.Equal(before, await database.CanonicalArticleReviewRuns.CountAsync());
    }

    [Fact]
    public async Task ReviewAndRead_RequireCurrentResearcherAssociation()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        SeededArticle seeded = await SeedAsync(database, "en", true, "Only the saved abstract was analyzed.", false, "abstract");
        StagedReviewHarness handler = ReviewHandler();
        ArticleReviewWorkflow workflow = Workflow(database, handler);
        CanonicalArticleReviewResponse created = await workflow.ReviewAsync(
            seeded.PersonelId, seeded.CanonicalWorkId, "en", false, default);
        Assert.True(created.Report.SourceCoverage.IsPartial);
        Assert.Equal("Only the saved abstract was analyzed.", created.Report.SourceCoverage.ScopeReason);

        await using (DbContext sourceDatabase = fixture.CreateSeedContext())
        {
            CanonicalResearcherWork association = await sourceDatabase.Set<CanonicalResearcherWork>()
                .SingleAsync(value => value.PersonelId == seeded.PersonelId &&
                    value.CanonicalWorkId == seeded.CanonicalWorkId);
            sourceDatabase.Remove(association);
            await sourceDatabase.SaveChangesAsync();
        }
        database.ChangeTracker.Clear();

        Assert.Null(await workflow.GetLatestAsync(seeded.PersonelId, seeded.CanonicalWorkId, "en", default));
        await Assert.ThrowsAsync<ArticleReviewUnavailableException>(() => workflow.ReviewAsync(
            seeded.PersonelId, seeded.CanonicalWorkId, "en", false, default));
        Assert.Equal(5, handler.DispatchCount);
    }

    [Fact]
    public async Task ReviewAsync_CaseDistinctFindingIdsPersistUnderBinaryCollation()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        SeededArticle seeded = await SeedAsync(database, "en", false, null, false);
        StagedReviewHarness handler = ReviewHandler(caseDistinct: true);

        CanonicalArticleReviewResponse result = await Workflow(database, handler).ReviewAsync(
            seeded.PersonelId, seeded.CanonicalWorkId, "en", false, default);

        Assert.Equal(2, result.Report.Coverage.SupportedFindings);
        Assert.Equal(2, await database.CanonicalArticleReviewFindings.CountAsync(finding =>
            finding.CanonicalArticleReviewRunId == result.ReviewRunId));
    }

    [Fact]
    public async Task ReviewAsync_LastRoleOutputLimit_SplitsAndCompletesWithoutRepeatingEarlierRoles()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        SeededArticle seeded = await SeedAsync(database, "en", false, null, false);
        StagedReviewHarness handler = new() { TeachingFindingCount = 3, OutputLimitTeachingRoot = true };

        CanonicalArticleReviewResponse result = await Workflow(database, handler).ReviewAsync(
            seeded.PersonelId, seeded.CanonicalWorkId, "en", false, default);

        Assert.Equal(3, result.Report.Reviews.Single(value => value.Role == "teaching").Findings.Count);
        Assert.Equal([3, 1, 2], handler.VerificationBatchSizes.Skip(1));
        Assert.Equal(1, handler.GenerationRoles.Count(value => value == "method"));
        Assert.Equal(1, handler.GenerationRoles.Count(value => value == "teaching"));
        Assert.Equal(4, result.Report.Coverage.ProcessedRoles);
        Assert.Equal(4, await database.ArticleReviewStageCheckpoints.CountAsync(value =>
            value.ArticleReviewWorkItem!.BaseAnalysisRunId == seeded.BaseRunId &&
            value.Stage == ArticleReviewStageKinds.Verification));
    }

    [Fact]
    public async Task ReviewAsync_QuoteFailure_RetryResumesOnlyMissingRole()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        SeededArticle seeded = await SeedAsync(database, "en", false, null, false);
        StagedReviewHarness handler = new() { FailTeachingQuoteOnce = true };
        ArticleReviewWorkflow workflow = Workflow(database, handler);

        await Assert.ThrowsAsync<ArticleReviewAnalysisException>(() => workflow.ReviewAsync(
            seeded.PersonelId, seeded.CanonicalWorkId, "en", true, default));
        using IServiceScope resumedScope = fixture.Services.CreateScope();
        AnalysisDbContext resumedDatabase = resumedScope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        CanonicalArticleReviewResponse resumed = await Workflow(resumedDatabase, handler).ReviewAsync(
            seeded.PersonelId, seeded.CanonicalWorkId, "en", false, default);

        Assert.Equal(4, resumed.Report.Coverage.ProcessedRoles);
        Assert.Equal(1, handler.GenerationRoles.Count(value => value == "method"));
        Assert.Equal(1, handler.GenerationRoles.Count(value => value == "quantitative"));
        Assert.Equal(1, handler.GenerationRoles.Count(value => value == "claim_evidence"));
        Assert.Equal(1, handler.GenerationRoles.Count(value => value == "teaching"));
        Assert.Single(await resumedDatabase.ArticleReviewWorkItems.Where(value => value.GenerationNonce != Guid.Empty)
            .ToListAsync());
        Assert.Equal("Completed", (await resumedDatabase.ArticleReviewWorkItems.OrderByDescending(value => value.Id)
            .FirstAsync()).Status);
    }

    [Fact]
    public async Task ReviewAsync_GenerationOutputLimit_UsesOneMediumRecoveryAndPersistsBothAttempts()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        SeededArticle seeded = await SeedAsync(database, "en", false, null, false);
        StagedReviewHarness handler = new() { OutputLimitMethodGeneration = true };

        CanonicalArticleReviewResponse result = await Workflow(database, handler).ReviewAsync(
            seeded.PersonelId, seeded.CanonicalWorkId, "en", false, default);

        Assert.Equal(["high", "medium"], handler.GenerationThinkingLevels.Take(2));
        Assert.Equal(4, result.Report.Coverage.ProcessedRoles);
        List<ArticleReviewStageCheckpoint> checkpoints = await database.ArticleReviewStageCheckpoints
            .Where(value => value.ArticleReviewWorkItem!.BaseAnalysisRunId == seeded.BaseRunId &&
                value.Stage == ArticleReviewStageKinds.Generation && value.Role == "method")
            .OrderBy(value => value.Ordinal).ToListAsync();
        Assert.Equal(2, checkpoints.Count);
        Assert.Equal("OutputLimit", checkpoints[0].Status);
        Assert.Equal("Completed", checkpoints[1].Status);
        Assert.Equal(checkpoints[0].BatchKey, checkpoints[1].ParentBatchKey);
        Assert.Equal(1, checkpoints[1].Ordinal);
        Assert.Contains(checkpoints[0].AttemptId!.Value.ToString(), checkpoints[1].RequestJson);
        Assert.All(checkpoints, value => Assert.Equal(0.001m, value.ActualCostUsd));
    }

    [Fact]
    public async Task ReviewAsync_CompletedMediumRecovery_IsReusedAfterLaterPreDispatchFailure()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        SeededArticle seeded = await SeedAsync(database, "en", false, null, false);
        StagedReviewHarness handler = new()
        {
            OutputLimitMethodGeneration = true,
            FailTeachingQuoteOnce = true
        };

        await Assert.ThrowsAsync<ArticleReviewAnalysisException>(() => Workflow(database, handler).ReviewAsync(
            seeded.PersonelId, seeded.CanonicalWorkId, "en", false, default));
        int before = handler.GenerationRoles.Count;
        using IServiceScope resumedScope = fixture.Services.CreateScope();
        AnalysisDbContext resumedDatabase = resumedScope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        CanonicalArticleReviewResponse resumed = await Workflow(resumedDatabase, handler).ReviewAsync(
            seeded.PersonelId, seeded.CanonicalWorkId, "en", false, default);

        Assert.Equal(4, resumed.Report.Coverage.ProcessedRoles);
        Assert.Equal(before + 1, handler.GenerationRoles.Count);
        Assert.Equal(2, handler.GenerationRoles.Count(value => value == "method"));
    }

    [Fact]
    public async Task ReviewAsync_SecondGenerationOutputLimit_StopsAfterTwoDispatches()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        SeededArticle seeded = await SeedAsync(database, "en", false, null, false);
        StagedReviewHarness handler = new()
        {
            OutputLimitMethodGeneration = true,
            OutputLimitMethodRecovery = true
        };
        ArticleReviewWorkflow workflow = Workflow(database, handler);

        await Assert.ThrowsAsync<ArticleReviewAnalysisException>(() => workflow.ReviewAsync(
            seeded.PersonelId, seeded.CanonicalWorkId, "en", false, default));
        await Assert.ThrowsAsync<ArticleReviewUnavailableException>(() => workflow.ReviewAsync(
            seeded.PersonelId, seeded.CanonicalWorkId, "en", false, default));

        Assert.Equal(["high", "medium"], handler.GenerationThinkingLevels);
        Assert.Equal(2, await database.ArticleReviewStageCheckpoints.CountAsync(value =>
            value.ArticleReviewWorkItem!.BaseAnalysisRunId == seeded.BaseRunId && value.AttemptId != null));
    }

    [Theory]
    [InlineData("model")]
    [InlineData("pricing")]
    [InlineData("cost")]
    public async Task ReviewAsync_UnattributedGenerationOutputLimit_DoesNotUnlockRecovery(string fault)
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        SeededArticle seeded = await SeedAsync(database, "en", false, null, false);
        StagedReviewHarness handler = new()
        {
            OutputLimitMethodGeneration = true,
            GenerationOutputLimitAttributionFault = fault
        };

        await Assert.ThrowsAsync<ArticleReviewAnalysisException>(() => Workflow(database, handler).ReviewAsync(
            seeded.PersonelId, seeded.CanonicalWorkId, "en", false, default));

        Assert.Single(handler.GenerationThinkingLevels);
        ArticleReviewStageCheckpoint checkpoint = await database.ArticleReviewStageCheckpoints.SingleAsync(value =>
            value.ArticleReviewWorkItem!.BaseAnalysisRunId == seeded.BaseRunId);
        Assert.Equal("Unknown", checkpoint.Status);
        Assert.Null(checkpoint.ActualCostUsd);
    }

    [Fact]
    public async Task ReviewAsync_GenerationRecovery_RespectsCumulativeCallCap()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        SeededArticle seeded = await SeedAsync(database, "en", false, null, false);
        StagedReviewHarness handler = new() { OutputLimitMethodGeneration = true };

        await Assert.ThrowsAsync<ArticleReviewUnavailableException>(() => Workflow(
            database, handler, maximumCalls: 1).ReviewAsync(
                seeded.PersonelId, seeded.CanonicalWorkId, "en", false, default));

        Assert.Equal(["high"], handler.GenerationThinkingLevels);
        Assert.Single(await database.ArticleReviewStageCheckpoints.Where(value =>
            value.ArticleReviewWorkItem!.BaseAnalysisRunId == seeded.BaseRunId && value.AttemptId != null)
            .ToListAsync());
    }

    [Fact]
    public async Task ReviewAsync_UnknownDispatchedAttempt_IsNeverBlindlyRetried()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        SeededArticle seeded = await SeedAsync(database, "en", false, null, false);
        StagedReviewHarness handler = new() { UnknownMethodGeneration = true };
        ArticleReviewWorkflow workflow = Workflow(database, handler);

        await Assert.ThrowsAsync<ArticleReviewAnalysisException>(() => workflow.ReviewAsync(
            seeded.PersonelId, seeded.CanonicalWorkId, "en", false, default));
        await Assert.ThrowsAsync<ArticleReviewUnavailableException>(() => workflow.ReviewAsync(
            seeded.PersonelId, seeded.CanonicalWorkId, "en", false, default));

        Assert.Single(handler.GenerationRoles);
        ArticleReviewStageCheckpoint checkpoint = await database.ArticleReviewStageCheckpoints.SingleAsync(value =>
            value.ArticleReviewWorkItem!.BaseAnalysisRunId == seeded.BaseRunId);
        Assert.Equal("Unknown", checkpoint.Status);
        Assert.NotNull(checkpoint.AttemptId);
        Assert.Null(await database.CanonicalArticleReviewRuns.FirstOrDefaultAsync(value =>
            value.BaseAnalysisRunId == seeded.BaseRunId));
    }

    [Fact]
    public async Task ReviewAsync_CumulativeSpendCannotResetOnRepeatRequest()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        SeededArticle seeded = await SeedAsync(database, "en", false, null, false);
        StagedReviewHarness handler = new() { ActualCostUsd = 0.03m };
        ArticleReviewWorkflow workflow = Workflow(database, handler, maximumSpendUsd: 0.06m);

        await Assert.ThrowsAsync<ArticleReviewUnavailableException>(() => workflow.ReviewAsync(
            seeded.PersonelId, seeded.CanonicalWorkId, "en", false, default));
        await Assert.ThrowsAsync<ArticleReviewUnavailableException>(() => workflow.ReviewAsync(
            seeded.PersonelId, seeded.CanonicalWorkId, "en", false, default));

        Assert.Single(handler.GenerationRoles);
        Assert.Equal(1, await database.ArticleReviewStageCheckpoints.CountAsync(value =>
            value.ArticleReviewWorkItem!.BaseAnalysisRunId == seeded.BaseRunId && value.AttemptId != null));
    }

    [Fact]
    public async Task ReviewAsync_OlderUnknownThenForcedSuccess_OrdinaryRequestUsesNewestSuccess()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        SeededArticle seeded = await SeedAsync(database, "en", false, null, false);
        StagedReviewHarness handler = new() { UnknownMethodGeneration = true };

        await Assert.ThrowsAsync<ArticleReviewAnalysisException>(() => Workflow(database, handler).ReviewAsync(
            seeded.PersonelId, seeded.CanonicalWorkId, "en", false, default));
        handler.UnknownMethodGeneration = false;
        CanonicalArticleReviewResponse forced = await Workflow(database, handler).ReviewAsync(
            seeded.PersonelId, seeded.CanonicalWorkId, "en", true, default);
        int dispatches = handler.GenerationRoles.Count;
        CanonicalArticleReviewResponse ordinary = await Workflow(database, handler).ReviewAsync(
            seeded.PersonelId, seeded.CanonicalWorkId, "en", false, default);

        Assert.True(ordinary.Reused);
        Assert.Equal(forced.ReviewRunId, ordinary.ReviewRunId);
        Assert.Equal(dispatches, handler.GenerationRoles.Count);
    }

    [Fact]
    public async Task ReviewAsync_OutputLimitSplit_RespectsCumulativeCallCapAcrossRetry()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        SeededArticle seeded = await SeedAsync(database, "en", false, null, false);
        StagedReviewHarness handler = new() { TeachingFindingCount = 3, OutputLimitTeachingRoot = true };
        ArticleReviewWorkflow workflow = Workflow(database, handler, maximumCalls: 6);

        await Assert.ThrowsAsync<ArticleReviewUnavailableException>(() => workflow.ReviewAsync(
            seeded.PersonelId, seeded.CanonicalWorkId, "en", false, default));
        int dispatched = handler.GenerationRoles.Count + handler.VerificationBatchSizes.Count;
        await Assert.ThrowsAsync<ArticleReviewUnavailableException>(() => workflow.ReviewAsync(
            seeded.PersonelId, seeded.CanonicalWorkId, "en", false, default));

        Assert.Equal(6, dispatched);
        Assert.Equal(dispatched, handler.GenerationRoles.Count + handler.VerificationBatchSizes.Count);
        Assert.Equal(6, await database.ArticleReviewStageCheckpoints.CountAsync(value =>
            value.ArticleReviewWorkItem!.BaseAnalysisRunId == seeded.BaseRunId && value.AttemptId != null));
    }

    [Fact]
    public async Task ReviewAsync_ConfigAndPolicyChanges_DoNotReusePartialWork()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        SeededArticle seeded = await SeedAsync(database, "en", false, null, false);
        StagedReviewHarness handler = new() { FailTeachingQuoteOnce = true };
        await Assert.ThrowsAsync<ArticleReviewAnalysisException>(() => Workflow(database, handler).ReviewAsync(
            seeded.PersonelId, seeded.CanonicalWorkId, "en", false, default));

        handler.UseAlternateConfiguration();
        await Workflow(database, handler).ReviewAsync(seeded.PersonelId, seeded.CanonicalWorkId, "en", false, default);
        await Workflow(database, handler, "article-specialist-review-policy-v2").ReviewAsync(
            seeded.PersonelId, seeded.CanonicalWorkId, "en", false, default);

        List<ArticleReviewWorkItem> items = await database.ArticleReviewWorkItems.Where(value =>
            value.BaseAnalysisRunId == seeded.BaseRunId).OrderBy(value => value.Id).ToListAsync();
        Assert.Equal(3, items.Count);
        Assert.Equal(2, items.Select(value => value.SettingsFingerprint).Distinct().Count());
        Assert.Equal(2, items.Select(value => value.PolicyVersion).Distinct().Count());
        Assert.Equal(3, handler.GenerationRoles.Count(value => value == "method"));
    }

    [Fact]
    public async Task ReviewAsync_HeldApplicationLock_AllowsOnlyOneOwner()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        SeededArticle seeded = await SeedAsync(database, "en", false, null, false);
        StagedReviewHarness handler = new();
        await using SqlApplicationLock? held = await SqlApplicationLock.TryAcquireAsync(
            fixture.ConnectionString, $"AcademicCollector.ArticleReview.{seeded.CanonicalWorkId}.en", 0, default);
        Assert.NotNull(held);

        await Assert.ThrowsAsync<ArticleReviewBusyException>(() => Workflow(database, handler).ReviewAsync(
            seeded.PersonelId, seeded.CanonicalWorkId, "en", false, default));
        Assert.Empty(handler.GenerationRoles);
    }

    [Fact]
    public async Task ReviewAsync_SourceSpanMutatesAfterLastCall_RejectsFinalReport()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        SeededArticle seeded = await SeedAsync(database, "en", false, null, false);
        StagedReviewHarness handler = new() { TeachingFindingCount = 1 };
        handler.OnTeachingVerification = () =>
        {
            using IServiceScope mutationScope = fixture.Services.CreateScope();
            AnalysisDbContext mutationDatabase = mutationScope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
            ArticleSourceSpanSnapshot span = mutationDatabase.ArticleSourceSpans.Single(value =>
                value.ArticleSourceSnapshotId == seeded.SourceSnapshotId);
            span.Text += " changed";
            span.EndOffset = span.StartOffset + span.Text.Length;
            mutationDatabase.SaveChanges();
        };

        await Assert.ThrowsAsync<ArticleReviewSourceChangedException>(() => Workflow(database, handler).ReviewAsync(
            seeded.PersonelId, seeded.CanonicalWorkId, "en", false, default));
        Assert.False(await database.CanonicalArticleReviewRuns.AnyAsync(value =>
            value.BaseAnalysisRunId == seeded.BaseRunId));
    }

    [Fact]
    public async Task HostEndpoints_GenerateAndReadThroughFakeAnalysisService()
    {
        SeededArticle seeded;
        using (IServiceScope scope = fixture.Services.CreateScope())
        {
            AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
            seeded = await SeedAsync(database, "en", false, null, false);
        }
        await using AnalysisTestHost host = await AnalysisTestHost.StartAsync(
            geminiHandler: new SuccessfulReviewGeminiHandler(),
            usageDatabase: fixture.ConnectionString);

        using HttpResponseMessage generated = await host.Client.PostAsJsonAsync(
            "/api/v1/articles/review/generate",
            new { PersonelID = seeded.PersonelId, seeded.CanonicalWorkId, Language = "en" });
        Assert.True(generated.IsSuccessStatusCode, await generated.Content.ReadAsStringAsync());
        CanonicalArticleReviewResponse created =
            (await generated.Content.ReadFromJsonAsync<CanonicalArticleReviewResponse>())!;
        using HttpResponseMessage read = await host.Client.PostAsJsonAsync(
            "/api/v1/articles/analysis",
            new { PersonelID = seeded.PersonelId, seeded.CanonicalWorkId, Language = "en" });
        read.EnsureSuccessStatusCode();
        CanonicalArticleReviewResponse saved =
            (await read.Content.ReadFromJsonAsync<CanonicalArticleAnalysisResponse>())!.Review!;
        using HttpResponseMessage isolated = await host.Client.PostAsJsonAsync(
            "/api/v1/articles/analysis",
            new { PersonelID = "other-researcher", seeded.CanonicalWorkId, Language = "en" });

        Assert.Equal(created.ReviewRunId, saved.ReviewRunId);
        Assert.Equal(seeded.PersonelId, saved.PersonelId);
        Assert.Equal(HttpStatusCode.NotFound, isolated.StatusCode);
    }

    [Fact]
    public async Task ReviewAsync_NewerBaseDuringModelCallRejectsAndPreservesLastGoodReview()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        SeededArticle seeded = await SeedAsync(database, "en", false, null, false);
        await Workflow(database, ReviewHandler()).ReviewAsync(
            seeded.PersonelId, seeded.CanonicalWorkId, "en", false, default);
        int before = await database.CanonicalArticleReviewRuns.CountAsync(run =>
            run.CanonicalWorkId == seeded.CanonicalWorkId);
        StagedReviewHarness racingHandler = ReviewHandler(onRequest: () =>
        {
            using IServiceScope racingScope = fixture.Services.CreateScope();
            AnalysisDbContext racingDatabase = racingScope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
            AddNewBaseAsync(racingDatabase, seeded, "en").GetAwaiter().GetResult();
        });

        await Assert.ThrowsAsync<ArticleReviewSourceChangedException>(() => Workflow(database, racingHandler)
            .ReviewAsync(seeded.PersonelId, seeded.CanonicalWorkId, "en", true, default));

        database.ChangeTracker.Clear();
        Assert.Equal(before, await database.CanonicalArticleReviewRuns.CountAsync(run =>
            run.CanonicalWorkId == seeded.CanonicalWorkId));
    }

    private ArticleReviewWorkflow Workflow(
        AnalysisDbContext database,
        StagedReviewHarness harness,
        string policy = ArticleReviewer.DefaultPolicyVersion,
        decimal maximumSpendUsd = 1m,
        int maximumCalls = 24)
    {
        AiOptions ai = harness.CreateOptions();
        IOptions<AiOptions> aiOptions = Options.Create(ai);
        ArticleReviewDispatchContext dispatchContext = new();
        harness.Attach(dispatchContext, ai);
        ArticleReviewer reviewer = new(harness, harness, aiOptions);
        ArticleReviewStageExecutor executor = new(
            reviewer, harness, harness, dispatchContext, aiOptions);
        return new(database,
            new ArticleReviewServiceClient(reviewer, executor),
            new AnalysisSourceLock(database),
            Options.Create(new ArticleReviewOptions
            {
                PolicyVersion = policy,
                MaximumSpendUsd = maximumSpendUsd,
                MaximumProviderCalls = maximumCalls
            }));
    }

    private static StagedReviewHarness ReviewHandler(
        bool caseDistinct = false,
        Action? onRequest = null) => new()
        {
            CaseDistinct = caseDistinct,
            OnFirstDispatch = onRequest
        };

    private async Task<SeededArticle> SeedAsync(
        AnalysisDbContext database,
        string language,
        bool isPartial,
        string? scopeReason,
        bool baseRunMarkedPartial,
        string sourceKind = "pdf")
    {
        string personelId = "review-" + Guid.NewGuid().ToString("N");
        SyntheticCanonicalSource canonical = await fixture.SeedCanonicalSourceAsync(personelId);
        database.ChangeTracker.Clear();
        int? pageNumber = sourceKind == "pdf" ? 1 : null;
        IReadOnlyList<ArticlePage> pages = [new(pageNumber, sourceKind == "abstract"
            ? "The abstract reports a sample of 40 participants."
            : "The full text reports a sample of 40 participants and its study method.")];
        IReadOnlyList<ArticleSourceSpan> spans = ArticleSourceCatalog.Create(pages);
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(pages, JsonOptions)))).ToLowerInvariant();
        ArticleSourceSnapshot source = new()
        {
            CanonicalWorkId = canonical.CanonicalWorkId,
            ExtractedTextHash = hash,
            SourceKind = sourceKind,
            ExtractionVersion = sourceKind + "-v1",
            CreatedAt = DateTimeOffset.UtcNow,
            Pages = pages.Select((page, index) => new ArticleSourcePageSnapshot
                { Ordinal = index, PageNumber = page.PageNumber, Text = page.Text }).ToList(),
            Spans = spans.Select((span, index) => new ArticleSourceSpanSnapshot
            {
                Ordinal = index,
                SourceId = span.SourceId,
                PageNumber = span.PageNumber,
                StartOffset = span.StartOffset,
                EndOffset = span.EndOffset,
                Text = span.Text
            }).ToList()
        };
        SummarizeArticleRequest sourceRequest = new(language, sourceKind, hash, sourceKind + "-v1",
            pages, 1, isPartial, scopeReason) { SourceSpans = spans };
        SavedArticleSummary saved = new()
        {
            OriginalAcademicWorkId = canonical.AcademicWorkId,
            PersonelId = personelId,
            SavedAt = DateTimeOffset.UtcNow,
            SourceHash = hash,
            SourceKind = sourceKind,
            ExtractionVersion = sourceKind + "-v1",
            SnapshotJson = JsonSerializer.Serialize(sourceRequest, JsonOptions),
            ReportJson = "{}"
        };
        database.AddRange(source, saved);
        await database.SaveChangesAsync();
        CanonicalArticleAnalysisRun run = BaseRun(canonical.CanonicalWorkId, source.Id, saved.Id,
            language, baseRunMarkedPartial, canonical.SourceIdentityHash);
        database.CanonicalArticleAnalysisRuns.Add(run);
        await database.SaveChangesAsync();
        return new(personelId, canonical.CanonicalWorkId, canonical.AcademicWorkId,
            canonical.SourceIdentityHash, run.Id, source.Id);
    }

    private static async Task AddNewBaseAsync(
        AnalysisDbContext database,
        SeededArticle seeded,
        string language)
    {
        SavedArticleSummary previous = await database.ArticleSummaries.AsNoTracking()
            .Where(summary => database.CanonicalArticleAnalysisRuns.Any(run =>
                run.Id == seeded.BaseRunId && run.SavedArticleSummaryId == summary.Id)).SingleAsync();
        SavedArticleSummary saved = new()
        {
            OriginalAcademicWorkId = previous.OriginalAcademicWorkId,
            PersonelId = previous.PersonelId,
            SavedAt = DateTimeOffset.UtcNow,
            SourceHash = previous.SourceHash,
            SourceKind = previous.SourceKind,
            ExtractionVersion = previous.ExtractionVersion,
            SnapshotJson = previous.SnapshotJson,
            ReportJson = previous.ReportJson
        };
        database.ArticleSummaries.Add(saved);
        await database.SaveChangesAsync();
        database.CanonicalArticleAnalysisRuns.Add(BaseRun(
            seeded.CanonicalWorkId, seeded.SourceSnapshotId, saved.Id, language, false,
            seeded.SourceIdentityHash));
        await database.SaveChangesAsync();
    }

    private static CanonicalArticleAnalysisRun BaseRun(
        int canonicalWorkId,
        long sourceId,
        long summaryId,
        string language,
        bool partial,
        string sourceIdentityHash) => new()
    {
        CanonicalWorkId = canonicalWorkId,
        SourceIdentityHash = sourceIdentityHash,
        ArticleSourceSnapshotId = sourceId,
        SavedArticleSummaryId = summaryId,
        AnalyzedAt = DateTimeOffset.UtcNow,
        SourceAcquiredAt = DateTimeOffset.UtcNow,
        SourceOrigin = "Synthetic",
        Language = language,
        PolicyVersion = "article-summary-v1",
        Model = "synthetic",
        PromptVersion = "summary-v1",
        ExtractionMethod = "pdf_text",
        ProcessedChunks = 1,
        TotalChunks = 1,
        ProcessedPages = 1,
        TextBearingPages = 1,
        TotalPages = 1,
        SelectedClaimsOmitted = partial ? 1 : 0,
        IsPartial = partial,
        ScopeReason = partial ? "One summary claim was omitted." : null,
        OmissionReasonsJson = "[]",
        VerificationStatus = "automatically_checked",
        VerificationModel = "synthetic",
        VerificationPromptVersion = "verify-v1"
    };

    private sealed class StagedReviewHarness : IArticleReviewRecoveryGenerator, IArticleReviewVerifier
    {
        private const string Model = "gemini-3.8-flash";
        private const string Pricing = "gemini-3.8-flash-standard-through-2026-12-31";
        private ArticleReviewDispatchContext _dispatchContext = null!;
        private AiOptions _options = null!;
        private bool _failedTeachingQuote;
        private bool _returnedOutputLimit;
        private bool _returnedGenerationOutputLimit;
        private bool _firstDispatchRaised;
        private bool _alternateConfiguration;

        public bool CaseDistinct { get; init; }
        public bool FailTeachingQuoteOnce { get; init; }
        public bool OutputLimitTeachingRoot { get; init; }
        public bool OutputLimitMethodGeneration { get; init; }
        public bool OutputLimitMethodRecovery { get; init; }
        public string? GenerationOutputLimitAttributionFault { get; init; }
        public bool UnknownMethodGeneration { get; set; }
        public Action? OnFirstDispatch { get; init; }
        public Action? OnTeachingVerification { get; set; }
        public int TeachingFindingCount { get; init; }
        public decimal ActualCostUsd { get; init; } = 0.001m;
        public int DispatchCount => GenerationRoles.Count + VerificationBatchSizes.Count;
        public List<string> GenerationRoles { get; } = [];
        public List<string> GenerationThinkingLevels { get; } = [];
        public List<int> VerificationBatchSizes { get; } = [];

        public AiOptions CreateOptions() => new()
        {
            ArticleProvider = "Gemini",
            ArticleModel = Model,
            ArticleVerifierModel = Model,
            ArticleGenerationThinkingLevel = ArticleReviewGenerationRecovery.InitialThinkingLevel,
            ArticleVerifierThinkingLevel = _alternateConfiguration ? "medium" : "high",
            ArticleContextTokens = 131072,
            ArticleMaxOutputTokens = 8192,
            ArticleVerifierMaxOutputTokens = 8192,
            ArticleReviewMaximumInputBytes = 100000,
            ArticleReviewTimeoutSeconds = 300
        };

        public void Attach(ArticleReviewDispatchContext dispatchContext, AiOptions options)
        {
            _dispatchContext = dispatchContext;
            _options = options;
        }

        public void UseAlternateConfiguration() => _alternateConfiguration = true;

        public Task<GeneratedArticleReviewPass> GenerateAsync(string role, string language,
            string sourceKind, IReadOnlyList<ArticleSourceSpan> sourceSpans,
            CancellationToken cancellationToken) => GenerateAsync(role, language, sourceKind,
                sourceSpans, ArticleReviewGenerationRecovery.InitialThinkingLevel, cancellationToken);

        public Task<GeneratedArticleReviewPass> GenerateAsync(string role, string language,
            string sourceKind, IReadOnlyList<ArticleSourceSpan> sourceSpans, string thinkingLevel,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GenerationRoles.Add(role);
            GenerationThinkingLevels.Add(thinkingLevel);
            if (!_firstDispatchRaised)
            {
                _firstDispatchRaised = true;
                OnFirstDispatch?.Invoke();
            }
            if (UnknownMethodGeneration && role == "method")
                throw new AnalysisUnavailableException("Synthetic unknown provider outcome.");

            bool initialLimit = OutputLimitMethodGeneration && role == "method" &&
                thinkingLevel == ArticleReviewGenerationRecovery.InitialThinkingLevel &&
                !_returnedGenerationOutputLimit;
            bool recoveryLimit = OutputLimitMethodRecovery && role == "method" &&
                thinkingLevel == ArticleReviewGenerationRecovery.RecoveryThinkingLevel;
            if (initialLimit || recoveryLimit)
            {
                _returnedGenerationOutputLimit = true;
                Capture("OutputLimit", GenerationOutputLimitAttributionFault);
                throw new InvalidAnalysisException(AnalysisFailure.OutputLimit);
            }

            Capture("Success");
            int findingCount = role == "method" ? 1 : role == "teaching" ? TeachingFindingCount : 0;
            ArticleSourceSpan span = sourceSpans.First();
            List<GeneratedArticleReviewFinding> findings = Enumerable.Range(1, findingCount)
                .Select(index => new GeneratedArticleReviewFinding(
                    $"F{index}", role,
                    role == "teaching" ? "teaching_adaptation" : "source_observation",
                    "The source directly supports this finding.",
                    role == "teaching" ? "Use this as a labelled teaching adaptation." : null,
                    [span.SourceId])).ToList();
            if (CaseDistinct && role == "method")
                findings.Add(new("f1", role, "review_question",
                    "The source directly supports this finding.",
                    "Could a reviewer examine the reported method?", [span.SourceId]));
            if (FailTeachingQuoteOnce && role == "claim_evidence" && !_failedTeachingQuote)
            {
                _failedTeachingQuote = true;
                _options.ArticleProvider = "Unavailable";
            }
            return Task.FromResult(new GeneratedArticleReviewPass(
                role, findings, Model, ArticleReviewPrompt.Version));
        }

        public Task<GeneratedArticleReviewVerification> VerifyAsync(string role, string language,
            IReadOnlyList<GeneratedArticleReviewFinding> findings,
            IReadOnlyList<ArticleSourceSpan> sourceSpans, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            VerificationBatchSizes.Add(findings.Count);
            if (role == "teaching") OnTeachingVerification?.Invoke();
            if (OutputLimitTeachingRoot && role == "teaching" && findings.Count == 3 &&
                !_returnedOutputLimit)
            {
                _returnedOutputLimit = true;
                Capture("OutputLimit");
                throw new InvalidAnalysisException(AnalysisFailure.OutputLimit);
            }
            Capture("Success");
            return Task.FromResult(new GeneratedArticleReviewVerification(
                findings.Select(value => new GeneratedArticleReviewVerdict(
                    value.FindingId, "supported", "Direct support.")).ToList(),
                Model, ArticleReviewVerificationPrompt.Version));
        }

        private void Capture(string outcome, string? attributionFault = null)
        {
            _dispatchContext.Capture(new GeminiUsageCompletion
            {
                UsageValidForAttribution = attributionFault is null,
                Outcome = outcome,
                ReturnedModel = attributionFault == "model" ? "gemini-3.8-flash-001" : Model,
                PricingVersion = attributionFault == "pricing" ? null : Pricing,
                EstimatedUsd = attributionFault == "cost" ? null : ActualCostUsd
            });
        }
    }

    private sealed class SuccessfulReviewGeminiHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using JsonDocument requestDocument = JsonDocument.Parse(body);
            string input = requestDocument.RootElement.GetProperty("contents")[0]
                .GetProperty("parts")[0].GetProperty("text").GetString()!;
            using JsonDocument inputDocument = JsonDocument.Parse(input);
            string role = inputDocument.RootElement.GetProperty("role").GetString()!;
            string answer = JsonSerializer.Serialize(new
            {
                role,
                findings = Array.Empty<object>()
            });
            return new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    candidates = new[]
                    {
                        new
                        {
                            content = new { parts = new[] { new { text = answer } } },
                            finishReason = "STOP"
                        }
                    },
                    usageMetadata = new
                    {
                        promptTokenCount = 100,
                        candidatesTokenCount = 20,
                        totalTokenCount = 120
                    },
                    modelVersion = "gemini-3.8-flash"
                })
            };
        }
    }

    private sealed record SeededArticle(
        string PersonelId,
        int CanonicalWorkId,
        int AcademicWorkId,
        string SourceIdentityHash,
        long BaseRunId,
        long SourceSnapshotId);
}
