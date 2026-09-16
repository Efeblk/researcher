using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using ResearcherAnalysisService.Products.Api.Contracts;
using ResearcherAnalysisService.Products.FacultyAssistant;
using ResearcherAnalysisService.Products.HrDossiers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.SqlClient;

namespace ServiceAcceptancePilot;

internal static partial class LiveBroaderServiceAcceptance
{
    private const string ContinuationDatabase =
        "AcademicBroaderServiceAcceptance_a96a075e8eba4e3f975a5aa438404fec";
    private const string V1ManifestSha256 =
        "e6fc27ca4ffba4bdd1d2e75493dc2ac27efc28559e3135256d3fc74bb30075e2";
    private const int BaselineCalls = 14;
    private const decimal BaselineCostUsd = 0.29367675m;

    internal static async Task<int> RunContinuationPreflightAsync(string fileName = "preflight.json")
    {
        runCancellation = default;
        string root = BroaderServiceAcceptancePreflight.FindRoot();
        string directory = Path.Combine(root, "docs", Program.ContinuationRunId);
        string v1Directory = Path.Combine(root, "docs", Program.RunId);
        Directory.CreateDirectory(directory);
        string preflightPath = Path.Combine(directory, fileName);
        if (File.Exists(preflightPath))
            throw new InvalidOperationException("The frozen continuation preflight already exists.");

        string manifestPath = Path.Combine(v1Directory, "acceptance-artifact-manifest.json");
        string manifestHash = Convert.ToHexString(SHA256.HashData(
            await File.ReadAllBytesAsync(manifestPath))).ToLowerInvariant();
        (_, string database) = Connections(ContinuationDatabase);
        BaselineUsage before = await ReadBaselineUsageAsync(database);
        WorkSet works = await ResolveWorksAsync(database);
        BroaderReplayAudit replay = new();
        Dictionary<string, AcademicEvidenceSearchResponse> retrieval =
            await CaptureCaseRetrievalAsync(root, database, Path.Combine(v1Directory, "inputs"), replay, works);

        FacultyAssistantContextResponse context;
        HrEvidenceDossierResponse dossier;
        HttpCapture denied;
        await using (WebApplication idle = await BroaderAcceptanceHost.StartCollectorAsync(root,
            database, CollectorUrl, AnalysisUrl, "idle", Path.Combine(v1Directory, "inputs"), replay))
        using (HttpClient client = Client(authenticated: true))
        {
            context = await PostAsync<FacultyAssistantContextResponse>(client,
                AnalysisUrl + "/api/v1/faculty/context",
                new GetFacultyAssistantContextRequest
                {
                    PersonelId = BroaderAcceptanceHost.PrimarySubjectId,
                    Version = 1
                });
            dossier = await PostAsync<HrEvidenceDossierResponse>(client,
                AnalysisUrl + "/api/v1/hr/dossiers",
                new GetHrEvidenceDossierRequest
                {
                    PersonelId = BroaderAcceptanceHost.PrimarySubjectId,
                    DossierId = 1
                });
            denied = await CaptureAsync(client,
                AnalysisUrl + "/api/v1/knowledge/search",
                new AcademicEvidenceSearchRequest
                {
                    PersonelId = BroaderAcceptanceHost.SecondarySubjectId,
                    Query = "replication",
                    CanonicalWorkIds = [works.ReproducibilityCanonicalWorkId],
                    Take = 10
                });
        }
        BaselineUsage after = await ReadBaselineUsageAsync(database);

        JsonArray coverage = [];
        bool retrievalReady = true;
        foreach (FacultyCase item in Cases)
        {
            AcademicEvidenceSearchResponse search = retrieval[item.Name];
            int[] expected = item.WorkNames.Select(works.Id).Distinct().Order().ToArray();
            int[] found = search.Hits.Select(hit => hit.CanonicalWorkId).Distinct().Order().ToArray();
            bool valid = search.CatalogVersion == ResearcherAnalysisService.Products.Knowledge
                    .AcademicEvidenceSearchService.CatalogVersion &&
                (item.Negative || search.Hits.Count > 0 &&
                search.Hits.All(hit => expected.Contains(hit.CanonicalWorkId)) &&
                (!item.RequiresBothWorks || found.SequenceEqual(expected)));
            retrievalReady &= valid;
            coverage.Add(Node(new { item.Name, expected, found, hits = search.Hits.Count, valid }));
        }
        bool ready = manifestHash == V1ManifestSha256 &&
            before == after && before.Calls == BaselineCalls && before.CostUsd == BaselineCostUsd &&
            works.CanonicalWorkIds.SequenceEqual([1, 2]) && works.AssociationCount == 3 &&
            works.ObservationCount == 3 && works.ReproducibilityOwnerCount == 2 &&
            retrievalReady && context.Version == 1 &&
            JsonSerializer.Serialize(context, JsonOptions).Contains(PrivateCanary, StringComparison.Ordinal) &&
            dossier.DossierId == 1 && dossier.Dossier.Works.Count == 2 &&
            !JsonSerializer.Serialize(dossier, JsonOptions).Contains(PrivateCanary, StringComparison.Ordinal) &&
            denied.Status == HttpStatusCode.NotFound &&
            denied.Body == "{\"Message\":\"The requested resource was not found.\"}" &&
            replay.ProviderServed == 0 && replay.SourceServed == 0;

        JsonObject result = Node(new
        {
            runId = Program.ContinuationRunId,
            generatedAtUtc = DateTimeOffset.UtcNow,
            ready,
            databaseName = ContinuationDatabase,
            sourceRunId = Program.RunId,
            sourceArtifactManifestSha256 = manifestHash,
            baselineUsage = before,
            maximumNewCalls = 66,
            maximumNewSpendUsd = 2.70632325m,
            aggregateMaximumCalls = 96,
            aggregateMaximumSpendUsd = 3.00m,
            model = ServiceAcceptanceBudget.RequiredModel,
            retrievalCatalogVersion = ResearcherAnalysisService.Products.Knowledge
                .AcademicEvidenceSearchService.CatalogVersion,
            matrix = MatrixNode(),
            fixedQueryCoverage = coverage,
            fixedQueryRetrieval = retrieval,
            contextReadback = context,
            dossierReadback = dossier,
            ownerBoundary = new { status = (int)denied.Status, denied.Body },
            providerBoundary = replay.Snapshot(),
            externalNetworkRequests = 0,
            databaseWrites = 0,
            releaseValueSha256 = BroaderServiceAcceptancePreflight.Hash(
                System.Text.Encoding.UTF8.GetBytes(Program.ContinuationLiveReleaseValue))
        });
        await BroaderServiceAcceptancePreflight.WriteNewAsync(preflightPath,
            result.ToJsonString(JsonOptions));
        Console.WriteLine(ready ? "CONTINUATION_PREFLIGHT_READY" : "CONTINUATION_PREFLIGHT_BLOCKED");
        return ready ? 0 : 1;
    }

