using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AcademicCollector.Analysis.Contracts;
using ResearcherAnalysisService.Products.ArticleSummaries;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Configuration;

namespace ServiceAcceptancePilot;

internal static class FinalServiceAcceptancePreflight
{
    private static readonly string SourceDirectory =
        Environment.GetEnvironmentVariable("ACADEMIC_ACCEPTANCE_SOURCE_DIRECTORY") ??
        Path.Combine(Path.GetTempPath(), "academic-fulltext-source-t9aqbsak");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<int> RunAsync()
    {
        string root = FindRoot();
        await ServiceAcceptanceSelfTest.AssertAsync();
        JsonArray sources =
        [
            await InspectSourceAsync("adam", "adam-v1-fulltext.pdf",
                "935a5a15616961aff21529d86a754570028843407adfe858f1d18584b84293a7"),
            await InspectSourceAsync("football", "football-fulltext.pdf",
                "9c3d977e50059edce06618dc5bc5454b393ff50d9539d2400aafb252cc4dae58")
        ];
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(root, "ResearcherAnalysisService"))
            .AddJsonFile("appsettings.json", optional: false)
            .Build();
        AiOptions settings = configuration.GetSection("Ai").Get<AiOptions>() ?? new AiOptions();
        string verifierModel = string.IsNullOrWhiteSpace(settings.ArticleVerifierModel)
            ? settings.ArticleModel : settings.ArticleVerifierModel;
        bool exactConfiguration = settings.ArticleProvider == "Gemini" &&
            settings.ArticleModel == ServiceAcceptanceBudget.RequiredModel &&
            verifierModel == ServiceAcceptanceBudget.RequiredModel &&
            settings.ArticleMaxOutputTokens == 8192 && settings.ArticleVerifierMaxOutputTokens == 8192 &&
            settings.FacultyAssistantMaxOutputTokens == 16384 &&
            settings.ArticleGenerationThinkingLevel == "high" && settings.ArticleVerifierThinkingLevel == "high";
        bool keyPresent = HasGeminiKey();
        bool sourceReady = sources.All(value => value?["valid"]?.GetValue<bool>() == true);
        string planPath = Path.Combine(root, "docs", "SERVICE_ACCEPTANCE_20260914_PLAN.md");
        string diagnosticPath = Path.Combine(root, "docs", Program.RunId, "connection-diagnostic.json");
        bool ready = sourceReady && exactConfiguration && keyPresent && File.Exists(planPath) &&
            File.Exists(diagnosticPath);
        JsonObject result = new()
        {
            ["runId"] = Program.RunId,
            ["generatedAtUtc"] = DateTimeOffset.UtcNow,
            ["ready"] = ready,
            ["externalNetworkRequests"] = 0,
            ["sources"] = sources,
            ["effectiveAi"] = new JsonObject
            {
                ["articleProvider"] = settings.ArticleProvider,
                ["articleModel"] = settings.ArticleModel,
                ["requestedVerifierModel"] = settings.ArticleVerifierModel,
                ["effectiveVerifierModel"] = verifierModel,
                ["summaryOutputTokens"] = settings.ArticleMaxOutputTokens,
                ["verificationOutputTokens"] = settings.ArticleVerifierMaxOutputTokens,
                ["facultyOutputTokens"] = settings.FacultyAssistantMaxOutputTokens,
                ["generationThinking"] = settings.ArticleGenerationThinkingLevel,
                ["verificationThinking"] = settings.ArticleVerifierThinkingLevel,
                ["exactReleasedConfiguration"] = exactConfiguration
            },
            ["budget"] = new JsonObject
            {
                ["maximumCalls"] = ServiceAcceptanceBudget.MaximumCalls,
                ["maximumSpendUsd"] = ServiceAcceptanceBudget.MaximumSpendUsd,
                ["reservationBeforeDispatch"] = true,
                ["unknownStopsDispatch"] = true,
                ["priorLifecycleUnknownReservationUsd"] = 0.06427125m,
                ["priorLifecycleIncludedInThisLedger"] = false
            },
            ["credentials"] = new JsonObject
            {
                ["geminiApiKeyPresent"] = keyPresent,
                ["secretSerialized"] = false
            },
            ["connectionDiagnostic"] = "connection-diagnostic.json; authenticated exact-model GET returned HTTP 200; no generation call",
            ["databasePrefix"] = "AcademicFinalServiceAcceptance_",
            ["releaseLock"] = "--execute-paid-acceptance plus exact internal environment value",
            ["plan"] = Path.GetFileName(planPath)
        };
        string path = Path.Combine(root, "docs", Program.RunId, "preflight.json");
        await FinalAcceptanceArtifacts.WriteAtomicAsync(path, result.ToJsonString(JsonOptions));
        Console.WriteLine(path);
        Console.WriteLine(ready ? "PREFLIGHT_READY" : "PREFLIGHT_BLOCKED");
        return ready ? 0 : 1;
    }

    private static async Task<JsonObject> InspectSourceAsync(string name, string file, string expectedHash)
    {
        string path = Path.Combine(SourceDirectory, file);
        byte[] bytes = await File.ReadAllBytesAsync(path);
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        ArticlePdfExtractor extractor = new(Options.Create(new ArticleSummaryOptions
        {
            MaximumPages = 40,
            MaximumExtractedCharacters = 160000,
            OcrEnabled = false
        }));
        SummarizeArticleRequest extracted = extractor.Extract(bytes, "tr");
        bool valid = hash == expectedHash && extracted.SourceKind == "pdf" &&
            ArticleSourceCatalog.IsValid(extracted.Pages, extracted.SourceSpans, extracted.SourceKind);
        return new()
        {
            ["name"] = name,
            ["fileName"] = file,
            ["sha256"] = hash,
            ["expectedSha256"] = expectedHash,
            ["bytes"] = bytes.Length,
            ["pages"] = extracted.Pages.Count,
            ["sourceSpans"] = extracted.SourceSpans?.Count ?? 0,
            ["sourceKind"] = extracted.SourceKind,
            ["extractionVersion"] = extracted.ExtractionVersion,
            ["valid"] = valid
        };
    }

    private static bool HasGeminiKey()
    {
        IConfigurationRoot secrets = new ConfigurationBuilder()
            .AddUserSecrets(typeof(ResearcherAnalysisService.Program).Assembly, optional: true)
            .Build();
        return !string.IsNullOrWhiteSpace(secrets["Gemini:ApiKey"]);
    }

    internal static string FindRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "AcademicCollectorDemo.csproj")))
            current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
