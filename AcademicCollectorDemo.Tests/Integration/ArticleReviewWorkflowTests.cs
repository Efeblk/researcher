using System.Text.Json;
using System.Net.Http.Json;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using AcademicCollector.Analysis.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Analysis;
using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleReviews;
using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;
using AcademicCollectorDemo.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class ArticleReviewWorkflowTests(SqlServerFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task ReviewAsync_CachesByBaseAndPolicy_ForceAppendsAndPreservesSourceCoverage()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        SeededArticle seeded = await SeedAsync(database, "en", false, null, baseRunMarkedPartial: true);
        StubHttpHandler handler = ReviewHandler();
        ArticleReviewWorkflow workflow = Workflow(database, handler);

        var first = await workflow.ReviewAsync(seeded.PersonelId, seeded.CanonicalWorkId, "en", false, default);
        var reused = await workflow.ReviewAsync(seeded.PersonelId, seeded.CanonicalWorkId, "en", false, default);
        var forced = await workflow.ReviewAsync(seeded.PersonelId, seeded.CanonicalWorkId, "en", true, default);

        Assert.False(first.Reused);
        Assert.True(reused.Reused);
        Assert.Equal(first.ReviewRunId, reused.ReviewRunId);
        Assert.NotEqual(first.ReviewRunId, forced.ReviewRunId);
        Assert.Equal(23, handler.RequestCount);
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
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        SeededArticle seeded = await SeedAsync(database, "en", false, null, false);
        StubHttpHandler handler = ReviewHandler();

        var old = await Workflow(database, handler, "article-specialist-review-policy-v2")
            .ReviewAsync(seeded.PersonelId, seeded.CanonicalWorkId, "en", false, default);
        var current = await Workflow(database, handler)
            .ReviewAsync(seeded.PersonelId, seeded.CanonicalWorkId, "en", false, default);
        int requestsBeforeReuse = handler.RequestCount;
        var reused = await Workflow(database, handler)
            .ReviewAsync(seeded.PersonelId, seeded.CanonicalWorkId, "en", false, default);

        Assert.False(old.Reused);
        Assert.False(current.Reused);
        Assert.True(reused.Reused);
        Assert.NotEqual(old.ReviewRunId, current.ReviewRunId);
        Assert.Equal(current.ReviewRunId, reused.ReviewRunId);
        Assert.True(requestsBeforeReuse >= 22);
        Assert.Equal(requestsBeforeReuse + 1, handler.RequestCount);
    }

    [Fact]
    public async Task GetLatestAsync_NewBaseOrPolicyMarksSavedReviewStaleWithoutNetworkOrWrites()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        SeededArticle seeded = await SeedAsync(database, "en", false, null, false);
        StubHttpHandler handler = ReviewHandler();
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
        Assert.Equal(11, handler.RequestCount);
        Assert.Equal(before, await database.CanonicalArticleReviewRuns.CountAsync());
    }

    [Fact]
    public async Task ReviewAndRead_RequireCurrentResearcherAssociation()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        SeededArticle seeded = await SeedAsync(database, "en", true, "Only the saved abstract was analyzed.", false, "abstract");
        StubHttpHandler handler = ReviewHandler();
        ArticleReviewWorkflow workflow = Workflow(database, handler);
        CanonicalArticleReviewResponse created = await workflow.ReviewAsync(
            seeded.PersonelId, seeded.CanonicalWorkId, "en", false, default);
        Assert.True(created.Report.SourceCoverage.IsPartial);
        Assert.Equal("Only the saved abstract was analyzed.", created.Report.SourceCoverage.ScopeReason);

        CanonicalResearcherWork association = await database.CanonicalResearcherWorks.SingleAsync(value =>
            value.PersonelId == seeded.PersonelId && value.CanonicalWorkId == seeded.CanonicalWorkId);
        database.CanonicalResearcherWorks.Remove(association);
        await database.SaveChangesAsync();

        Assert.Null(await workflow.GetLatestAsync(seeded.PersonelId, seeded.CanonicalWorkId, "en", default));
        await Assert.ThrowsAsync<ArticleReviewUnavailableException>(() => workflow.ReviewAsync(
            seeded.PersonelId, seeded.CanonicalWorkId, "en", false, default));
        Assert.Equal(11, handler.RequestCount);
    }

    [Fact]
    public async Task ReviewAsync_CaseDistinctFindingIdsPersistUnderBinaryCollation()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        SeededArticle seeded = await SeedAsync(database, "en", false, null, false);
        StubHttpHandler handler = ReviewHandler(caseDistinct: true);

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
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        SeededArticle seeded = await SeedAsync(database, "en", false, null, false);
        StagedReviewHandler handler = new() { TeachingFindingCount = 3, OutputLimitTeachingRoot = true };

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
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        SeededArticle seeded = await SeedAsync(database, "en", false, null, false);
        StagedReviewHandler handler = new() { FailTeachingQuoteOnce = true };
        ArticleReviewWorkflow workflow = Workflow(database, handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => workflow.ReviewAsync(
            seeded.PersonelId, seeded.CanonicalWorkId, "en", true, default));
        using IServiceScope resumedScope = fixture.Services.CreateScope();
        AcademicDbContext resumedDatabase = resumedScope.ServiceProvider.GetRequiredService<AcademicDbContext>();
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
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        SeededArticle seeded = await SeedAsync(database, "en", false, null, false);
        StagedReviewHandler handler = new() { OutputLimitMethodGeneration = true };

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
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        SeededArticle seeded = await SeedAsync(database, "en", false, null, false);
        StagedReviewHandler handler = new()
        {
            OutputLimitMethodGeneration = true,
            FailTeachingQuoteOnce = true
        };

        await Assert.ThrowsAsync<HttpRequestException>(() => Workflow(database, handler).ReviewAsync(
            seeded.PersonelId, seeded.CanonicalWorkId, "en", false, default));
        int before = handler.GenerationRoles.Count;
        using IServiceScope resumedScope = fixture.Services.CreateScope();
        AcademicDbContext resumedDatabase = resumedScope.ServiceProvider.GetRequiredService<AcademicDbContext>();
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
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        SeededArticle seeded = await SeedAsync(database, "en", false, null, false);
        StagedReviewHandler handler = new()
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
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        SeededArticle seeded = await SeedAsync(database, "en", false, null, false);
        StagedReviewHandler handler = new()
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
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        SeededArticle seeded = await SeedAsync(database, "en", false, null, false);
        StagedReviewHandler handler = new() { OutputLimitMethodGeneration = true };

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
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        SeededArticle seeded = await SeedAsync(database, "en", false, null, false);
        StagedReviewHandler handler = new() { UnknownMethodGeneration = true };
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
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        SeededArticle seeded = await SeedAsync(database, "en", false, null, false);
        StagedReviewHandler handler = new();
        ArticleReviewWorkflow workflow = Workflow(database, handler, maximumSpendUsd: 0.0105m);

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
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        SeededArticle seeded = await SeedAsync(database, "en", false, null, false);
        StagedReviewHandler handler = new() { UnknownMethodGeneration = true };

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
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        SeededArticle seeded = await SeedAsync(database, "en", false, null, false);
        StagedReviewHandler handler = new() { TeachingFindingCount = 3, OutputLimitTeachingRoot = true };
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
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        SeededArticle seeded = await SeedAsync(database, "en", false, null, false);
        StagedReviewHandler handler = new() { FailTeachingQuoteOnce = true };
        await Assert.ThrowsAsync<HttpRequestException>(() => Workflow(database, handler).ReviewAsync(
            seeded.PersonelId, seeded.CanonicalWorkId, "en", false, default));

        handler.FingerprintValue = new string('e', 64);
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
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        SeededArticle seeded = await SeedAsync(database, "en", false, null, false);
        StagedReviewHandler handler = new();
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
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        SeededArticle seeded = await SeedAsync(database, "en", false, null, false);
        StagedReviewHandler handler = new() { TeachingFindingCount = 1 };
        handler.OnTeachingVerification = () =>
        {
            using IServiceScope mutationScope = fixture.Services.CreateScope();
            AcademicDbContext mutationDatabase = mutationScope.ServiceProvider.GetRequiredService<AcademicDbContext>();
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
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            seeded = await SeedAsync(database, "en", false, null, false);
        }
        await using FakeArticleReviewAnalysisServer analysis = await FakeArticleReviewAnalysisServer.StartAsync();
        using HostProcess host = new(fixture.ConnectionString, analysis.BaseUrl + "/");
        await host.WaitUntilReadyAsync();

        using HttpResponseMessage generated = await host.Client.PostAsJsonAsync(
            "/Services/AcademicPerformance/V1/ReviewCanonicalArticle",
            new { PersonelID = seeded.PersonelId, seeded.CanonicalWorkId, Language = "en" });
        Assert.True(generated.IsSuccessStatusCode,
            await generated.Content.ReadAsStringAsync() + Environment.NewLine + host.Output);
        CanonicalArticleReviewResponse created =
            (await generated.Content.ReadFromJsonAsync<CanonicalArticleReviewResponse>())!;
        using HttpResponseMessage read = await host.Client.PostAsJsonAsync(
            "/Services/AcademicPerformance/V1/GetCanonicalArticleReview",
            new { PersonelID = seeded.PersonelId, seeded.CanonicalWorkId, Language = "en" });
        read.EnsureSuccessStatusCode();
        CanonicalArticleReviewResponse saved =
            (await read.Content.ReadFromJsonAsync<CanonicalArticleReviewResponse>())!;
        using HttpResponseMessage isolated = await host.Client.PostAsJsonAsync(
            "/Services/AcademicPerformance/V1/GetCanonicalArticleReview",
            new { PersonelID = "other-researcher", seeded.CanonicalWorkId, Language = "en" });

        Assert.Equal(created.ReviewRunId, saved.ReviewRunId);
        Assert.Equal(seeded.PersonelId, saved.PersonelId);
        Assert.Equal(HttpStatusCode.NotFound, isolated.StatusCode);
    }

    [Fact]
    public async Task ReviewAsync_NewerBaseDuringModelCallRejectsAndPreservesLastGoodReview()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        SeededArticle seeded = await SeedAsync(database, "en", false, null, false);
        await Workflow(database, ReviewHandler()).ReviewAsync(
            seeded.PersonelId, seeded.CanonicalWorkId, "en", false, default);
        int before = await database.CanonicalArticleReviewRuns.CountAsync(run =>
            run.CanonicalWorkId == seeded.CanonicalWorkId);
        StubHttpHandler racingHandler = ReviewHandler(onRequest: () =>
        {
            using IServiceScope racingScope = fixture.Services.CreateScope();
            AcademicDbContext racingDatabase = racingScope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            AddNewBaseAsync(racingDatabase, seeded, "en").GetAwaiter().GetResult();
        });

        await Assert.ThrowsAsync<ArticleReviewSourceChangedException>(() => Workflow(database, racingHandler)
            .ReviewAsync(seeded.PersonelId, seeded.CanonicalWorkId, "en", true, default));

        database.ChangeTracker.Clear();
        Assert.Equal(before, await database.CanonicalArticleReviewRuns.CountAsync(run =>
            run.CanonicalWorkId == seeded.CanonicalWorkId));
    }

    private static ArticleReviewWorkflow Workflow(
        AcademicDbContext database,
        HttpMessageHandler handler,
        string policy = "article-specialist-review-policy-v3",
        decimal maximumSpendUsd = 1m,
        int maximumCalls = 24)
    {
        HttpClient http = new(handler) { BaseAddress = new Uri("http://127.0.0.1/") };
        return new(database,
            new ArticleReviewServiceClient(http, Options.Create(new AnalysisServiceOptions())),
            new CanonicalWorkSynchronizer(database),
            Options.Create(new ArticleReviewOptions
                { PolicyVersion = policy, MaximumSpendUsd = maximumSpendUsd, MaximumProviderCalls = maximumCalls }));
    }

    private static StubHttpHandler ReviewHandler(bool caseDistinct = false, Action? onRequest = null) => new(request =>
    {
        onRequest?.Invoke();
        const string fingerprint = "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";
        const string pricing = "gemini-3.8-flash-standard-through-2026-12-31";
        string path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("/configuration", StringComparison.Ordinal))
            return StubHttpHandler.Json(JsonSerializer.Serialize(new ArticleReviewRuntimeConfiguration(
                fingerprint, "Gemini", "gemini-3.8-flash", "gemini-3.8-flash",
                "article-specialist-review-v1", "article-specialist-review-verification-v1",
                8192, 8192, "high", "high", pricing), JsonOptions));
        if (path.EndsWith("/quote", StringComparison.Ordinal))
        {
            ArticleReviewStageQuoteRequest quoted = request.Content!.ReadFromJsonAsync<ArticleReviewStageQuoteRequest>()
                .GetAwaiter().GetResult()!;
            return StubHttpHandler.Json(JsonSerializer.Serialize(new ArticleReviewStageQuote(
                fingerprint, new string(quoted.Role[0], 64), 0.01m), JsonOptions));
        }
        ArticleReviewStageDispatchRequest input = request.Content!
            .ReadFromJsonAsync<ArticleReviewStageDispatchRequest>().GetAwaiter().GetResult()!;
        ArticleReviewProviderAttempt attempt = new(input.AttemptId, "Success", "gemini-3.8-flash", 0.001m, pricing);
        if (path.EndsWith("/verify", StringComparison.Ordinal))
            return StubHttpHandler.Json(JsonSerializer.Serialize(new ArticleReviewVerificationStageResult(
                input.Findings!.Select(value => new ArticleReviewVerificationVerdict(
                    value.FindingId, "supported", "Direct support.")).ToList(),
                "gemini-3.8-flash", "article-specialist-review-verification-v1", attempt), JsonOptions));
        ArticleSourceSpan span = input.Source.SourceSpans!.First();
        List<ArticleReviewCandidateFinding> methodFindings =
        [
            new("F1", "method", "source_observation", "The source reports its method.", null, [span.SourceId])
        ];
        if (caseDistinct)
            methodFindings.Add(new("f1", "method", "review_question", "The source reports its method.",
                "Could a reviewer examine the reported method?", [span.SourceId]));
        IReadOnlyList<ArticleReviewCandidateFinding> findings = input.Role == "method" ? methodFindings : [];
        return StubHttpHandler.Json(JsonSerializer.Serialize(new ArticleReviewGenerationStageResult(
            input.Role, findings, "gemini-3.8-flash", "article-specialist-review-v1", attempt), JsonOptions));
    });

    private static async Task<SeededArticle> SeedAsync(
        AcademicDbContext database,
        string language,
        bool isPartial,
        string? scopeReason,
        bool baseRunMarkedPartial,
        string sourceKind = "pdf")
    {
        string personelId = "review-" + Guid.NewGuid().ToString("N");
        DateTime now = DateTime.UtcNow;
        Researcher researcher = new() { PersonelId = personelId, FirstName = "Review" };
        CanonicalWork canonical = new()
        {
            NormalizedDoi = "10.9100/" + Guid.NewGuid().ToString("N"), CreatedAt = now, UpdatedAt = now
        };
        database.AddRange(researcher, canonical);
        await database.SaveChangesAsync();
        database.CanonicalResearcherWorks.Add(new()
        {
            CanonicalWorkId = canonical.Id, PersonelId = personelId, LastObservedAt = now
        });
        int? pageNumber = sourceKind == "pdf" ? 1 : null;
        IReadOnlyList<ArticlePage> pages = [new(pageNumber, sourceKind == "abstract"
            ? "The abstract reports a sample of 40 participants."
            : "The full text reports a sample of 40 participants and its study method.")];
        IReadOnlyList<ArticleSourceSpan> spans = ArticleSourceCatalog.Create(pages);
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(pages, JsonOptions)))).ToLowerInvariant();
        ArticleSourceSnapshot source = new()
        {
            CanonicalWorkId = canonical.Id, ExtractedTextHash = hash, SourceKind = sourceKind,
            ExtractionVersion = sourceKind + "-v1", CreatedAt = DateTimeOffset.UtcNow,
            Pages = pages.Select((page, index) => new ArticleSourcePageSnapshot
                { Ordinal = index, PageNumber = page.PageNumber, Text = page.Text }).ToList(),
            Spans = spans.Select((span, index) => new ArticleSourceSpanSnapshot
            {
                Ordinal = index, SourceId = span.SourceId, PageNumber = span.PageNumber,
                StartOffset = span.StartOffset, EndOffset = span.EndOffset, Text = span.Text
            }).ToList()
        };
        SummarizeArticleRequest sourceRequest = new(language, sourceKind, hash, sourceKind + "-v1",
            pages, 1, isPartial, scopeReason) { SourceSpans = spans };
        SavedArticleSummary saved = new()
        {
            OriginalAcademicWorkId = 1, PersonelId = personelId, SavedAt = DateTimeOffset.UtcNow,
            SourceHash = hash, SourceKind = sourceKind, ExtractionVersion = sourceKind + "-v1",
            SnapshotJson = JsonSerializer.Serialize(sourceRequest, JsonOptions), ReportJson = "{}"
        };
        database.AddRange(source, saved);
        await database.SaveChangesAsync();
        CanonicalArticleAnalysisRun run = BaseRun(canonical.Id, source.Id, saved.Id, language, baseRunMarkedPartial);
        database.CanonicalArticleAnalysisRuns.Add(run);
        await database.SaveChangesAsync();
        return new(personelId, canonical.Id, run.Id, source.Id);
    }

    private static async Task AddNewBaseAsync(AcademicDbContext database, SeededArticle seeded, string language)
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
            seeded.CanonicalWorkId, seeded.SourceSnapshotId, saved.Id, language, false));
        await database.SaveChangesAsync();
    }

    private static CanonicalArticleAnalysisRun BaseRun(
        int canonicalWorkId, long sourceId, long summaryId, string language, bool partial) => new()
    {
        CanonicalWorkId = canonicalWorkId,
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

    private sealed class StagedReviewHandler : HttpMessageHandler
    {
        private const string Pricing = "gemini-3.8-flash-standard-through-2026-12-31";
        private bool _failedTeachingQuote;
        private bool _returnedOutputLimit;
        private bool _returnedGenerationOutputLimit;

        public bool FailTeachingQuoteOnce { get; init; }
        public bool OutputLimitTeachingRoot { get; init; }
        public bool OutputLimitMethodGeneration { get; init; }
        public bool OutputLimitMethodRecovery { get; init; }
        public string? GenerationOutputLimitAttributionFault { get; init; }
        public bool UnknownMethodGeneration { get; set; }
        public string FingerprintValue { get; set; } =
            "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";
        public Action? OnTeachingVerification { get; set; }
        public int TeachingFindingCount { get; init; }
        public List<string> GenerationRoles { get; } = [];
        public List<string> GenerationThinkingLevels { get; } = [];
        public List<int> VerificationBatchSizes { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/configuration", StringComparison.Ordinal))
                return Json(new ArticleReviewRuntimeConfiguration(FingerprintValue, "Gemini", "gemini-3.8-flash",
                    "gemini-3.8-flash", "article-specialist-review-v1",
                    "article-specialist-review-verification-v1", 8192, 8192, "high", "high", Pricing)
                {
                    GenerationRecoveryPolicyVersion = ArticleReviewGenerationRecovery.PolicyVersion,
                    GenerationRecoveryThinkingLevel = ArticleReviewGenerationRecovery.RecoveryThinkingLevel
                });
            if (path.EndsWith("/quote", StringComparison.Ordinal))
            {
                ArticleReviewStageQuoteRequest value = Read<ArticleReviewStageQuoteRequest>(request);
                if (FailTeachingQuoteOnce && value.Role == "teaching" && !_failedTeachingQuote)
                {
                    _failedTeachingQuote = true;
                    throw new HttpRequestException("Synthetic pre-dispatch failure.");
                }
                return Json(new ArticleReviewStageQuote(FingerprintValue, new string(value.Role[0], 64), 0.01m));
            }

            ArticleReviewStageDispatchRequest dispatch = Read<ArticleReviewStageDispatchRequest>(request);
            ArticleReviewProviderAttempt attempt = new(dispatch.AttemptId, "Success", "gemini-3.8-flash",
                0.001m, Pricing);
            if (path.EndsWith("/generate", StringComparison.Ordinal))
            {
                GenerationRoles.Add(dispatch.Role);
                string thinkingLevel = dispatch.GenerationThinkingLevel ??
                    ArticleReviewGenerationRecovery.InitialThinkingLevel;
                GenerationThinkingLevels.Add(thinkingLevel);
                if (UnknownMethodGeneration && dispatch.Role == "method")
                    return Error(HttpStatusCode.BadGateway, new AnalysisErrorResponse(
                        "Synthetic unknown dispatch.", "provider_failure"));
                bool initialLimit = OutputLimitMethodGeneration && dispatch.Role == "method" &&
                    thinkingLevel == ArticleReviewGenerationRecovery.InitialThinkingLevel &&
                    !_returnedGenerationOutputLimit;
                bool recoveryLimit = OutputLimitMethodRecovery && dispatch.Role == "method" &&
                    thinkingLevel == ArticleReviewGenerationRecovery.RecoveryThinkingLevel;
                if (initialLimit || recoveryLimit)
                {
                    _returnedGenerationOutputLimit = true;
                    ArticleReviewProviderAttempt limited = attempt with
                    {
                        Outcome = "OutputLimit",
                        ReturnedModel = GenerationOutputLimitAttributionFault == "model"
                            ? "gemini-3.8-flash-001" : attempt.ReturnedModel,
                        PricingVersion = GenerationOutputLimitAttributionFault == "pricing"
                            ? null : attempt.PricingVersion,
                        EstimatedCostUsd = GenerationOutputLimitAttributionFault == "cost"
                            ? null : attempt.EstimatedCostUsd
                    };
                    return Error(HttpStatusCode.BadGateway, new AnalysisErrorResponse(
                        "Synthetic generation output limit.", "output_limit")
                    {
                        Failure = new("output_limit", "generation", dispatch.Role),
                        ProviderAttempt = limited
                    });
                }
                int findingCount = dispatch.Role == "method" ? 1 :
                    dispatch.Role == "teaching" ? TeachingFindingCount : 0;
                ArticleSourceSpan span = dispatch.Source.SourceSpans!.First();
                List<ArticleReviewCandidateFinding> findings = Enumerable.Range(1, findingCount).Select(index =>
                    new ArticleReviewCandidateFinding($"F{index}", dispatch.Role,
                        dispatch.Role == "teaching" ? "teaching_adaptation" : "source_observation",
                        "The source directly supports this finding.",
                        dispatch.Role == "teaching" ? "Use this as a labelled teaching adaptation." : null,
                        [span.SourceId])).ToList();
                return Json(new ArticleReviewGenerationStageResult(dispatch.Role, findings,
                    "gemini-3.8-flash", "article-specialist-review-v1", attempt));
            }

            VerificationBatchSizes.Add(dispatch.Findings!.Count);
            if (dispatch.Role == "teaching") OnTeachingVerification?.Invoke();
            if (OutputLimitTeachingRoot && dispatch.Role == "teaching" && dispatch.Findings.Count == 3 &&
                !_returnedOutputLimit)
            {
                _returnedOutputLimit = true;
                ArticleReviewProviderAttempt limited = attempt with { Outcome = "OutputLimit" };
                return Error(HttpStatusCode.BadGateway, new AnalysisErrorResponse(
                    "Synthetic output limit.", "output_limit")
                {
                    Failure = new("output_limit", "verification", dispatch.Role),
                    ProviderAttempt = limited
                });
            }
            return Json(new ArticleReviewVerificationStageResult(dispatch.Findings.Select(value =>
                    new ArticleReviewVerificationVerdict(value.FindingId, "supported", "Direct support."))
                .ToList(), "gemini-3.8-flash", "article-specialist-review-verification-v1", attempt));
        }

        private static T Read<T>(HttpRequestMessage request) =>
            request.Content!.ReadFromJsonAsync<T>().GetAwaiter().GetResult()!;

        private static Task<HttpResponseMessage> Json<T>(T value) => Task.FromResult(
            StubHttpHandler.Json(JsonSerializer.Serialize(value, JsonOptions)));

        private static Task<HttpResponseMessage> Error(HttpStatusCode status, AnalysisErrorResponse value) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = JsonContent.Create(value)
            });
    }

    private sealed record SeededArticle(string PersonelId, int CanonicalWorkId, long BaseRunId, long SourceSnapshotId);
}
