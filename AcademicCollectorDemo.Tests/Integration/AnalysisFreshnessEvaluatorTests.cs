using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using AcademicCollectorDemo.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class AnalysisFreshnessEvaluatorTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task EvaluateAsync_SavedIdentitiesDistinguishCurrentStaleAndUnknown()
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        const string currentPolicy = "freshness-policy-v2";
        string personelId = "freshness-" + Guid.NewGuid().ToString("N");
        database.Researchers.Add(new Researcher { PersonelId = personelId });
        await database.SaveChangesAsync();

        CanonicalArticleAnalysisRun current = await AddRunAsync(database, personelId, currentPolicy);
        AddJob(database, current, "input-a", "input-a", currentPolicy, currentPolicy);
        CanonicalArticleAnalysisRun sourceChanged = await AddRunAsync(database, personelId, currentPolicy);
        AddJob(database, sourceChanged, "input-a", "input-b", currentPolicy, currentPolicy);
        CanonicalArticleAnalysisRun policyChanged = await AddRunAsync(database, personelId, "freshness-policy-v1");
        CanonicalArticleAnalysisRun legacy = await AddRunAsync(database, personelId, null);
        CanonicalArticleAnalysisRun replaced = await AddRunAsync(database, personelId, currentPolicy);
        _ = await AddRunAsync(database, personelId, currentPolicy, replaced.CanonicalWorkId);
        await database.SaveChangesAsync();

        IReadOnlyDictionary<long, AnalysisFreshnessResult> result =
            await AnalysisFreshnessEvaluator.EvaluateAsync(database,
                [current.Id, sourceChanged.Id, policyChanged.Id, legacy.Id, replaced.Id],
                currentPolicy, default);

        Assert.Equal(AnalysisFreshnessStatus.Current, result[current.Id].Status);
        Assert.Equal(AnalysisFreshnessStatus.Stale, result[sourceChanged.Id].Status);
        Assert.Contains("SourceInputChanged", result[sourceChanged.Id].Reasons);
        Assert.Equal(AnalysisFreshnessStatus.Stale, result[policyChanged.Id].Status);
        Assert.Contains("CurrentPolicyChanged", result[policyChanged.Id].Reasons);
        Assert.Equal(AnalysisFreshnessStatus.Unknown, result[legacy.Id].Status);
        Assert.Contains("AutomationJobUnavailable", result[legacy.Id].Reasons);
        Assert.Equal(AnalysisFreshnessStatus.Stale, result[replaced.Id].Status);
        Assert.Contains("NewerAnalysisAvailable", result[replaced.Id].Reasons);
        Assert.Equal(64, AnalysisFreshnessEvaluator.CreateFreshnessHash(result.Values, currentPolicy).Length);
    }

    private static async Task<CanonicalArticleAnalysisRun> AddRunAsync(AcademicDbContext database,
        string personelId, string? policyVersion, int? existingWorkId = null)
    {
        CanonicalWork? work = existingWorkId.HasValue
            ? await database.CanonicalWorks.FindAsync(existingWorkId.Value)
            : null;
        work ??= new CanonicalWork
        {
            NormalizedDoi = "10.9898/" + Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        ArticleSourceSnapshot source = new()
        {
            CanonicalWork = work,
            ExtractedTextHash = Guid.NewGuid().ToString("N").PadRight(64, 'a'),
            SourceKind = "Pdf",
            ExtractionVersion = "freshness-test-v1",
            CreatedAt = DateTimeOffset.UtcNow
        };
        SavedArticleSummary summary = new()
        {
            PersonelId = personelId,
            OriginalAcademicWorkId = Random.Shared.Next(1, int.MaxValue),
            SavedAt = DateTimeOffset.UtcNow,
            SourceHash = Guid.NewGuid().ToString("N").PadRight(64, 'b'),
            SourceKind = "Pdf",
            ExtractionVersion = "freshness-test-v1",
            SnapshotJson = "{}",
            ReportJson = "{}"
        };
        CanonicalArticleAnalysisRun run = new()
        {
            CanonicalWork = work,
            ArticleSourceSnapshot = source,
            SavedArticleSummary = summary,
            AnalyzedAt = DateTimeOffset.UtcNow,
            SourceAcquiredAt = DateTimeOffset.UtcNow,
            SourceOrigin = "Test",
            Language = "tr",
            PolicyVersion = policyVersion,
            Model = "synthetic",
            PromptVersion = "freshness-test-v1",
            ExtractionMethod = "test",
            ProcessedChunks = 1,
            TotalChunks = 1,
            ProcessedPages = 1,
            TextBearingPages = 1,
            TotalPages = 1,
            OmissionReasonsJson = "[]",
            VerificationStatus = "automatically_checked",
            VerificationModel = "synthetic",
            VerificationPromptVersion = "freshness-test-v1"
        };
        database.Add(run);
        await database.SaveChangesAsync();
        return run;
    }

    private static void AddJob(AcademicDbContext database, CanonicalArticleAnalysisRun run,
        string processedInput, string desiredInput, string processedPolicy, string desiredPolicy)
    {
        database.ArticleSummaryAutomationJobs.Add(new()
        {
            CanonicalWorkId = run.CanonicalWorkId,
            Language = run.Language,
            Status = ArticleSummaryAutomationJobStatus.Succeeded,
            DesiredInputHash = desiredInput,
            DesiredPolicyVersion = desiredPolicy,
            ProcessedInputHash = processedInput,
            ProcessedPolicyVersion = processedPolicy,
            LastSuccessfulAnalysisRunId = run.Id,
            NextAttemptAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
    }
}
