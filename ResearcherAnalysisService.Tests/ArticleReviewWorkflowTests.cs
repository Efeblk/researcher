using System.Text.Json;
using AcademicCollector.Analysis.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Products.ArticleReviews;
using ResearcherAnalysisService.Products.ArticleSummaries;
using ResearcherAnalysisService.Products.Data;
using ResearcherAnalysisService.Tests.Infrastructure;

namespace ResearcherAnalysisService.Tests;

[Collection("Analysis Product SQL Server")]
public sealed class ArticleReviewWorkflowTests(AnalysisProductSqlServerFixture fixture)
{
    [Fact]
    public async Task GetLatestAsync_RequiresAssociationAndMarksNewBaseOrPolicyStale()
    {
        string personelId = "review-read-" + Guid.NewGuid().ToString("N");
        SyntheticCanonicalSource source = await fixture.SeedCanonicalSourceAsync(personelId);
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        string policy = scope.ServiceProvider.GetRequiredService<IOptions<ArticleReviewOptions>>()
            .Value.PolicyVersion.Trim();
        (CanonicalArticleAnalysisRun baseRun, ArticleSourceSnapshot snapshot) =
            await AddBaseRunAsync(database, source, "en");
        ArticleReviewReport report = EmptyReport(policy);
        CanonicalArticleReviewRun review = new()
        {
            CanonicalWorkId = source.CanonicalWorkId,
            BaseAnalysisRunId = baseRun.Id,
            ArticleSourceSnapshotId = snapshot.Id,
            ReviewedAt = DateTimeOffset.UtcNow,
            Language = "en",
            PolicyVersion = policy,
            SettingsFingerprint = new string('f', 64),
            Model = report.Model,
            PromptVersion = report.PromptVersion,
            Outcome = report.Outcome,
            VerificationStatus = report.Verification.Status,
            VerificationModel = report.Verification.Model,
            VerificationPromptVersion = report.Verification.PromptVersion,
            UsesSameModelFamily = true,
            ProcessedPages = 1,
            TextBearingPages = 1,
            TotalPages = 1,
            ProcessedRoles = 4,
            TotalRoles = 4,
            OmissionReasonsJson = "[]",
            ReportJson = JsonSerializer.Serialize(report,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };
        database.Add(review);
        await database.SaveChangesAsync();
        ArticleReviewWorkflow workflow = new(database,
            new ArticleReviewServiceClient(null!, null!),
            scope.ServiceProvider.GetRequiredService<AnalysisSourceLock>(),
            scope.ServiceProvider.GetRequiredService<IOptions<ArticleReviewOptions>>());

        var current = await workflow.GetLatestAsync(
            personelId, source.CanonicalWorkId, "en", default);
        Assert.NotNull(current);
        Assert.False(current.IsStale);
        Assert.Null(await workflow.GetLatestAsync(
            "other-owner", source.CanonicalWorkId, "en", default));

        await AddBaseRunAsync(database, source, "en");
        database.ChangeTracker.Clear();
        var newerBase = await workflow.GetLatestAsync(
            personelId, source.CanonicalWorkId, "en", default);
        Assert.True(newerBase!.IsStale);
        Assert.Contains(newerBase.StaleReasons, reason => reason.Contains("newer", StringComparison.OrdinalIgnoreCase));

        review = await database.CanonicalArticleReviewRuns.SingleAsync(value => value.Id == review.Id);
        review.PolicyVersion = "old-policy";
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        var changedPolicy = await workflow.GetLatestAsync(
            personelId, source.CanonicalWorkId, "en", default);
        Assert.Contains(changedPolicy!.StaleReasons,
            reason => reason.Contains("policy", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<(CanonicalArticleAnalysisRun Run, ArticleSourceSnapshot Snapshot)>
        AddBaseRunAsync(AnalysisDbContext database, SyntheticCanonicalSource source, string language)
    {
        ArticleSourceSnapshot snapshot = new()
        {
            CanonicalWorkId = source.CanonicalWorkId,
            ExtractedTextHash = Guid.NewGuid().ToString("N").PadRight(64, 'a'),
            SourceKind = "Pdf",
            ExtractionVersion = "test-v1",
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
                SourceHash = new string('b', 64),
                SourceKind = "Pdf",
                ExtractionVersion = "test-v1",
                SnapshotJson = "{}",
                ReportJson = "{}"
            },
            AnalyzedAt = DateTimeOffset.UtcNow,
            SourceAcquiredAt = DateTimeOffset.UtcNow,
            SourceOrigin = "Synthetic",
            Language = language,
            PolicyVersion = "summary-policy",
            Model = "synthetic",
            PromptVersion = "test-v1",
            ExtractionMethod = "pdf_text",
            ProcessedChunks = 1,
            TotalChunks = 1,
            ProcessedPages = 1,
            TextBearingPages = 1,
            TotalPages = 1,
            OmissionReasonsJson = "[]",
            VerificationStatus = "automatically_checked",
            VerificationModel = "synthetic",
            VerificationPromptVersion = "test-v1"
        };
        database.Add(run);
        await database.SaveChangesAsync();
        return (run, snapshot);
    }

    private static ArticleReviewReport EmptyReport(string policy) => new(
        "en", "Pdf", new string('b', 64), "test-v1", policy,
        "no_supported_findings",
        new(1, 1, 1, false, null),
        new(4, 4, 0, 0, 0, 0, 0, 0, []),
        [new("method", "no_supported_findings", []),
         new("quantitative", "no_supported_findings", []),
         new("claim_evidence", "no_supported_findings", []),
         new("teaching", "no_supported_findings", [])],
        "synthetic", "test-v1",
        new("no_supported_findings", "synthetic", "test-v1", true, null));
}
