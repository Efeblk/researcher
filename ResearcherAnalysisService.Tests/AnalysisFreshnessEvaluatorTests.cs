using ResearcherAnalysisService.Products.ArticleSummaries;
using ResearcherAnalysisService.Products.Data;
using ResearcherAnalysisService.SourceData.Researchers;
using ResearcherAnalysisService.SourceData.Works;
using ResearcherAnalysisService.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ResearcherAnalysisService.Tests;

[Collection("Analysis Product SQL Server")]
public sealed class AnalysisFreshnessEvaluatorTests(AnalysisProductSqlServerFixture fixture)
{
    [Fact]
    public async Task EvaluateAsync_SavedIdentitiesDistinguishCurrentStaleAndUnknown()
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        const string currentPolicy = "freshness-policy-v2";
        string personelId = "freshness-" + Guid.NewGuid().ToString("N");
        database.Researchers.Add(new Researcher { PersonelId = personelId });
        await database.SaveChangesWithSourceSeedAsync(fixture.ConnectionString);

        CanonicalArticleAnalysisRun current = await AddRunAsync(database, personelId, currentPolicy);
        AddJob(database, current, "input-a", "input-a", currentPolicy, currentPolicy);
        CanonicalArticleAnalysisRun sourceChanged = await AddRunAsync(database, personelId, currentPolicy);
        AddJob(database, sourceChanged, "input-a", "input-b", currentPolicy, currentPolicy);
        CanonicalArticleAnalysisRun policyChanged = await AddRunAsync(database, personelId, "freshness-policy-v1");
        CanonicalArticleAnalysisRun replaced = await AddRunAsync(database, personelId, currentPolicy);
        _ = await AddRunAsync(database, personelId, currentPolicy, replaced.CanonicalWorkId);
        await database.SaveChangesWithSourceSeedAsync(fixture.ConnectionString);

        IReadOnlyDictionary<long, AnalysisFreshnessResult> result =
            await AnalysisFreshnessEvaluator.EvaluateAsync(database,
                [current.Id, sourceChanged.Id, policyChanged.Id, replaced.Id],
                currentPolicy, default);

        Assert.Equal(AnalysisFreshnessStatus.Current, result[current.Id].Status);
        Assert.Equal(AnalysisFreshnessStatus.Stale, result[sourceChanged.Id].Status);
        Assert.Contains("SourceInputChanged", result[sourceChanged.Id].Reasons);
        Assert.Equal(AnalysisFreshnessStatus.Stale, result[policyChanged.Id].Status);
        Assert.Contains("CurrentPolicyChanged", result[policyChanged.Id].Reasons);
        Assert.Equal(AnalysisFreshnessStatus.Stale, result[replaced.Id].Status);
        Assert.Contains("NewerAnalysisAvailable", result[replaced.Id].Reasons);
        Assert.Equal(64, AnalysisFreshnessEvaluator.CreateFreshnessHash(result.Values, currentPolicy).Length);
    }

    private async Task<CanonicalArticleAnalysisRun> AddRunAsync(AnalysisDbContext database,
        string personelId, string policyVersion, int? existingWorkId = null)
    {
        int canonicalWorkId;
        if (existingWorkId.HasValue)
        {
            canonicalWorkId = existingWorkId.Value;
        }
        else
        {
            string doi = "10.9898/" + Guid.NewGuid().ToString("N");
            CanonicalWork work = new()
            {
                NormalizedDoi = doi,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            AcademicWork academicWork = new()
            {
                PersonelId = personelId,
                Provider = AcademicWorkProvider.OpenAlex,
                ProviderWorkId = "https://openalex.org/W" + Guid.NewGuid().ToString("N"),
                Title = "Synthetic freshness article",
                Doi = doi,
                SyncedAt = DateTime.UtcNow,
                CanonicalObservation = new CanonicalWorkObservation
                {
                    CanonicalWork = work,
                    PersonelId = personelId,
                    Provider = AcademicWorkProvider.OpenAlex,
                    DoiObserved = doi,
                    ObservedAt = DateTime.UtcNow
                }
            };
            await using DbContext sourceSeed = fixture.CreateSeedContext();
            sourceSeed.Add(academicWork);
            await sourceSeed.SaveChangesAsync();
            canonicalWorkId = work.Id;
        }
        string sourceIdentity = (await CanonicalSourceIdentity.LoadAsync(
            database, canonicalWorkId, default))!;
        ArticleSourceSnapshot source = new()
        {
            CanonicalWorkId = canonicalWorkId,
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
            CanonicalWorkId = canonicalWorkId,
            ArticleSourceSnapshot = source,
            SavedArticleSummary = summary,
            AnalyzedAt = DateTimeOffset.UtcNow,
            SourceAcquiredAt = DateTimeOffset.UtcNow,
            SourceOrigin = "Test",
            Language = "tr",
            PolicyVersion = policyVersion,
            SourceIdentityHash = sourceIdentity,
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
        await database.SaveChangesWithSourceSeedAsync(fixture.ConnectionString);
        return run;
    }

    private static void AddJob(AnalysisDbContext database, CanonicalArticleAnalysisRun run,
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
