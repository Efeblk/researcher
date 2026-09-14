using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ServiceAcceptancePilot;

internal static class RetainedSourceAudit
{
    internal const string ManifestSha = "68c3176e31230e11bd039b0fd876b1aa164089b2a74ed316ba79c999e093d16b";
    internal const string SummaryPhaseSha = "e7c5c1d8bec8bf058973db33a8faff9225c1a0ba746831427e15e167abfce74f";
    internal const string AdamSnapshotSha = "ef1663dd32671bce737af77d0cfd0777fd8ebd95bb86f5d0ff2b4aa457675fa3";
    internal const string FootballSnapshotSha = "c2f7f4ac5265813d06ac7f3776abc9b7dcdfe0c21efc1b943fc4059afab950ab";

    internal static async Task<JsonObject> VerifyFrozenArtifactsAsync(string root)
    {
        string directory = Path.Combine(root, "docs", "service-acceptance-20260914-v5");
        string manifestPath = Path.Combine(directory, "artifact-sha256-manifest.json");
        Require(await HashAsync(manifestPath) == ManifestSha, "The frozen v5 manifest hash changed.");
        JsonObject manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!.AsObject();
        JsonArray entries = manifest["artifacts"]?.AsArray() ?? throw new InvalidOperationException("Artifact list absent.");
        Require(entries.Count == 24, "The frozen v5 manifest must contain exactly 24 raw artifacts.");
        foreach (JsonNode? node in entries)
        {
            JsonObject entry = node!.AsObject();
            string relative = entry["path"]!.GetValue<string>();
            string path = Path.GetFullPath(Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar)));
            Require(path.StartsWith(Path.GetFullPath(directory) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase), "A manifest path escaped its frozen directory.");
            Require(File.Exists(path) && new FileInfo(path).Length == entry["bytes"]!.GetValue<long>() &&
                await HashAsync(path) == entry["sha256"]!.GetValue<string>(), $"Frozen artifact mismatch: {relative}");
        }
        string summaryPath = Path.Combine(directory, "phases", "005-automatic-pdf-summaries-complete.json");
        Require(await HashAsync(summaryPath) == SummaryPhaseSha, "The frozen v5 summary phase hash changed.");
        return new()
        {
            ["manifestPath"] = manifestPath,
            ["manifestSha256"] = ManifestSha,
            ["rawArtifactsVerified"] = entries.Count,
            ["executedSourcesCompared"] = false,
            ["summaryPhaseSha256"] = SummaryPhaseSha
        };
    }

    internal static async Task<JsonArray> CaptureExecutedSourcesAsync(string root)
    {
        string[] paths = Directory.GetFiles(Path.Combine(root, "PilotRunners", "RetainedProductAcceptance"), "*.cs")
            .Concat([
                Path.Combine(root, "PilotRunners", "RetainedProductAcceptance", "RetainedProductAcceptance.csproj"),
                Path.Combine(root, "PilotRunners", "FinalServiceAcceptance", "FinalAcceptanceArtifacts.cs"),
                Path.Combine(root, "PilotRunners", "FinalServiceAcceptance", "ServiceAcceptanceBudget.cs"),
                Path.Combine(root, "ResearcherAnalysisService", "Analysis", "ArticleReviewPrompt.cs"),
                Path.Combine(root, "ResearcherAnalysisService", "Analysis", "ArticleReviewVerificationPrompt.cs"),
                Path.Combine(root, "ResearcherAnalysisService", "Analysis", "ArticleReviewer.cs"),
                Path.Combine(root, "ResearcherAnalysisService", "Analysis", "ArticleReviewDispatchContext.cs"),
                Path.Combine(root, "ResearcherAnalysisService", "Analysis", "ArticleReviewStageExecutor.cs"),
                Path.Combine(root, "ResearcherAnalysisService", "Analysis", "FacultyAssistantPrompt.cs"),
                Path.Combine(root, "ResearcherAnalysisService", "Analysis", "FacultyAssistantVerificationPrompt.cs"),
                Path.Combine(root, "ResearcherAnalysisService", "Analysis", "FacultyRequestCoveragePrompt.cs"),
                Path.Combine(root, "ResearcherAnalysisService", "Analysis", "FacultyAssistantRepairPrompt.cs"),
                Path.Combine(root, "ResearcherAnalysisService", "Analysis", "IFacultyAssistantGenerator.cs"),
                Path.Combine(root, "ResearcherAnalysisService", "Analysis", "FacultyAssistant.cs"),
                Path.Combine(root, "ResearcherAnalysisService", "Configuration", "AiOptions.cs"),
                Path.Combine(root, "ResearcherAnalysisService", "Integrations", "Gemini", "GeminiArticleClient.cs"),
                Path.Combine(root, "ResearcherAnalysisService", "Integrations", "Gemini", "GeminiArticleReviewGenerator.cs"),
                Path.Combine(root, "ResearcherAnalysisService", "Integrations", "Gemini", "GeminiArticleReviewVerifier.cs"),
                Path.Combine(root, "ResearcherAnalysisService", "Integrations", "Gemini", "GeminiFacultyAssistantGenerator.cs"),
                Path.Combine(root, "ResearcherAnalysisService", "Integrations", "Gemini", "GeminiFacultyAssistantVerifier.cs"),
                Path.Combine(root, "ResearcherAnalysisService", "Integrations", "Gemini", "GeminiFacultyAssistantRepairGenerator.cs"),
                Path.Combine(root, "ResearcherAnalysisService", "Integrations", "Gemini", "GeminiFacultyRequestCoverageVerifier.cs"),
                Path.Combine(root, "Modules", "AcademicPerformance", "Service", "ArticleReviews", "ArticleReviewWorkflow.cs"),
                Path.Combine(root, "Modules", "AcademicPerformance", "Service", "ArticleReviews", "ArticleReviewServiceClient.cs"),
                Path.Combine(root, "Modules", "AcademicPerformance", "Service", "ArticleReviews", "ArticleReviewOptions.cs"),
                Path.Combine(root, "Modules", "AcademicPerformance", "Service", "FacultyAssistant", "FacultyAssistantProcessor.cs"),
                Path.Combine(root, "Modules", "AcademicPerformance", "Service", "FacultyAssistant", "FacultyAssistantOptions.cs"),
                Path.Combine(root, "Modules", "AcademicPerformance", "Service", "FacultyAssistant", "FacultyAssistantServiceClient.cs"),
                Path.Combine(root, "Modules", "AcademicPerformance", "Service", "Knowledge", "AcademicEvidenceSearchService.cs"),
                Path.Combine(root, "ResearcherAnalysis.Contracts", "FacultyAssistantContracts.cs"),
                Path.Combine(root, "ResearcherAnalysis.Contracts", "ArticleReviewContracts.cs"),
                Path.Combine(root, "ResearcherAnalysisService", "appsettings.json"),
                Path.Combine(root, "Modules", "AcademicPerformance", "Service", "Api", "V1", "Contracts", "FacultyAssistantContracts.cs"),
                Path.Combine(root, "academicsettings.json")
            ]).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray();
        string output = Path.Combine(root, "docs", Program.RunId, "executed-sources");
        Directory.CreateDirectory(output);
        JsonArray entries = [];
        foreach (string path in paths)
        {
            Require(File.Exists(path), $"Executed source is missing: {path}");
            string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            string copiedName = relative.Replace('/', '_') + ".txt";
            string destination = Path.Combine(output, copiedName);
            File.Copy(path, destination, false);
            entries.Add(new JsonObject
            {
                ["path"] = relative, ["copy"] = copiedName,
                ["bytes"] = new FileInfo(path).Length, ["sha256"] = await HashAsync(path)
            });
        }
        await FinalAcceptanceArtifacts.WriteAtomicAsync(Path.Combine(root, "docs", Program.RunId,
            "executed-source-manifest.json"), entries.ToJsonString(new() { WriteIndented = true }));
        return entries;
    }

    private static async Task<string> HashAsync(string path) => Convert.ToHexString(
        SHA256.HashData(await File.ReadAllBytesAsync(path))).ToLowerInvariant();

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
