using System.Text.Json;
using AcademicCollector.Analysis.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Evaluations;
using AcademicCollectorDemo.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ApiContracts = AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class ArticleEvaluationReadServiceTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task GetAsync_MixedProfileOutcomes_RetainsFailuresInProfileAndPooledDenominators()
    {
        Guid completedRunId = Guid.NewGuid();
        Guid pendingRunId = Guid.NewGuid();
        using IServiceScope scope = fixture.Services.CreateScope();
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        try
        {
            ArticleEvaluationRun completed = Run(completedRunId, ArticleEvaluationStatus.CompletedWithFailures,
                [Profile(ProfileA), Profile(ProfileB)]);
            ArticleEvaluationCase completedCase = Case(completed);
            Complete(Item(completedCase, ProfileA), ProfileA);
            ArticleEvaluationWorkItem failed = Item(completedCase, ProfileB);
            failed.Status = ArticleEvaluationStatus.Failed;
            failed.OutcomeCode = "ExecutionFailed";
            completed.TotalWorkItems = 2;
            completed.CompletedWorkItems = 1;
            completed.FailedWorkItems = 1;

            ArticleEvaluationRun pending = Run(pendingRunId, ArticleEvaluationStatus.Pending,
                [Profile(ProfileA)]);
            Item(Case(pending), ProfileA);
            pending.TotalWorkItems = 1;
            database.AddRange(completed, pending);
            await database.SaveChangesAsync();

            ArticleEvaluationReadService reader = new(database);
            ApiContracts.ArticleEvaluationResponse completedResponse = (await reader.GetAsync(
                new ApiContracts.GetArticleEvaluationRequest { RunId = completedRunId }, default))!;
            ApiContracts.ArticleEvaluationResponse pendingResponse = (await reader.GetAsync(
                new ApiContracts.GetArticleEvaluationRequest { RunId = pendingRunId }, default))!;

            Assert.Equal(6, completedResponse.Aggregate.ScheduledClaims);
            Assert.Equal(3, completedResponse.Aggregate.FailedClaims);
            Assert.Equal(3, completedResponse.Aggregate.ScoredClaims);
            Assert.Equal(3, completedResponse.Aggregate.CorrectClaims);
            Assert.Equal(0.5, completedResponse.Aggregate.ControlledSourceReadingAccuracy);
            Assert.Contains(completedResponse.Aggregate.ConfusionMatrix.Cast<ConfusionCell>(),
                value => value is { Expected: "supported", Actual: "failed", Count: 1 });
            Assert.Equal(1d, Assert.Single(completedResponse.ProfileAggregates,
                value => value.ProfileId == ProfileA).Aggregate.ControlledSourceReadingAccuracy);
            ApiContracts.ArticleEvaluationAggregateDto failedProfile = Assert.Single(
                completedResponse.ProfileAggregates, value => value.ProfileId == ProfileB).Aggregate;
            Assert.Equal(3, failedProfile.FailedClaims);
            Assert.Equal(0d, failedProfile.ControlledSourceReadingAccuracy);

            Assert.Equal(3, pendingResponse.Aggregate.PendingClaims);
            Assert.Null(pendingResponse.Aggregate.ControlledSourceReadingAccuracy);
            Assert.Null(Assert.Single(pendingResponse.ProfileAggregates).Aggregate.ControlledSourceReadingAccuracy);
        }
        finally
        {
            await CleanupAsync(database, completedRunId, pendingRunId);
        }
    }

    [Fact]
    public async Task GetAsync_PartialCostAttempt_DoesNotReportKnownCost()
    {
        Guid runId = Guid.NewGuid();
        using IServiceScope scope = fixture.Services.CreateScope();
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        try
        {
            ArticleEvaluationRun run = Run(runId, ArticleEvaluationStatus.Completed, [Profile(ProfileA)]);
            ArticleEvaluationWorkItem item = Item(Case(run), ProfileA);
            Complete(item, ProfileA);
            item.AttemptCount = 1;
            item.Attempts.Add(Attempt(0.01m, "Partial", null));
            run.TotalWorkItems = 1;
            run.CompletedWorkItems = 1;
            database.Add(run);
            await database.SaveChangesAsync();

            ApiContracts.ArticleEvaluationResponse response = (await new ArticleEvaluationReadService(database)
                .GetAsync(new ApiContracts.GetArticleEvaluationRequest { RunId = runId }, default))!;

            Assert.Equal(0.01m, response.Aggregate.EstimatedCostUsd);
            Assert.Equal("Partial", response.Aggregate.CostStatus);
            ApiContracts.ArticleEvaluationAggregateDto profile = Assert.Single(response.ProfileAggregates).Aggregate;
            Assert.Equal(0.01m, profile.EstimatedCostUsd);
            Assert.Equal("Partial", profile.CostStatus);
        }
        finally
        {
            await CleanupAsync(database, runId);
        }
    }

    [Fact]
    public async Task GetAsync_MissingAttemptTelemetry_DoesNotReportCompleteProfileTokenTotals()
    {
        Guid runId = Guid.NewGuid();
        using IServiceScope scope = fixture.Services.CreateScope();
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        try
        {
            ArticleEvaluationRun run = Run(runId, ArticleEvaluationStatus.Completed, [Profile(ProfileA)]);
            ArticleEvaluationCase evaluationCase = Case(run);
            ArticleEvaluationWorkItem known = Item(evaluationCase, ProfileA);
            Complete(known, ProfileA);
            known.AttemptCount = 1;
            known.Attempts.Add(Attempt(null, "Unknown", Telemetry(10, 5)));
            ArticleEvaluationWorkItem missing = Item(evaluationCase, ProfileA);
            Complete(missing, ProfileA);
            missing.AttemptCount = 1;
            missing.Attempts.Add(Attempt(null, "Unknown", null));
            run.TotalWorkItems = 2;
            run.CompletedWorkItems = 2;
            database.Add(run);
            await database.SaveChangesAsync();

            ApiContracts.ArticleEvaluationProfileAggregateDto profile = Assert.Single(
                (await new ArticleEvaluationReadService(database).GetAsync(
                    new ApiContracts.GetArticleEvaluationRequest { RunId = runId }, default))!.ProfileAggregates);

            Assert.NotEqual("Known", profile.TokenStatus);
            Assert.Null(profile.InputTokens);
            Assert.Null(profile.OutputTokens);
        }
        finally
        {
            await CleanupAsync(database, runId);
        }
    }

    private static ArticleEvaluationRun Run(Guid runId, string status,
        IReadOnlyList<ArticleEvaluationProfile> profiles) => new()
    {
        RunId = runId,
        Status = status,
        DatasetVersion = "test-dataset-v1",
        EvaluatorVersion = "test-evaluator-v1",
        PolicyVersion = "test-policy-v1",
        ProfilesJson = JsonSerializer.Serialize(profiles, JsonOptions),
        TotalCases = 1,
        WorstCaseModelCalls = profiles.Count,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        CompletedAt = status == ArticleEvaluationStatus.Pending ? null : DateTimeOffset.UtcNow
    };

    private static ArticleEvaluationCase Case(ArticleEvaluationRun run)
    {
        ArticleEvaluationCase value = new()
        {
            Ordinal = 0,
            CaseId = "case-" + Guid.NewGuid().ToString("N"),
            Kind = ArticleEvaluationTaskKinds.Calibration,
            Language = "en",
            SourceHash = new string('s', 64),
            SourceSnapshotJson = "{}",
            ReferenceJson = "{}",
            ExpectedVerdictsJson = JsonSerializer.Serialize(References, JsonOptions),
            RequestPayloadJson = "{}"
        };
        run.Cases.Add(value);
        return value;
    }

    private static ArticleEvaluationWorkItem Item(ArticleEvaluationCase evaluationCase, string profileId)
    {
        ArticleEvaluationWorkItem value = new()
        {
            Ordinal = evaluationCase.WorkItems.Count,
            Phase = ArticleEvaluationTaskKinds.Calibration,
            ProfileId = profileId,
            ProfileFingerprint = Fingerprint(profileId),
            ProfileSnapshotJson = JsonSerializer.Serialize(Profile(profileId), JsonOptions)
        };
        evaluationCase.WorkItems.Add(value);
        return value;
    }

    private static void Complete(ArticleEvaluationWorkItem item, string profileId)
    {
        item.Status = ArticleEvaluationStatus.Completed;
        item.CompletedAt = DateTimeOffset.UtcNow;
        item.Result = new ArticleEvaluationResult
        {
            ActualModelIdentity = "actual-" + profileId,
            MetricsJson = "{}",
            ResultJson = JsonSerializer.Serialize(Result(profileId), JsonOptions),
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static ArticleEvaluationAttempt Attempt(decimal? cost, string costStatus, string? telemetry) => new()
    {
        AttemptNumber = 1,
        ExecutionToken = Guid.NewGuid(),
        Status = ArticleEvaluationStatus.Completed,
        RequestJson = "{}",
        RequestHash = new string('r', 64),
        TelemetryJson = telemetry,
        EstimatedCostUsd = cost,
        CostStatus = costStatus,
        StartedAt = DateTimeOffset.UtcNow,
        CompletedAt = DateTimeOffset.UtcNow
    };

    private static string Telemetry(int inputTokens, int outputTokens)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ArticleEvaluationAttemptTelemetry attempt = new(1, "calibration_verify", null, "fake", "model",
            "actual-model", "completed", now, now.AddMilliseconds(100), 100, inputTokens, outputTokens,
            null, null, null, null, null, null);
        return JsonSerializer.Serialize(new ArticleEvaluationTelemetry(100, 1, [attempt]), JsonOptions);
    }

    private static AcademicCollector.Analysis.Contracts.ArticleEvaluationResponse Result(string profileId) => new(
        profileId, ArticleEvaluationTaskKinds.Calibration, "fake", "model", ["actual-" + profileId],
        Fingerprint(profileId), "settings-v1", "source", ArticleEvaluationOutcomes.Completed, null,
        new ArticleEvaluationTelemetry(0, 0, []))
    {
        Verdicts =
        [
            new("c1", "supported", "test"),
            new("c2", "unsupported", "test"),
            new("c3", "uncertain", "test")
        ]
    };

    private static ArticleEvaluationProfile Profile(string profileId) => new(profileId, profileId, "fake", "model",
        "revision", Fingerprint(profileId), "settings-v1", [ArticleEvaluationTaskKinds.Calibration],
        "configured", null, true);

    private static string Fingerprint(string profileId) => new(profileId == ProfileA ? 'a' : 'b', 64);

    private static async Task CleanupAsync(AcademicDbContext database, params Guid[] runIds)
    {
        database.ChangeTracker.Clear();
        await database.ArticleEvaluationRuns.Where(value => runIds.Contains(value.RunId)).ExecuteDeleteAsync();
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlyList<CalibrationReference> References =
    [
        new("c1", "supported", "test"),
        new("c2", "unsupported", "test"),
        new("c3", "uncertain", "test")
    ];
    private const string ProfileA = "profile-a";
    private const string ProfileB = "profile-b";
}
