using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AcademicCollector.Analysis.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Configuration;

namespace ServiceAcceptancePilot;

internal static class ServiceAcceptancePreflight
{
    private static readonly string SourceDirectory =
        Environment.GetEnvironmentVariable("ACADEMIC_ACCEPTANCE_SOURCE_DIRECTORY") ??
        Path.Combine(Path.GetTempPath(), "academic-fulltext-source-t9aqbsak");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<int> RunAsync()
    {
        string root = FindRoot();
        await ServiceAcceptanceSelfTest.AssertAsync();
        JsonArray sources = [];
        sources.Add(await InspectSourceAsync("adam", "adam-v1-fulltext.pdf",
            "935a5a15616961aff21529d86a754570028843407adfe858f1d18584b84293a7"));
        sources.Add(await InspectSourceAsync("football", "football-fulltext.pdf",
            "9c3d977e50059edce06618dc5bc5454b393ff50d9539d2400aafb252cc4dae58"));

        IConfigurationRoot analysis = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(root, "ResearcherAnalysisService"))
            .AddJsonFile("appsettings.json", optional: false)
            .Build();
        AiOptions ai = analysis.GetSection("Ai").Get<AiOptions>() ?? new AiOptions();
        string effectiveVerifierModel = string.IsNullOrWhiteSpace(ai.ArticleVerifierModel)
            ? ai.ArticleModel : ai.ArticleVerifierModel;
        bool exactModels = ai.ArticleModel == ServiceAcceptanceBudget.RequiredModel &&
            effectiveVerifierModel == ServiceAcceptanceBudget.RequiredModel &&
            ai.ArticleMaxOutputTokens == 8192 && ai.ArticleVerifierMaxOutputTokens == 8192 &&
            ai.FacultyAssistantMaxOutputTokens == 16384 &&
            ai.ArticleGenerationThinkingLevel == "high" && ai.ArticleVerifierThinkingLevel == "high";
        string[] requiredFiles =
        [
            "Modules/AcademicPerformance/Service/Data/Migrations/Core/202609110003_AddCanonicalAcademicData.cs",
            "Modules/AcademicPerformance/Service/Data/Migrations/Core/202609110004_AddCanonicalArticleEvidence.cs",
            "Modules/AcademicPerformance/Service/Data/Migrations/Core/202609110005_AddArticleSummaryAutomation.cs",
            "Modules/AcademicPerformance/Service/Data/Migrations/Core/202609110006_AddPublicationMetricSnapshots.cs",
            "Modules/AcademicPerformance/Service/Data/Migrations/Core/202609120010_AddHrEvidenceDossiers.cs",
            "Modules/AcademicPerformance/Service/Data/Migrations/Core/202609120011_AddFacultyAssistant.cs"
        ];
        JsonObject result = new()
        {
            ["runId"] = Program.RunId,
            ["generatedAtUtc"] = DateTimeOffset.UtcNow,
            ["ready"] = exactModels && sources.All(value => value?["valid"]?.GetValue<bool>() == true) &&
                requiredFiles.All(value => File.Exists(Path.Combine(root, value.Replace('/', Path.DirectorySeparatorChar)))) &&
                HasGeminiKey(),
            ["networkPolicy"] = new JsonObject
            {
                ["preflightExternalNetwork"] = "denied",
                ["liveProviderMetadata"] = "bounded HTTP replay; not live provider collection",
                ["liveSource"] = "immutable public-PDF bytes supplied by an in-memory replay handler",
                ["gemini"] = "guarded exact generateContent POST only after release"
            },
            ["sourceScope"] = "Two saved public PDFs; fresh PDF extraction and fresh Turkish summaries are required.",
            ["sources"] = sources,
            ["effectiveAi"] = new JsonObject
            {
                ["articleProvider"] = ai.ArticleProvider,
                ["articleModel"] = ai.ArticleModel,
                ["requestedArticleVerifierModel"] = ai.ArticleVerifierModel,
                ["effectiveArticleVerifierModel"] = effectiveVerifierModel,
                ["generationMaxOutputTokens"] = ai.ArticleMaxOutputTokens,
                ["verificationMaxOutputTokens"] = ai.ArticleVerifierMaxOutputTokens,
                ["facultyMaxOutputTokens"] = ai.FacultyAssistantMaxOutputTokens,
                ["generationThinking"] = ai.ArticleGenerationThinkingLevel,
                ["verificationThinking"] = ai.ArticleVerifierThinkingLevel,
                ["facultyPromptVersion"] = FacultyAssistantPrompt.Version,
                ["facultyVerificationPromptVersion"] = FacultyAssistantVerificationPrompt.Version,
                ["exactReleasedConfiguration"] = exactModels
            },
            ["budget"] = new JsonObject
            {
                ["maximumGeminiCalls"] = ServiceAcceptanceBudget.MaximumCalls,
                ["maximumEstimatedSpendUsd"] = ServiceAcceptanceBudget.MaximumSpendUsd,
                ["reservationPersistedBeforeDispatch"] = true,
                ["unknownUsageStopsDispatch"] = true,
                ["failedLedgerNeverReset"] = true
            },
            ["credentials"] = new JsonObject
            {
                ["geminiApiKeyPresentInAnalysisUserSecrets"] = HasGeminiKey(),
                ["secretValueSerialized"] = false
            },
            ["routeStages"] = new JsonArray(
                "Bulk/Submit actual endpoint",
                "Bulk/Status and GetResearcher/ListCanonicalPublications readback",
                "normal ResearcherCollectionHandler with bounded ORCID HTTP DTO replay",
                "CanonicalWorkSynchronizer.SyncAsync(scheduleArticleSummaries:true)",
                "automatic Turkish summary worker after fresh extraction from immutable public-PDF replay bytes",
                "publication metrics refresh worker/read",
                "HR dossier metadata+metrics with honest Reviews=[] when no specialist review exists",
                "pure-Turkish OwnPaperMethods and TeachingHelp requests",
                "GeminiFacultyAssistantGenerator plus dedicated GeminiFacultyAssistantVerifier",
                "SQL/public readback and duplicate replay with zero new AI calls"),
            ["restartChecks"] = new JsonArray(
                "pending bulk survives owned collector-host restart",
                "pending summary survives owned collector-host restart",
                "pending faculty survives owned collector-host restart",
                "abandoned same-input summary is terminal Interrupted with no automatic redispatch",
                "unknown in-flight faculty synthetic fault is terminal Interrupted with no automatic retry"),
            ["schemaCoverage"] = new JsonObject
            {
                ["requiredMigrationSources"] = JsonSerializer.SerializeToNode(requiredFiles, JsonOptions),
                ["allPresent"] = requiredFiles.All(value => File.Exists(
                    Path.Combine(root, value.Replace('/', Path.DirectorySeparatorChar))))
            },
            ["deferred"] = new JsonArray("generic API/BYS authorization", "UI", "fresh specialist article reviews")
        };
        string directory = Path.Combine(root, "docs", Program.RunId);
        Directory.CreateDirectory(directory);
        string output = Path.Combine(directory, "preflight.json");
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(result, JsonOptions), Encoding.UTF8);
        Console.WriteLine(output);
        Console.WriteLine(result["ready"]!.GetValue<bool>() ? "PREFLIGHT_READY" : "PREFLIGHT_BLOCKED");
        return result["ready"]!.GetValue<bool>() ? 0 : 1;
    }

    private static async Task<JsonObject> InspectSourceAsync(string name, string file, string expectedHash)
    {
        string path = Path.Combine(SourceDirectory, file);
        byte[] bytes = await File.ReadAllBytesAsync(path);
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        ArticlePdfExtractor extractor = new(Options.Create(new ArticleSummaryOptions
        {
            MaximumPages = 40, MaximumExtractedCharacters = 160000, OcrEnabled = false
        }));
        SummarizeArticleRequest extracted = extractor.Extract(bytes, "tr");
        bool catalogValid = ArticleSourceCatalog.IsValid(
            extracted.Pages, extracted.SourceSpans, extracted.SourceKind);
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
            ["valid"] = hash == expectedHash && catalogValid && extracted.Pages.Count > 0 &&
                extracted.SourceSpans?.Count > 0
        };
    }

    private static bool HasGeminiKey()
    {
        IConfigurationRoot secrets = new ConfigurationBuilder()
            .AddUserSecrets(typeof(ResearcherAnalysisService.Program).Assembly, optional: true).Build();
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
