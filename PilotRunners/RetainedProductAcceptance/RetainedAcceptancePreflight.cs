using Microsoft.Extensions.Configuration;
using System.Text.Json.Nodes;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using AcademicCollectorDemo.Modules.AcademicPerformance.FacultyAssistant;
using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleReviews;
using AcademicCollector.Analysis.Contracts;

namespace ServiceAcceptancePilot;

internal static class RetainedAcceptancePreflight
{
    public static async Task<int> RunAsync()
    {
        await RetainedAcceptanceSelfTest.AssertAsync();
        string root = FindRoot();
        JsonObject frozen = await RetainedSourceAudit.VerifyFrozenArtifactsAsync(root);
        bool keyPresent = !string.IsNullOrWhiteSpace(new ConfigurationBuilder()
            .AddUserSecrets(typeof(ResearcherAnalysisService.Program).Assembly, true).Build()["Gemini:ApiKey"]);
        SqlConnectionStringBuilder baselineConnection = new()
        {
            DataSource = @"(localdb)\MSSQLLocalDB", InitialCatalog = RetainedAcceptanceDatabase.SourceName,
            IntegratedSecurity = true, TrustServerCertificate = true
        };
        AiOptions? ai = null;
        FacultyAssistantOptions? faculty = null;
        ArticleReviewOptions? review = null;
        ArticleReviewRuntimeConfiguration? reviewRuntime = null;
        bool startupValidatorsExecuted = false;
        if (keyPresent)
        {
            await using WebApplication releasedHost = RetainedAcceptanceHost.CreateLiveAnalysisApplication(
                baselineConnection.ConnectionString, "http://127.0.0.1:5130",
                new ServiceAcceptanceBudget(_ => Task.CompletedTask));
            IStartupValidator? analysisValidator = releasedHost.Services.GetService<IStartupValidator>();
            analysisValidator?.Validate();
            ai = releasedHost.Services.GetRequiredService<IOptions<AiOptions>>().Value;
            reviewRuntime = releasedHost.Services.GetRequiredService<ArticleReviewStageExecutor>()
                .GetConfiguration();
            await using WebApplication collectorHost = RetainedAcceptanceHost.CreateCollectorApplication(
                root, baselineConnection.ConnectionString, "http://127.0.0.1:5230",
                "http://127.0.0.1:5130", "idle");
            IStartupValidator? collectorValidator = collectorHost.Services.GetService<IStartupValidator>();
            collectorValidator?.Validate();
            faculty = collectorHost.Services.GetRequiredService<IOptions<FacultyAssistantOptions>>().Value;
            review = collectorHost.Services.GetRequiredService<IOptions<ArticleReviewOptions>>().Value;
            startupValidatorsExecuted = analysisValidator is not null && collectorValidator is not null;
        }
        bool exact = startupValidatorsExecuted && ai is not null && ai.TimeoutSeconds == 180 &&
            ai.ArticleProvider == "Gemini" && ai.ArticleModel == ServiceAcceptanceBudget.RequiredModel &&
            (string.IsNullOrWhiteSpace(ai.ArticleVerifierModel) ? ai.ArticleModel : ai.ArticleVerifierModel) ==
                ServiceAcceptanceBudget.RequiredModel &&
            ai.ArticleMaxOutputTokens == 8192 && ai.ArticleVerifierMaxOutputTokens == 8192 &&
            ai.FacultyAssistantMaxOutputTokens == 16384 && ai.ArticleGenerationThinkingLevel == "high" &&
            ai.FacultyAssistantGenerationThinkingLevel == "medium" &&
            ai.ArticleVerifierThinkingLevel == "high" && ArticleReviewPrompt.Version == "article-specialist-review-v4" &&
            ArticleReviewVerificationPrompt.Version == "article-specialist-review-verification-v4" &&
            FacultyAssistantPrompt.Version == "faculty-evidence-assistant-v7" &&
            FacultyAssistantVerificationPrompt.Version == "faculty-evidence-assistant-verification-v5" &&
            FacultyRequestCoveragePrompt.Version == "faculty-request-coverage-v1" &&
            faculty is { RequestTimeoutSeconds: 750, PollSeconds: 1, WorkerEnabled: false } &&
            review is { PolicyVersion: "article-specialist-review-policy-v5", TotalTimeoutSeconds: 600,
                MaximumProviderCalls: 24, MaximumSpendUsd: 1.0m } &&
            reviewRuntime is { Provider: "Gemini", GenerationModel: ServiceAcceptanceBudget.RequiredModel,
                VerificationModel: ServiceAcceptanceBudget.RequiredModel, GenerationMaxOutputTokens: 8192,
                GenerationThinkingLevel: ArticleReviewGenerationRecovery.InitialThinkingLevel,
                GenerationRecoveryPolicyVersion: ArticleReviewGenerationRecovery.PolicyVersion,
                GenerationRecoveryThinkingLevel: ArticleReviewGenerationRecovery.RecoveryThinkingLevel };
        bool sourceOnline = await RetainedAcceptanceDatabase.SourceIsOnlineAsync();
        RetainedBaseline? baseline = sourceOnline
            ? await RetainedAcceptanceDatabase.InspectBaselineAsync(baselineConnection.ConnectionString) : null;
        JsonObject? sqlValidation = sourceOnline
            ? await LiveRetainedProductAcceptance.ValidateReadOnlySqlAsync(
                baselineConnection.ConnectionString) : null;
        JsonObject? qualificationReuse = sourceOnline
            ? await RetainedQualificationRegression.RunAsync(
                baselineConnection.ConnectionString, writeArtifacts: false) : null;
        JsonObject result = new()
        {
            ["runId"] = Program.RunId, ["generatedAtUtc"] = DateTimeOffset.UtcNow,
            ["ready"] = exact && keyPresent && sourceOnline && sqlValidation is not null &&
                qualificationReuse?["allCriteriaMatched"]?.GetValue<bool>() == true &&
                qualificationReuse?["freshCalls"]?.GetValue<int>() == 0 &&
                qualificationReuse?["reusedCalls"]?.GetValue<int>() == 6,
            ["externalNetworkRequests"] = 0, ["databaseWrites"] = 0,
            ["frozenArtifacts"] = frozen, ["sourceDatabaseOnline"] = sourceOnline,
            ["sourceBaseline"] = baseline is null ? null : JsonSerializer.SerializeToNode(baseline),
            ["readOnlySqlValidation"] = sqlValidation,
            ["qualificationReuseValidation"] = qualificationReuse,
            ["exactReleasedConfiguration"] = exact, ["geminiApiKeyPresent"] = keyPresent,
            ["startupValidatorsExecuted"] = startupValidatorsExecuted,
            ["secretSerialized"] = false,
            ["collectorFacultyTimeoutSeconds"] = faculty?.RequestTimeoutSeconds,
            ["collectorReviewPolicyVersion"] = review?.PolicyVersion,
            ["collectorReviewTimeoutSeconds"] = review?.TotalTimeoutSeconds,
            ["collectorReviewMaximumCalls"] = review?.MaximumProviderCalls,
            ["collectorReviewMaximumSpendUsd"] = review?.MaximumSpendUsd,
            ["analysisReviewRuntime"] = reviewRuntime is null ? null : JsonSerializer.SerializeToNode(reviewRuntime),
            ["analysisProviderTimeoutSeconds"] = ai?.TimeoutSeconds,
            ["facultyPollMinutes"] = 14, ["clonePerformed"] = false,
            ["releaseLock"] = "exact internal value plus --execute-paid-acceptance"
        };
        string path = Path.Combine(root, "docs", Program.RunId, "preflight.json");
        await FinalAcceptanceArtifacts.WriteAtomicAsync(path, result.ToJsonString(new() { WriteIndented = true }));
        Console.WriteLine(path);
        Console.WriteLine(result["ready"]!.GetValue<bool>() ? "PREFLIGHT_READY" : "PREFLIGHT_BLOCKED");
        return result["ready"]!.GetValue<bool>() ? 0 : 1;
    }

    internal static string FindRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "AcademicCollectorDemo.csproj")))
            current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
