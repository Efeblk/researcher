using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using AcademicCollector.Analysis.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using ResearcherAnalysisService.Products.Api.Contracts;
using ResearcherAnalysisService.Products.FacultyAssistant;
using Microsoft.AspNetCore.Builder;
using ResearcherAnalysisService.Analysis;

namespace ServiceAcceptancePilot;

internal static partial class LiveBroaderServiceAcceptance
{
    private const string ContinuationManifestSha256 =
        "b250c027bd3851d6388da178842d53f827d4f7afbc33e1fa96365cd09f9a2d3f";
    private const int CorrectionBaselineCalls = 57;
    private const decimal CorrectionBaselineCostUsd = 0.6004965m;
    private const string CorrectionManifestSha256 =
        "6c8e0d0a8a692a9c641754f7018cc54bbf4cecff038213fbf340a820ee083b1b";
    private const int FinalRelatedBaselineCalls = 69;
    private const decimal FinalRelatedBaselineCostUsd = 0.70719m;
    private static FacultyCase[] CorrectionCases => Cases
        .Where(value => value.Name is "related-works" or "fine-teaching").ToArray();
    private static FacultyCase[] FinalRelatedCases => Cases
        .Where(value => value.Name == "related-works").ToArray();
    private static readonly PreservedReport[] InitiallyPreservedReports =
    [
        new("explore-record", Program.ContinuationRunId, "faculty-evidence-assistant-v8",
            "faculty-request-coverage-v1"),
        new("reproducibility-methods", Program.ContinuationRunId, "faculty-evidence-assistant-v8",
            "faculty-request-coverage-v1"),
        new("reproducibility-issues", Program.ContinuationRunId, "faculty-evidence-assistant-v8",
            "faculty-request-coverage-v1")
    ];
    private static readonly PreservedReport[] FinallyPreservedReports =
    [
        .. InitiallyPreservedReports,
        new("fine-teaching", Program.CorrectionRunId, "faculty-evidence-assistant-v9",
            "faculty-request-coverage-v1")
    ];

    private static CorrectionRunSettings InitialCorrectionSettings => new(
        Program.CorrectionRunId, Program.ContinuationRunId, ContinuationManifestSha256,
        CorrectionBaselineCalls, CorrectionBaselineCostUsd, 22, 2.3995035m, CorrectionCases,
        "faculty-evidence-assistant-v9", "faculty-evidence-assistant-verification-v7",
        "faculty-evidence-assistant-repair-v2", "faculty-request-coverage-v1",
        InitiallyPreservedReports, "correction", Program.CorrectionLiveReleaseValue);

    private static CorrectionRunSettings FinalRelatedSettings => new(
        Program.FinalRelatedRunId, Program.CorrectionRunId, CorrectionManifestSha256,
        FinalRelatedBaselineCalls, FinalRelatedBaselineCostUsd, 11, 2.29281m, FinalRelatedCases,
        "faculty-evidence-assistant-v10", "faculty-evidence-assistant-verification-v7",
        "faculty-evidence-assistant-repair-v2", "faculty-request-coverage-v2",
        FinallyPreservedReports, "final-related", Program.FinalRelatedLiveReleaseValue);

    internal static Task<int> RunCorrectionPreflightAsync() =>
        RunCorrectionPreflightAsync(InitialCorrectionSettings);

    internal static Task<int> RunFinalRelatedPreflightAsync() =>
        RunCorrectionPreflightAsync(FinalRelatedSettings);

