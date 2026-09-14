using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AcademicCollector.Analysis.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Bulk;
using ResearcherAnalysisService.Products.ArticleSummaries;
using ResearcherAnalysisService.Products.FacultyAssistant;
using ResearcherAnalysisService.Products.Metrics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Configuration;

namespace ServiceAcceptancePilot;

internal static class BroaderServiceAcceptancePreflight
{
    internal static readonly string SourceDirectory =
        Environment.GetEnvironmentVariable("ACADEMIC_BROADER_ACCEPTANCE_SOURCE_DIRECTORY") ??
        Path.Combine(Path.GetTempPath(), "academic-broader-sources-2708c833685944458964a4b3e9bae752");
    internal const string PreservedDatabase =
        "AcademicQualifiedServiceAcceptanceV10_2bbb62932a7b47f684be7ed267856447";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        { WriteIndented = true };

    internal static readonly SourceDefinition[] Sources =
    [
        new("fine", "fine-is-a-price.pdf", 200291,
            "55b0b122a4a2b702b8214482e9f8fabf492c4ed50d6747a5de0c091eaa4d9f89",
            "10.1086/468061", "A Fine Is a Price", 2000,
            "https://rady.ucsd.edu/_files/faculty-research/uri-gneezy/fine.pdf"),
        new("reproducibility", "psychology-reproducibility.pdf", 677051,
            "84b0f9e63c3117be15f08ee31776d30b90e7fd4426fc0aeb0ef8d288b62fa5b4",
            "10.1126/science.aac4716", "Estimating the reproducibility of psychological science", 2015,
            "https://www.nicolaslab.org/files/reproducibility.pdf")
    ];

    public static async Task<int> RunAsync()
    {
        await AssertSelfTestAsync();
        string root = FindRoot();
        string directory = Path.Combine(root, "docs", Program.RunId);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "preflight.json");
        if (File.Exists(path))
            throw new InvalidOperationException("The frozen broader-acceptance preflight already exists.");

