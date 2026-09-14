using System.Text.Json;
using System.Text.Json.Nodes;
using AcademicCollector.Analysis.Contracts;
using ResearcherAnalysisService.Products.ArticleReviews;
using ResearcherAnalysisService.Products.FacultyAssistant;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Configuration;

namespace ServiceAcceptancePilot;

internal static class RetainedTeachingDiagnosticPreflight
{
    internal static async Task<int> RunAsync()
    {
        await RetainedAcceptanceSelfTest.AssertAsync();
        string root = RetainedAcceptancePreflight.FindRoot();
        JsonObject frozen = await RetainedFacultyCompletion.VerifyV13Async(root);
        RetainedTeachingBaseline baseline = await RetainedTeachingBaselineAudit.InspectAsync();
        bool keyPresent = !string.IsNullOrWhiteSpace(new ConfigurationBuilder()
            .AddUserSecrets(typeof(ResearcherAnalysisService.Program).Assembly, true).Build()["Gemini:ApiKey"]);
        AiOptions? ai = null;
        FacultyAssistantOptions? faculty = null;
        ArticleReviewOptions? review = null;
        ArticleReviewRuntimeConfiguration? reviewRuntime = null;
        bool validators = false;
        ServiceAcceptanceBudget validationBudget = new(_ => Task.CompletedTask, maximumCalls: 38,
            maximumSpendUsd: 2m);
        if (keyPresent)
        {
            await using WebApplication analysis = RetainedAcceptanceHost.CreateLiveAnalysisApplication(
                RetainedTeachingBaselineAudit.ConnectionString, "http://127.0.0.1:5130", validationBudget);
            IStartupValidator? analysisValidator = analysis.Services.GetService<IStartupValidator>();
            analysisValidator?.Validate();
            ai = analysis.Services.GetRequiredService<IOptions<AiOptions>>().Value;
            reviewRuntime = analysis.Services.GetRequiredService<ArticleReviewStageExecutor>().GetConfiguration();
            await using WebApplication collector = RetainedAcceptanceHost.CreateCollectorApplication(root,
                RetainedTeachingBaselineAudit.ConnectionString, "http://127.0.0.1:5230",
                "http://127.0.0.1:5130", "idle");
            IStartupValidator? collectorValidator = collector.Services.GetService<IStartupValidator>();
            collectorValidator?.Validate();
            faculty = collector.Services.GetRequiredService<IOptions<FacultyAssistantOptions>>().Value;
            review = collector.Services.GetRequiredService<IOptions<ArticleReviewOptions>>().Value;
            validators = analysisValidator is not null && collectorValidator is not null;
        }
        long pending = await InspectPendingAsync();
        (int football, int contextVersion) = await RetainedTeachingDiagnostic.ResolveInputAsync();
        JsonArray newUsageValidation;
        await using (SqlConnection connection = new(RetainedTeachingBaselineAudit.ConnectionString))
        {
            await connection.OpenAsync();
            newUsageValidation = await LiveRetainedProductAcceptance.AuditNewUsageAsync(
                connection, baseline.AttemptIds, validationBudget.Snapshot());
        }
        bool exact = validators && ai is
            {
                TimeoutSeconds: 180, ArticleProvider: "Gemini", ArticleModel: ServiceAcceptanceBudget.RequiredModel,
                ArticleMaxOutputTokens: 8192, ArticleVerifierMaxOutputTokens: 8192,
                FacultyAssistantMaxOutputTokens: 16384, ArticleGenerationThinkingLevel: "high",
                ArticleVerifierThinkingLevel: "high", FacultyAssistantGenerationThinkingLevel: "medium",
                FacultyAssistantVerifierThinkingLevel: "medium"
            } &&
            (string.IsNullOrWhiteSpace(ai.ArticleVerifierModel) ? ai.ArticleModel : ai.ArticleVerifierModel) ==
                ServiceAcceptanceBudget.RequiredModel &&
            faculty is { WorkerEnabled: false, PollSeconds: 1, RequestTimeoutSeconds: 1800 } &&
            review is { PolicyVersion: "article-specialist-review-policy-v5", TotalTimeoutSeconds: 600,
                MaximumProviderCalls: 24, MaximumSpendUsd: 1.0m } &&
            reviewRuntime is { Provider: "Gemini", GenerationModel: ServiceAcceptanceBudget.RequiredModel,
                VerificationModel: ServiceAcceptanceBudget.RequiredModel, GenerationMaxOutputTokens: 8192,
                GenerationThinkingLevel: ArticleReviewGenerationRecovery.InitialThinkingLevel,
                GenerationRecoveryPolicyVersion: ArticleReviewGenerationRecovery.PolicyVersion,
                GenerationRecoveryThinkingLevel: ArticleReviewGenerationRecovery.RecoveryThinkingLevel } &&
            FacultyAssistantPrompt.Version == "faculty-evidence-assistant-v8" &&
            FacultyAssistantVerificationPrompt.Version == "faculty-evidence-assistant-verification-v6" &&
            FacultyAssistantRepairPrompt.Version == "faculty-evidence-assistant-repair-v1" &&
            FacultyRequestCoveragePrompt.Version == "faculty-request-coverage-v1" &&
            await RetainedFacultyQualificationRegression.ValidateReadOnlySqlAsync(
                RetainedTeachingBaselineAudit.ConnectionString) == 3;
        JsonObject result = new()
        {
            ["runId"] = Program.RunId, ["generatedAtUtc"] = DateTimeOffset.UtcNow,
            ["ready"] = keyPresent && exact && pending == 0 && football > 0 && contextVersion == 1 &&
                newUsageValidation.Count == 0,
            ["externalNetworkRequests"] = 0, ["databaseWrites"] = 0, ["clonePerformed"] = false,
            ["targetDatabase"] = RetainedTeachingBaselineAudit.DatabaseName,
            ["frozenV13"] = frozen, ["baseline"] = JsonSerializer.SerializeToNode(baseline),
            ["fingerprintRowsValidated"] = new JsonObject
            {
                ["usage"] = baseline.AttemptIds.Count, ["faculty"] = baseline.FacultyRunIds.Count,
                ["reviews"] = baseline.ReviewRunIds.Count, ["dossiers"] = baseline.DossierIds.Count,
                ["contexts"] = baseline.ContextIds.Count, ["sourceSnapshots"] = baseline.SourceSnapshotIds.Count,
                ["lastUsageAttemptId"] = baseline.AttemptIds.Last().ToString(),
                ["lastFacultyRowId"] = baseline.FacultyRunIds.Last()
            },
            ["pendingOrRunningFacultyRows"] = pending, ["footballCanonicalWorkId"] = football,
            ["perRowUsageSqlValidatedWithZeroNewRows"] = newUsageValidation.Count == 0,
            ["contextVersion"] = contextVersion, ["exactReleasedConfiguration"] = exact,
            ["startupValidatorsExecuted"] = validators, ["geminiApiKeyPresent"] = keyPresent,
            ["maximumCalls"] = 38, ["maximumSpendUsd"] = 2m,
            ["providerCaptureMaximumBytesPerArtifact"] = 4 * 1024 * 1024,
            ["unrelatedWorkersEnabled"] = false, ["secretSerialized"] = false,
            ["releaseLock"] = "exact internal V14 value plus --execute-paid-acceptance"
        };
        string path = Path.Combine(root, "docs", Program.RunId, "preflight.json");
        await FinalAcceptanceArtifacts.WriteAtomicAsync(path, result.ToJsonString(new() { WriteIndented = true }));
        Console.WriteLine(path);
        Console.WriteLine(result["ready"]!.GetValue<bool>() ? "PREFLIGHT_READY" : "PREFLIGHT_BLOCKED");
        return result["ready"]!.GetValue<bool>() ? 0 : 1;
    }

    private static async Task<long> InspectPendingAsync()
    {
        await using SqlConnection connection = new(RetainedTeachingBaselineAudit.ConnectionString);
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT_BIG(*) FROM [faculty].[AssistantRuns] WHERE Status IN ('Pending','Running')";
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