    private static async Task<int> RunCorrectionPreflightAsync(CorrectionRunSettings settings)
    {
        runCancellation = default;
        string root = BroaderServiceAcceptancePreflight.FindRoot();
        string directory = Path.Combine(root, "docs", settings.RunId);
        string sourceDirectory = Path.Combine(root, "docs", settings.SourceRunId);
        string continuationDirectory = Path.Combine(root, "docs", Program.ContinuationRunId);
        string v1InputDirectory = Path.Combine(root, "docs", Program.RunId, "inputs");
        Directory.CreateDirectory(directory);
        string preflightPath = Path.Combine(directory, "preflight.json");
        if (File.Exists(preflightPath))
            throw new InvalidOperationException("The frozen correction preflight already exists.");

        string manifestPath = Path.Combine(sourceDirectory, "acceptance-artifact-manifest.json");
        string manifestHash = Convert.ToHexString(SHA256.HashData(
            await File.ReadAllBytesAsync(manifestPath))).ToLowerInvariant();
        JsonObject sourceResult = JsonNode.Parse(await File.ReadAllTextAsync(
            Path.Combine(sourceDirectory, "result.json")))!.AsObject();
        JsonObject v4Preflight = JsonNode.Parse(await File.ReadAllTextAsync(
            Path.Combine(continuationDirectory, "preflight-v4.json")))!.AsObject();
        (_, string database) = Connections(ContinuationDatabase);
        BaselineUsage before = await ReadBaselineUsageAsync(database);
        WorkSet works = await ResolveWorksAsync(database);
        BroaderReplayAudit replay = new();
        Dictionary<string, AcademicEvidenceSearchResponse> retrieval =
            await CaptureCaseRetrievalAsync(root, database, v1InputDirectory, replay, works, settings.Cases);
        JsonObject historicalReadback = await ReadHistoricalAcceptedRunsAsync(root, database,
            v1InputDirectory, replay, settings.PreservedReports);
        BaselineUsage after = await ReadBaselineUsageAsync(database);

        bool retrievalExact = settings.Cases.All(item =>
        {
            AcademicEvidenceSearchResponse current = retrieval[item.Name];
            AcademicEvidenceSearchResponse frozen = JsonSerializer.Deserialize<AcademicEvidenceSearchResponse>(
                v4Preflight["fixedQueryRetrieval"]![item.Name]!.ToJsonString(), JsonOptions)!;
            return current.CatalogVersion == ResearcherAnalysisService.Products.Knowledge
                    .AcademicEvidenceSearchService.CatalogVersion &&
                current.QueryHash == frozen.QueryHash && current.QueryPlanHash == frozen.QueryPlanHash &&
                current.CorpusHash == frozen.CorpusHash && current.InputHash == frozen.InputHash &&
                current.Hits.Select(value => value.EvidenceId)
                    .SequenceEqual(frozen.Hits.Select(value => value.EvidenceId), StringComparer.Ordinal);
        });
        bool protocolsExact = FacultyAssistantPrompt.Version == settings.GenerationVersion &&
            FacultyAssistantVerificationPrompt.Version == settings.VerificationVersion &&
            FacultyAssistantRepairPrompt.Version == settings.RepairVersion &&
            FacultyRequestCoveragePrompt.Version == settings.CoverageVersion;
        bool ready = manifestHash == settings.SourceManifestSha256 && before == after &&
            before.Calls == settings.BaselineCalls && before.CostUsd == settings.BaselineCostUsd &&
            sourceResult["status"]?.GetValue<string>() == "awaiting_root_audit_nonpass" &&
            retrievalExact && protocolsExact && historicalReadback.Count == settings.PreservedReports.Count &&
            replay.ProviderServed == 0 && replay.SourceServed == 0;

        JsonObject result = Node(new
        {
            runId = settings.RunId,
            generatedAtUtc = DateTimeOffset.UtcNow,
            ready,
            databaseName = ContinuationDatabase,
            sourceRunId = settings.SourceRunId,
            sourceArtifactManifestSha256 = manifestHash,
            baselineUsage = before,
            maximumNewCalls = settings.MaximumCalls,
            maximumNewSpendUsd = settings.MaximumSpendUsd,
            aggregateMaximumCalls = 96,
            aggregateMaximumSpendUsd = 3.00m,
            model = ServiceAcceptanceBudget.RequiredModel,
            protocols = new
            {
                generation = FacultyAssistantPrompt.Version,
                verification = FacultyAssistantVerificationPrompt.Version,
                repair = FacultyAssistantRepairPrompt.Version,
                coverage = FacultyRequestCoveragePrompt.Version
            },
            matrix = CorrectionMatrixNode(settings.Cases),
            fixedQueryRetrieval = retrieval,
            retrievalExact,
            historicalReadback,
            providerBoundary = replay.Snapshot(),
            externalNetworkRequests = 0,
            databaseWrites = 0,
            releaseValueSha256 = BroaderServiceAcceptancePreflight.Hash(
                System.Text.Encoding.UTF8.GetBytes(settings.ReleaseValue))
        });
        await BroaderServiceAcceptancePreflight.WriteNewAsync(preflightPath,
            result.ToJsonString(JsonOptions));
        Console.WriteLine(preflightPath);
        Console.WriteLine(ready ? "CORRECTION_PREFLIGHT_READY" : "CORRECTION_PREFLIGHT_BLOCKED");
        return ready ? 0 : 1;
    }

    internal static Task<int> RunCorrectionAsync(CancellationToken cancellationToken) =>
        RunCorrectionAsync(InitialCorrectionSettings, cancellationToken);