        JsonArray sources = [];
        foreach (SourceDefinition source in Sources)
            sources.Add(await InspectSourceAsync(source));
        bool portsFree = PortsAreFree(5135, 5235);
        bool keyPresent = BroaderAcceptanceHost.HasGeminiKey();
        bool protocols = FacultyAssistantPrompt.Version == "faculty-evidence-assistant-v10" &&
            FacultyAssistantVerificationPrompt.Version == "faculty-evidence-assistant-verification-v7" &&
            FacultyAssistantRepairPrompt.Version == "faculty-evidence-assistant-repair-v2" &&
            FacultyRequestCoveragePrompt.Version == "faculty-request-coverage-v2";
        JsonObject resolvedConfiguration = InspectResolvedConfiguration(root);
        bool configurationReady = resolvedConfiguration["valid"]?.GetValue<bool>() == true;
        bool sourceReady = sources.All(node => node?["valid"]?.GetValue<bool>() == true);
        bool ready = sourceReady && portsFree && keyPresent && protocols && configurationReady &&
            File.Exists(Path.Combine(root, "docs", "BROADER_SERVICE_ACCEPTANCE_20260914_PLAN.md"));
        JsonObject result = new()
        {
            ["runId"] = Program.RunId,
            ["generatedAtUtc"] = DateTimeOffset.UtcNow,
            ["ready"] = ready,
            ["externalNetworkRequests"] = 0,
            ["databaseReads"] = 0,
            ["databaseWrites"] = 0,
            ["preservedDatabaseExcluded"] = PreservedDatabase,
            ["sources"] = sources,
            ["portsFree"] = portsFree,
            ["geminiApiKeyPresent"] = keyPresent,
            ["protocolsExact"] = protocols,
            ["resolvedConfiguration"] = resolvedConfiguration,
            ["model"] = ServiceAcceptanceBudget.RequiredModel,
            ["maximumCalls"] = 96,
            ["maximumSpendUsd"] = 3.00m,
            ["maximumSummaryCalls"] = 30,
            ["maximumCallsPerFacultyPhase"] = 11,
            ["releaseValueSha256"] = Hash(Encoding.UTF8.GetBytes(Program.LiveReleaseValue)),
            ["releaseLock"] = "--execute-paid-acceptance plus exact internal environment value"
        };
        await WriteNewAsync(path, result.ToJsonString(JsonOptions));
        Console.WriteLine(path);
        Console.WriteLine(ready ? "PREFLIGHT_READY" : "PREFLIGHT_BLOCKED");
        return ready ? 0 : 1;
    }

    public static async Task<int> RunSelfTestAsync()
    {
        await AssertSelfTestAsync();
        Console.WriteLine("SELF_TEST_OK");
        return 0;
    }

    private static async Task AssertSelfTestAsync()
    {
        LiveBroaderServiceAcceptance.AssertStaticShape();
        LiveBroaderServiceAcceptance.AssertCorrectionShape();
        ServiceAcceptanceBudget budget = new(_ => Task.CompletedTask,
            maximumCalls: 96, maximumSpendUsd: 3.00m);
        Require(budget.Snapshot() is { MaximumCalls: 96, MaximumSpendUsd: 3.00m },
            "The bounded aggregate budget was not retained.");
        ServiceAcceptanceBudget continuationBudget = new(_ => Task.CompletedTask,
            maximumCalls: 66, maximumSpendUsd: 2.70632325m);
        Require(continuationBudget.Snapshot() is
                { MaximumCalls: 66, MaximumSpendUsd: 2.70632325m } &&
            new FinalAcceptanceArtifacts("synthetic-root", Program.ContinuationRunId).DirectoryPath ==
                Path.Combine("synthetic-root", "docs", Program.ContinuationRunId),
            "The continuation budget or artifact route changed.");
        ServiceAcceptanceBudget correctionBudget = new(_ => Task.CompletedTask,
            maximumCalls: 22, maximumSpendUsd: 2.3995035m);
        Require(correctionBudget.Snapshot() is
                { MaximumCalls: 22, MaximumSpendUsd: 2.3995035m } &&
            new FinalAcceptanceArtifacts("synthetic-root", Program.CorrectionRunId).DirectoryPath ==
                Path.Combine("synthetic-root", "docs", Program.CorrectionRunId),
            "The correction budget or artifact route changed.");
        ServiceAcceptanceBudget finalRelatedBudget = new(_ => Task.CompletedTask,
            maximumCalls: 11, maximumSpendUsd: 2.29281m);
        Require(finalRelatedBudget.Snapshot() is
                { MaximumCalls: 11, MaximumSpendUsd: 2.29281m } &&
            new FinalAcceptanceArtifacts("synthetic-root", Program.FinalRelatedRunId).DirectoryPath ==
                Path.Combine("synthetic-root", "docs", Program.FinalRelatedRunId),
            "The final RelatedWorks budget or artifact route changed.");
        BroaderDispatchGate gate = new();
        gate.SetPhase("faculty-self-test", budget);
        for (int index = 0; index < 11; index++) gate.AuthorizeDispatch();
        bool rejected = false;
        try { gate.AuthorizeDispatch(); }
        catch (ServiceAcceptanceBudgetException) { rejected = true; }
        Require(rejected, "A twelfth faculty dispatch was admitted.");

        string directory = Path.Combine(Path.GetTempPath(),
            "broader-provider-capture-self-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            RetainedProviderCapture capture = new(directory);
            using HttpRequestMessage request = new(HttpMethod.Post,
                "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.8-flash:generateContent")
            { Content = new StringContent("{\"marker\":\"safe\"}", Encoding.UTF8, "application/json") };
            request.Headers.Authorization = new("Bearer", "never-capture-this-secret");
            byte[] body = await request.Content.ReadAsByteArrayAsync();
            await capture.WriteRequestAsync(1, DateTime.UtcNow, request, body, default);
            string captured = await File.ReadAllTextAsync(Path.Combine(directory, "attempt-01-request.json"));
            Require(!captured.Contains("never-capture-this-secret", StringComparison.Ordinal),
                "Provider capture exposed a request header.");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                string resolved = Path.GetFullPath(directory);
                string expectedParent = Path.GetFullPath(Path.GetTempPath());
                Require(resolved.StartsWith(expectedParent, StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(resolved).StartsWith("broader-provider-capture-self-test-", StringComparison.Ordinal),
                    "Self-test cleanup target escaped its exact temporary prefix.");
                Directory.Delete(resolved, true);
            }
        }
    }

    private static async Task<JsonObject> InspectSourceAsync(SourceDefinition source)
    {
        string path = Path.Combine(SourceDirectory, source.FileName);
        byte[] bytes = await File.ReadAllBytesAsync(path);
        ArticlePdfExtractor extractor = new(Options.Create(new ArticleSummaryOptions
        {
            MaximumPages = 40,
            MaximumExtractedCharacters = 160000,
            MaximumDownloadBytes = 12 * 1024 * 1024,
            OcrEnabled = false
        }));
        SummarizeArticleRequest extracted = extractor.Extract(bytes, "tr");
        IReadOnlyList<ArticleSourceSpan> spans = extracted.SourceSpans ?? [];
        bool exactSlices = spans.All(span => extracted.Pages.Single(page =>
                page.PageNumber == span.PageNumber).Text[span.StartOffset..span.EndOffset] == span.Text);
        string byteHash = Hash(bytes);
        string pageHash = Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            extracted.Pages, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        bool valid = bytes.Length == source.ExpectedBytes && byteHash == source.ExpectedSha256 &&
            extracted.Pages.Count is > 0 and <= 40 && extracted.Pages.Sum(page => page.Text.Length) <= 160000 &&
            spans.Count > 0 && exactSlices &&
            ArticleSourceCatalog.IsValid(extracted.Pages, spans, extracted.SourceKind);
        return new()
        {
            ["name"] = source.Name,
            ["fileName"] = source.FileName,
            ["sourceUrl"] = source.SourceUrl,
            ["doi"] = source.Doi,
            ["bytes"] = bytes.Length,
            ["sha256"] = byteHash,
            ["pagesInFileExtraction"] = extracted.Pages.Count,
            ["totalSourcePages"] = extracted.TotalSourcePages,
            ["isPartial"] = extracted.IsPartial,
            ["scopeReason"] = extracted.ScopeReason,
            ["textCharacters"] = extracted.Pages.Sum(page => page.Text.Length),
            ["sourceSpans"] = spans.Count,
            ["sourceKind"] = extracted.SourceKind,
            ["extractionVersion"] = extracted.ExtractionVersion,
            ["extractedPagesSha256"] = pageHash,
            ["exactUtf16Slices"] = exactSlices,
            ["valid"] = valid,
            ["scopeNote"] = source.Name == "reproducibility"
                ? "The whole ten-page PDF is recorded; page 1 is a journal summary and page 10 is publisher tooling, not article-body pages."
                : null
        };
    }

    internal static string FindRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "AcademicCollectorDemo.csproj")))
            current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private static JsonObject InspectResolvedConfiguration(string root)
    {
        const string fakeDatabase = "Server=(localdb)\\MSSQLLocalDB;Database=AcademicBroaderServiceAcceptance_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa;Integrated Security=true;TrustServerCertificate=true";
        BroaderReplayAudit audit = new();
        string sourceDirectory = SourceDirectory;
        using WebApplication collector = BroaderAcceptanceHost.CreateCollectorApplication(root,
            fakeDatabase, "http://127.0.0.1:5235", "http://127.0.0.1:5135", "idle", sourceDirectory, audit);
        foreach (IStartupValidator validator in collector.Services.GetServices<IStartupValidator>())
            validator.Validate();
        ArticleSummaryOptions summary = collector.Services.GetRequiredService<IOptions<ArticleSummaryOptions>>().Value;
        ArticleSummaryAutomationOptions automation = collector.Services
            .GetRequiredService<IOptions<ArticleSummaryAutomationOptions>>().Value;
        BulkCollectionOptions bulk = collector.Services.GetRequiredService<IOptions<BulkCollectionOptions>>().Value;
        FacultyAssistantOptions faculty = collector.Services.GetRequiredService<IOptions<FacultyAssistantOptions>>().Value;
        PublicationMetricsOptions metrics = collector.Services.GetRequiredService<IOptions<PublicationMetricsOptions>>().Value;
        bool collectorValid = summary.MaximumPages == 40 && summary.MaximumExtractedCharacters == 160000 &&
            summary.MaximumDownloadBytes == 12 * 1024 * 1024 && !summary.OcrEnabled &&
            automation.Enabled && !automation.WorkerEnabled && automation.MaximumAttempts == 1 &&
            !bulk.WorkerEnabled && bulk.MaximumAttempts == 1 &&
            !faculty.WorkerEnabled && faculty.RequestTimeoutSeconds == 1800 &&
            !metrics.WorkerEnabled && metrics.MaximumAttempts == 1;

        ServiceAcceptanceBudget budget = new(_ => Task.CompletedTask,
            maximumCalls: 96, maximumSpendUsd: 3.00m);
        BroaderDispatchGate dispatchGate = new();
        RetainedProviderCapture capture = new(Path.Combine(Path.GetTempPath(),
            "broader-preflight-unused-capture"));
        using WebApplication analysis = BroaderAcceptanceHost.CreateLiveAnalysisApplication(
            fakeDatabase, "http://127.0.0.1:5135", budget, dispatchGate, capture, "preflight-not-a-secret");
        foreach (IStartupValidator validator in analysis.Services.GetServices<IStartupValidator>())
            validator.Validate();
        AiOptions ai = analysis.Services.GetRequiredService<IOptions<AiOptions>>().Value;
        string verifier = string.IsNullOrWhiteSpace(ai.ArticleVerifierModel)
            ? ai.ArticleModel : ai.ArticleVerifierModel;
        bool analysisValid = ai.ArticleProvider == "Gemini" &&
            ai.ArticleModel == ServiceAcceptanceBudget.RequiredModel &&
            verifier == ServiceAcceptanceBudget.RequiredModel && ai.ArticleMaxOutputTokens == 8192 &&
            ai.ArticleVerifierMaxOutputTokens == 8192 && ai.FacultyAssistantMaxOutputTokens == 16384 &&
            ai.ArticleGenerationThinkingLevel == "high" && ai.ArticleVerifierThinkingLevel == "high" &&
            ai.FacultyAssistantGenerationThinkingLevel == "medium" &&
            ai.FacultyAssistantVerifierThinkingLevel == "medium" && ai.TimeoutSeconds == 180;
        return new()
        {
            ["valid"] = collectorValid && analysisValid,
            ["collector"] = JsonSerializer.SerializeToNode(new
            {
                summary.MaximumPages, summary.MaximumExtractedCharacters, summary.MaximumDownloadBytes,
                summary.OcrEnabled, automation.Enabled,
                summaryWorkerEnabled = automation.WorkerEnabled,
                summaryMaximumAttempts = automation.MaximumAttempts,
                bulkWorkerEnabled = bulk.WorkerEnabled, bulkMaximumAttempts = bulk.MaximumAttempts,
                facultyWorkerEnabled = faculty.WorkerEnabled,
                faculty.RequestTimeoutSeconds, metricsWorkerEnabled = metrics.WorkerEnabled,
                metrics.MaximumAttempts, startupValidatorsInvoked = true
            }),
            ["analysis"] = JsonSerializer.SerializeToNode(new
            {
                ai.ArticleProvider, ai.ArticleModel, effectiveVerifierModel = verifier,
                ai.ArticleMaxOutputTokens, ai.ArticleVerifierMaxOutputTokens,
                ai.FacultyAssistantMaxOutputTokens, ai.ArticleGenerationThinkingLevel,
                ai.ArticleVerifierThinkingLevel, ai.FacultyAssistantGenerationThinkingLevel,
                ai.FacultyAssistantVerifierThinkingLevel, ai.TimeoutSeconds,
                startupValidatorsInvoked = true
            })
        };
    }

    internal static string Hash(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    internal static async Task WriteNewAsync(string path, string value)
    {
        await using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.WriteThrough);
        byte[] bytes = Encoding.UTF8.GetBytes(value + Environment.NewLine);
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
    }

    private static bool PortsAreFree(params int[] ports)
    {
        HashSet<int> active = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()
            .Select(endpoint => endpoint.Port).ToHashSet();
        return ports.All(port => !active.Contains(port));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

internal sealed record SourceDefinition(string Name, string FileName, int ExpectedBytes,
    string ExpectedSha256, string Doi, string Title, int Year, string SourceUrl);