    internal static async Task<int> RunContinuationAsync(CancellationToken cancellationToken)
    {
        runCancellation = cancellationToken;
        string root = BroaderServiceAcceptancePreflight.FindRoot();
        string directory = Path.Combine(root, "docs", Program.ContinuationRunId);
        string v1Directory = Path.Combine(root, "docs", Program.RunId);
        JsonObject preflight = JsonNode.Parse(await File.ReadAllTextAsync(
            Path.Combine(directory, "preflight-v4.json"), cancellationToken))!.AsObject();
        Require(preflight["ready"]?.GetValue<bool>() == true &&
            preflight["databaseName"]?.GetValue<string>() == ContinuationDatabase &&
            preflight["retrievalCatalogVersion"]?.GetValue<string>() ==
                ResearcherAnalysisService.Products.Knowledge
                    .AcademicEvidenceSearchService.CatalogVersion,
            "The exact v4 continuation preflight is absent or blocked.");
        string manifestHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(
            Path.Combine(v1Directory, "acceptance-artifact-manifest.json"), cancellationToken)))
            .ToLowerInvariant();
        (_, string database) = Connections(ContinuationDatabase);
        BaselineUsage baseline = await ReadBaselineUsageAsync(database);
        Require(manifestHash == V1ManifestSha256 && baseline.Calls == BaselineCalls &&
            baseline.CostUsd == BaselineCostUsd && baseline.Fingerprint ==
                preflight["baselineUsage"]?["fingerprint"]?.GetValue<string>(),
            "The preserved v1 artifacts or database usage baseline changed after preflight.");

        FinalAcceptanceArtifacts artifacts = new(root, Program.ContinuationRunId);
        artifacts.EnsureLiveIsNew();
        ServiceAcceptanceBudget budget = new(artifacts.WriteBudgetAsync,
            maximumCalls: 66, maximumSpendUsd: 2.70632325m);
        await artifacts.WriteBudgetAsync(budget.Snapshot());
        BroaderDispatchGate dispatchGate = new();
        BroaderReplayAudit replay = new();
        RetainedProviderCapture capture = new(Path.Combine(directory, "provider"));
        WorkSet works = await ResolveWorksAsync(database);
        JsonObject result = new()
        {
            ["runId"] = Program.ContinuationRunId,
            ["sourceRunId"] = Program.RunId,
            ["databaseName"] = ContinuationDatabase,
            ["startedAtUtc"] = DateTimeOffset.UtcNow,
            ["sourceArtifactManifestSha256"] = manifestHash,
            ["baselineUsage"] = Node(baseline),
            ["matrix"] = MatrixNode(),
            ["fixedQueryRetrieval"] = preflight["fixedQueryRetrieval"]?.DeepClone(),
            ["providerBoundary"] = "Existing SQL inputs are reused; only Gemini is live."
        };
        List<FacultyExecution> faculty = [];
        JsonArray classifications = [];
        List<string> semanticFailures = [];
        string inputDirectory = Path.Combine(v1Directory, "inputs");

        try
        {
            await PhaseAsync("continuation-baseline-validated", result, artifacts, budget, dispatchGate);
            await using WebApplication analysis = await BroaderAcceptanceHost.StartLiveAnalysisAsync(
                database, AnalysisUrl, budget, dispatchGate, capture);
            foreach (FacultyCase item in Cases)
            {
                FacultyExecution execution = await ExecuteFacultyAsync(root, database, inputDirectory,
                    replay, budget, dispatchGate, item, works, contextVersion: 1);
                faculty.Add(execution);
                result[$"faculty-{item.Name}"] = execution.Json;
                await PhaseAsync($"faculty-{item.Name}-captured", result, artifacts, budget, dispatchGate);

                try
                {
                    ValidateFaculty(item, execution, works);
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
                "Faculty readback, replay, or conflict checks dispatched Gemini.");
            result["sqlAudit"] = await AuditSqlAsync(database, afterReplay, works, faculty,
                BaselineCalls, BaselineCostUsd);
            result["providerReplay"] = Node(replay.Snapshot());
            result["budget"] = Node(afterReplay);
            result["semanticFailures"] = ContinuationFailuresNode(semanticFailures);
            await PhaseAsync("continuation-replay-sql-cost-audit-complete", result,
                artifacts, budget, dispatchGate);

            Require(afterReplay is { DispatchStopped: false, UnknownUsageCalls: 0 } &&
                afterReplay.Calls <= 66 && afterReplay.CommittedSpendUsd <= 2.70632325m &&
                BaselineCalls + afterReplay.Calls <= 96 &&
                BaselineCostUsd + afterReplay.CommittedSpendUsd <= 3.00m,
                "The continuation or aggregate live usage ledger is unhealthy.");
            bool accepted = semanticFailures.Count == 0;
            result["success"] = accepted;
            result["status"] = accepted ? "awaiting_root_audit" : "awaiting_root_audit_nonpass";
            result["completedAtUtc"] = DateTimeOffset.UtcNow;
            result["cleanup"] = "Owned hosts stopped; preserved v1 database and all artifacts retained.";
            await analysis.StopAsync(cancellationToken);
            await artifacts.WriteResultAsync(result);
            await artifacts.WritePhaseAsync((string)result["status"]!, new JsonObject
            {
                ["runId"] = Program.ContinuationRunId,
                ["databaseName"] = ContinuationDatabase,
                ["success"] = accepted,
                ["status"] = (string)result["status"]!
            }, afterReplay);
            await WriteArtifactManifestAsync(directory, Program.ContinuationRunId);
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
            result["cleanup"] = "Owned hosts disposed; preserved v1 database and artifacts retained; implicit rerun refused.";
            await artifacts.WriteFailureAsync(result);
            await artifacts.WritePhaseAsync("failed-database-preserved", new JsonObject
            {
                ["runId"] = Program.ContinuationRunId,
                ["databaseName"] = ContinuationDatabase,
                ["success"] = false,
                ["failureType"] = exception.GetType().FullName,
                ["failureMessage"] = exception.Message
            }, budget.Snapshot());
            await WriteArtifactManifestAsync(directory, Program.ContinuationRunId);
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static bool IsIndependentCaseAcceptanceFailure(FacultyCase item, string message) =>
        message == $"Positive faculty case {item.Name} did not fulfill its explicit request." ||
        message == $"Both-paper faculty case {item.Name} did not cite both owned works." ||
        item.Negative && message is
            "The unsupported BERT/GPU request was incorrectly classified as fulfilled." or
            "The unsupported request invented a BERT layer count or GPU model.";

    private static JsonNode ContinuationFailuresNode(IReadOnlyCollection<string> failures) =>
        JsonSerializer.SerializeToNode(failures, JsonOptions)!;

    private static async Task<BaselineUsage> ReadBaselineUsageAsync(string database)
    {
        await using SqlConnection connection = new(database);
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT AttemptId,StartedAt,CompletedAt,RequestedModel,ReturnedModel,Outcome,HttpStatus,
              PricingVersion,EstimatedUsd
            FROM [analysis].[GeminiUsageAttempts] ORDER BY StartedAt,AttemptId
            """;
        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        JsonArray rows = [];
        decimal cost = 0;
        while (await reader.ReadAsync())
        {
            decimal itemCost = reader.GetDecimal(8);
            cost += itemCost;
            rows.Add(Node(new
            {
                attemptId = reader.GetGuid(0),
                startedAt = reader.GetDateTime(1),
                completedAt = reader.GetDateTime(2),
                requestedModel = reader.GetString(3),
                returnedModel = reader.GetString(4),
                outcome = reader.GetString(5),
                httpStatus = reader.GetInt32(6),
                pricingVersion = reader.GetString(7),
                estimatedUsd = itemCost
            }));
        }
        string fingerprint = BroaderServiceAcceptancePreflight.Hash(
            System.Text.Encoding.UTF8.GetBytes(rows.ToJsonString(JsonOptions)));
        return new(rows.Count, cost, fingerprint);
    }

    private sealed record BaselineUsage(int Calls, decimal CostUsd, string Fingerprint);
}