    internal static Task<int> RunFinalRelatedAsync(CancellationToken cancellationToken) =>
        RunCorrectionAsync(FinalRelatedSettings, cancellationToken);

    private static async Task<int> RunCorrectionAsync(CorrectionRunSettings settings,
        CancellationToken cancellationToken)
    {
        runCancellation = cancellationToken;
        string root = BroaderServiceAcceptancePreflight.FindRoot();
        string directory = Path.Combine(root, "docs", settings.RunId);
        string sourceDirectory = Path.Combine(root, "docs", settings.SourceRunId);
        string inputDirectory = Path.Combine(root, "docs", Program.RunId, "inputs");
        JsonObject preflight = JsonNode.Parse(await File.ReadAllTextAsync(
            Path.Combine(directory, "preflight.json"), cancellationToken))!.AsObject();
        Require(preflight["ready"]?.GetValue<bool>() == true &&
            preflight["databaseName"]?.GetValue<string>() == ContinuationDatabase &&
            preflight["protocols"]?["generation"]?.GetValue<string>() == FacultyAssistantPrompt.Version &&
            preflight["protocols"]?["verification"]?.GetValue<string>() ==
                FacultyAssistantVerificationPrompt.Version &&
            preflight["protocols"]?["repair"]?.GetValue<string>() == FacultyAssistantRepairPrompt.Version &&
            preflight["protocols"]?["coverage"]?.GetValue<string>() == FacultyRequestCoveragePrompt.Version,
            "The exact correction preflight is absent, blocked, or stale.");
        string manifestHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(
            Path.Combine(sourceDirectory, "acceptance-artifact-manifest.json"), cancellationToken)))
            .ToLowerInvariant();
        (_, string database) = Connections(ContinuationDatabase);
        BaselineUsage baseline = await ReadBaselineUsageAsync(database);
        Require(manifestHash == settings.SourceManifestSha256 && baseline.Calls == settings.BaselineCalls &&
            baseline.CostUsd == settings.BaselineCostUsd && baseline.Fingerprint ==
                preflight["baselineUsage"]?["fingerprint"]?.GetValue<string>(),
            "The preserved continuation artifacts or SQL usage baseline changed after preflight.");

        FinalAcceptanceArtifacts artifacts = new(root, settings.RunId);
        artifacts.EnsureLiveIsNew();
        ServiceAcceptanceBudget budget = new(artifacts.WriteBudgetAsync,
            maximumCalls: settings.MaximumCalls, maximumSpendUsd: settings.MaximumSpendUsd);
        await artifacts.WriteBudgetAsync(budget.Snapshot());
        BroaderDispatchGate dispatchGate = new();
        BroaderReplayAudit replay = new();
        RetainedProviderCapture capture = new(Path.Combine(directory, "provider"));
        WorkSet works = await ResolveWorksAsync(database);
        JsonObject result = new()
        {
            ["runId"] = settings.RunId,
            ["sourceRunId"] = settings.SourceRunId,
            ["databaseName"] = ContinuationDatabase,
            ["startedAtUtc"] = DateTimeOffset.UtcNow,
            ["sourceArtifactManifestSha256"] = manifestHash,
            ["baselineUsage"] = Node(baseline),
            ["matrix"] = CorrectionMatrixNode(settings.Cases),
            ["fixedQueryRetrieval"] = preflight["fixedQueryRetrieval"]?.DeepClone(),
            ["preservedAcceptedReadback"] = preflight["historicalReadback"]?.DeepClone(),
            ["providerBoundary"] = "Existing SQL inputs are reused; only Gemini is live."
        };
        List<FacultyExecution> faculty = [];
        JsonArray classifications = [];
        List<string> semanticFailures = [];

        try
        {
            await PhaseAsync(settings.PhasePrefix + "-baseline-validated", result, artifacts, budget, dispatchGate);
            await using WebApplication analysis = await BroaderAcceptanceHost.StartLiveAnalysisAsync(
                database, AnalysisUrl, budget, dispatchGate, capture);
            foreach (FacultyCase item in settings.Cases)
            {
                FacultyExecution execution = await ExecuteFacultyAsync(root, database, inputDirectory,
                    replay, budget, dispatchGate, item, works, contextVersion: 1);
                faculty.Add(execution);
                result[$"faculty-{item.Name}"] = execution.Json;
                await PhaseAsync($"faculty-{item.Name}-captured", result, artifacts, budget, dispatchGate);

                try
                {
                    ValidateFaculty(item, execution, works);
                    Require(execution.Completed?.Report?.RequestCoverage?.PromptVersion == settings.CoverageVersion,
                        $"Faculty case {item.Name} did not use the released coverage protocol.");
                    Require(!JsonSerializer.Serialize(execution, JsonOptions)
                        .Contains(PrivateCanary, StringComparison.Ordinal),
                        $"Private context leaked into public faculty result {item.Name}.");
                    classifications.Add(Node(new { item.Name, accepted = true }));
                }
                catch (InvalidOperationException exception) when (
                    IsIndependentCaseAcceptanceFailure(item, exception.Message))
                {
                    semanticFailures.Add(item.Name + ": " + exception.Message);
                    classifications.Add(Node(new { item.Name, accepted = false, exception.Message }));
                }
                result["caseClassifications"] = classifications.DeepClone();
                await PhaseAsync($"faculty-{item.Name}-classified", result, artifacts, budget, dispatchGate);
            }

            ServiceAcceptanceBudgetSnapshot beforeReplay = budget.Snapshot();
            result["zeroCallReplays"] = await ReplayFacultyAsync(root, database, inputDirectory,
                replay, faculty);
            ServiceAcceptanceBudgetSnapshot afterReplay = budget.Snapshot();
            Require(afterReplay.Calls == beforeReplay.Calls &&
                afterReplay.CommittedSpendUsd == beforeReplay.CommittedSpendUsd,
                "Correction readback, replay, or conflict checks dispatched Gemini.");
            result["sqlAudit"] = await AuditSqlAsync(database, afterReplay, works, faculty,
                settings.BaselineCalls, settings.BaselineCostUsd);
            result["providerReplay"] = Node(replay.Snapshot());
            result["budget"] = Node(afterReplay);
            result["semanticFailures"] = ContinuationFailuresNode(semanticFailures);
            await PhaseAsync(settings.PhasePrefix + "-replay-sql-cost-audit-complete", result,
                artifacts, budget, dispatchGate);

            Require(afterReplay is { DispatchStopped: false, UnknownUsageCalls: 0 } &&
                afterReplay.Calls <= settings.MaximumCalls &&
                afterReplay.CommittedSpendUsd <= settings.MaximumSpendUsd &&
                settings.BaselineCalls + afterReplay.Calls <= 96 &&
                settings.BaselineCostUsd + afterReplay.CommittedSpendUsd <= 3.00m,
                "The correction or aggregate live usage ledger is unhealthy.");
            bool accepted = semanticFailures.Count == 0;
            result["success"] = accepted;
            result["status"] = accepted ? "awaiting_root_audit" : "awaiting_root_audit_nonpass";
            result["completedAtUtc"] = DateTimeOffset.UtcNow;
            result["cleanup"] = "Owned hosts stopped; database and all prior artifacts retained.";
            await analysis.StopAsync(cancellationToken);
            await artifacts.WriteResultAsync(result);
            await artifacts.WritePhaseAsync((string)result["status"]!, new JsonObject
            {
                ["runId"] = settings.RunId,
                ["databaseName"] = ContinuationDatabase,
                ["success"] = accepted,
                ["status"] = (string)result["status"]!
            }, afterReplay);
            await WriteArtifactManifestAsync(directory, settings.RunId);
            return accepted ? 0 : 1;
        }
        catch (Exception exception)
        {
            result["success"] = false;
            result["status"] = "failed_database_preserved";
            result["failure"] = Node(new
            {
                type = exception.GetType().FullName,
                exception.Message,
                exception.StackTrace
            });
            result["budget"] = Node(budget.Snapshot());
            result["providerReplay"] = Node(replay.Snapshot());
            result["failureSqlSnapshot"] = await FailureSqlAsync(database);
            result["cleanup"] = "Owned hosts disposed; database and all prior artifacts retained; implicit rerun refused.";
            await artifacts.WriteFailureAsync(result);
            await artifacts.WritePhaseAsync("failed-database-preserved", new JsonObject
            {
                ["runId"] = settings.RunId,
                ["databaseName"] = ContinuationDatabase,
                ["success"] = false,
                ["failureType"] = exception.GetType().FullName,
                ["failureMessage"] = exception.Message
            }, budget.Snapshot());
            await WriteArtifactManifestAsync(directory, settings.RunId);
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static void AssertCorrectionShape()
    {
        Require(CorrectionCases.Select(value => value.Name)
                .SequenceEqual(["related-works", "fine-teaching"], StringComparer.Ordinal) &&
            CorrectionCases.Select(value => value.Query).Distinct(StringComparer.Ordinal).Count() == 2,
            "The correction matrix is not the exact two failed cases.");
        Require(CorrectionMatrixNode(CorrectionCases) is JsonArray { Count: 2 } &&
            CorrectionMatrixNode(FinalRelatedCases) is JsonArray { Count: 1 },
            "The correction matrix did not serialize as an array.");
        Require(FacultyAssistantPrompt.Version == "faculty-evidence-assistant-v10" &&
            FacultyAssistantVerificationPrompt.Version == "faculty-evidence-assistant-verification-v7" &&
            FacultyAssistantRepairPrompt.Version == "faculty-evidence-assistant-repair-v2" &&
            FacultyRequestCoveragePrompt.Version == "faculty-request-coverage-v2",
            "The correction prompt protocol changed.");
    }

    private static JsonArray CorrectionMatrixNode(IReadOnlyCollection<FacultyCase> cases) =>
        JsonSerializer.SerializeToNode(cases, JsonOptions)!.AsArray();

    private static async Task<JsonObject> ReadHistoricalAcceptedRunsAsync(string root, string database,
        string inputDirectory, BroaderReplayAudit replay, IReadOnlyCollection<PreservedReport> preservedReports)
    {
        JsonObject readback = [];
        Dictionary<string, JsonObject> sourceResults = [];
        await using WebApplication idle = await BroaderAcceptanceHost.StartCollectorAsync(root,
            database, CollectorUrl, AnalysisUrl, "idle", inputDirectory, replay);
        using HttpClient client = Client(authenticated: true, timeout: TimeSpan.FromMinutes(2));
        foreach (PreservedReport preserved in preservedReports)
        {
            if (!sourceResults.TryGetValue(preserved.SourceRunId, out JsonObject? sourceResult))
            {
                sourceResult = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(root, "docs",
                    preserved.SourceRunId, "result.json")))!.AsObject();
                sourceResults[preserved.SourceRunId] = sourceResult;
            }
            string name = preserved.Name;
            Guid runId = sourceResult[$"faculty-{name}"]?["completed"]?["runId"]?
                .GetValue<Guid>() ?? throw new InvalidOperationException($"Saved run ID is absent for {name}.");
            FacultyAssistantRunResponse saved = await PostAsync<FacultyAssistantRunResponse>(client,
                AnalysisUrl + "/api/v1/products/GetFacultyAssistantRun",
                new GetFacultyAssistantRunRequest
                {
                    PersonelId = BroaderAcceptanceHost.PrimarySubjectId,
                    RunId = runId
                });
            FacultyAssistantAnalysisReport report = saved.Report ??
                throw new InvalidOperationException($"Previously accepted run {name} saved no report.");
            FacultyAssistantAnalysisReport frozen = JsonSerializer.Deserialize<FacultyAssistantAnalysisReport>(
                sourceResult[$"faculty-{name}"]!["completed"]!["report"]!.ToJsonString(), JsonOptions) ??
                throw new InvalidOperationException($"Frozen accepted report is absent for {name}.");
            bool reportExact = Equivalent(report, frozen);
            Require(saved.Status == "Completed" && report.PromptVersion == preserved.PromptVersion &&
                    report.RequestCoverage?.Status == "fulfilled" &&
                    report.RequestCoverage.PromptVersion == preserved.CoverageVersion && reportExact,
                $"Previously accepted run {name} is no longer readable without reinterpretation.");
            readback[name] = Node(new
            {
                saved.RunId,
                saved.Status,
                report.PromptVersion,
                verificationPromptVersion = report.Verification.PromptVersion,
                repairPromptVersion = report.Repair?.PromptVersion,
                report.Outcome,
                requestCoverage = report.RequestCoverage!.Status,
                itemCount = report.Items.Count,
                reportExact
            });
        }
        return readback;
    }

    private sealed record PreservedReport(string Name, string SourceRunId,
        string PromptVersion, string CoverageVersion);

    private sealed record CorrectionRunSettings(string RunId, string SourceRunId,
        string SourceManifestSha256, int BaselineCalls, decimal BaselineCostUsd,
        int MaximumCalls, decimal MaximumSpendUsd, IReadOnlyCollection<FacultyCase> Cases,
        string GenerationVersion, string VerificationVersion, string RepairVersion,
        string CoverageVersion, IReadOnlyCollection<PreservedReport> PreservedReports,
        string PhasePrefix, string ReleaseValue);
}
