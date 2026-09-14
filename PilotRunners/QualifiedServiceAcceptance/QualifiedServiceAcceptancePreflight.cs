using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;
using Microsoft.Extensions.Configuration;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Configuration;

namespace ServiceAcceptancePilot;

internal static class QualifiedServiceAcceptancePreflight
{
    private static readonly string SourceDirectory =
        Environment.GetEnvironmentVariable("ACADEMIC_ACCEPTANCE_SOURCE_DIRECTORY") ??
        Path.Combine(Path.GetTempPath(), "academic-fulltext-source-t9aqbsak");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<int> RunAsync()
    {
        string root = FinalServiceAcceptancePreflight.FindRoot();
        await ServiceAcceptanceSelfTest.AssertAsync();
        bool sourcesReady = Hash("adam-v1-fulltext.pdf") ==
                "935a5a15616961aff21529d86a754570028843407adfe858f1d18584b84293a7" &&
            Hash("football-fulltext.pdf") ==
                "9c3d977e50059edce06618dc5bc5454b393ff50d9539d2400aafb252cc4dae58";
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(root, "ResearcherAnalysisService"))
            .AddJsonFile("appsettings.json", optional: false).Build();
        AiOptions settings = configuration.GetSection("Ai").Get<AiOptions>() ?? new();
        string verifierModel = string.IsNullOrWhiteSpace(settings.ArticleVerifierModel)
            ? settings.ArticleModel : settings.ArticleVerifierModel;
        bool exactConfiguration = settings.ArticleProvider == "Gemini" &&
            settings.ArticleModel == ServiceAcceptanceBudget.RequiredModel &&
            verifierModel == ServiceAcceptanceBudget.RequiredModel &&
            settings.ArticleMaxOutputTokens == 8192 && settings.ArticleVerifierMaxOutputTokens == 8192 &&
            settings.FacultyAssistantMaxOutputTokens == 16384 &&
            settings.ArticleGenerationThinkingLevel == "high" && settings.ArticleVerifierThinkingLevel == "high";
        IConfigurationRoot academicConfiguration = new ConfigurationBuilder()
            .SetBasePath(root).AddJsonFile("academicsettings.json", optional: false).Build();
        ArticleSummaryAutomationOptions configuredSummary = academicConfiguration
            .GetSection("ArticleSummaryAutomation").Get<ArticleSummaryAutomationOptions>() ?? new();
        bool correctedPolicies = new ArticleSummaryAutomationOptions().PolicyVersion == "article-summary-v5" &&
            configuredSummary.PolicyVersion == "article-summary-v5" &&
            ArticleSummaryPrompt.Version == "article-summary-source-ids-v6" &&
            ArticleVerificationPrompt.Version == "article-claim-verification-v6" &&
            FacultyAssistantPrompt.Version == "faculty-evidence-assistant-v5" &&
            FacultyAssistantVerificationPrompt.Version == "faculty-evidence-assistant-verification-v4" &&
            FacultyRequestCoveragePrompt.Version == "faculty-request-coverage-v1";
        bool keyPresent = HasGeminiKey();
        string v1 = Path.Combine(root, "docs", "service-acceptance-20260914-v1");
        string v2 = Path.Combine(root, "docs", "service-acceptance-20260914-v2");
        string v3 = Path.Combine(root, "docs", "service-acceptance-20260914-v3");
        string v4 = Path.Combine(root, "docs", "service-acceptance-20260914-v4");
        bool v1Frozen = File.Exists(Path.Combine(v1, "audit.json")) &&
            File.Exists(Path.Combine(v1, "artifact-sha256-manifest.json"));
        bool v2Frozen = File.Exists(Path.Combine(v2, "failure.json")) &&
            File.Exists(Path.Combine(v2, "qualification-generic-request.json")) &&
            File.Exists(Path.Combine(v2, "artifact-sha256-manifest.json"));
        bool v3Frozen = File.Exists(Path.Combine(v3, "failure.json")) &&
            File.Exists(Path.Combine(v3, "qualification-generic-qualified.json")) &&
            File.Exists(Path.Combine(v3, "artifact-sha256-manifest.json"));
        bool v4Frozen = File.Exists(Path.Combine(v4, "result.json")) &&
            File.Exists(Path.Combine(v4, "artifact-sha256-manifest.json"));
        bool ready = sourcesReady && exactConfiguration && correctedPolicies && keyPresent &&
            v1Frozen && v2Frozen && v3Frozen && v4Frozen;
        JsonObject result = new()
        {
            ["runId"] = Program.RunId,
            ["generatedAtUtc"] = DateTimeOffset.UtcNow,
            ["ready"] = ready,
            ["externalNetworkRequests"] = 0,
            ["sourcesReady"] = sourcesReady,
            ["exactGeminiConfiguration"] = exactConfiguration,
            ["correctedPolicies"] = correctedPolicies,
            ["v1AuditFrozen"] = v1Frozen,
            ["v2FailureFrozen"] = v2Frozen,
            ["v3FailureFrozen"] = v3Frozen,
            ["v4ResultFrozen"] = v4Frozen,
            ["credentials"] = new JsonObject { ["geminiApiKeyPresent"] = keyPresent, ["secretSerialized"] = false },
            ["budget"] = new JsonObject
            {
                ["maximumCalls"] = ServiceAcceptanceBudget.MaximumCalls,
                ["maximumSpendUsd"] = ServiceAcceptanceBudget.MaximumSpendUsd,
                ["v1KnownCostUsd"] = 0.551427m,
                ["v2KnownCostUsd"] = 0.0322485m,
                ["v3KnownCostUsd"] = 0.032181m,
                ["v4KnownCostUsd"] = 0.608958m,
                ["knownPriorSubtotalUsd"] = 1.2248145m,
                ["olderUnknownReservedUsd"] = 0.06427125m,
                ["priorSpendIncludedInCurrentLedger"] = false
            },
            ["releaseLock"] = "--execute-paid-acceptance plus exact internal v5 environment value"
        };
        string path = Path.Combine(root, "docs", Program.RunId, "preflight.json");
        await FinalAcceptanceArtifacts.WriteAtomicAsync(path, result.ToJsonString(JsonOptions));
        Console.WriteLine(path);
        Console.WriteLine(ready ? "PREFLIGHT_READY" : "PREFLIGHT_BLOCKED");
        return ready ? 0 : 1;
    }

    private static string Hash(string file) => Convert.ToHexString(SHA256.HashData(
        File.ReadAllBytes(Path.Combine(SourceDirectory, file)))).ToLowerInvariant();

    private static bool HasGeminiKey()
    {
        IConfigurationRoot secrets = new ConfigurationBuilder()
            .AddUserSecrets(typeof(ResearcherAnalysisService.Program).Assembly, optional: true).Build();
        return !string.IsNullOrWhiteSpace(secrets["Gemini:ApiKey"]);
    }
}
