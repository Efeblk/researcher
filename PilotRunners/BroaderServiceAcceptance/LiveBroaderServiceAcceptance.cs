using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AcademicCollector.Analysis.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.FacultyAssistant;
using AcademicCollectorDemo.Modules.AcademicPerformance.HrDossiers;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Integrations.Gemini;

namespace ServiceAcceptancePilot;

internal static partial class LiveBroaderServiceAcceptance
{
    private const string CollectorUrl = "http://127.0.0.1:5235";
    private const string AnalysisUrl = "http://127.0.0.1:5135";
    private const string DatabasePrefix = "AcademicBroaderServiceAcceptance_";
    private const string PrivateCanary = "PRIVATE-BROADER-CANARY-c7d91a38";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        { WriteIndented = true };
    private static CancellationToken runCancellation;

    private static readonly FacultyCase[] Cases =
    [
        new("explore-record", "ExploreOwnRecord", "tr",
            "Çalışmalarımın ortak temalarını ve aralarındaki temel yöntem farkını kaynaklarıyla özetle.",
            ["fine", "reproducibility"], true, false),
        new("related-works", "RelatedWorks", "tr",
            "Bu iki yayın arasında, yalnızca mevcut kayıtların desteklediği yöntemsel ilişkileri göster; önerilerin kapsamını belirt.",
            ["fine", "reproducibility"], true, false),
        new("fine-teaching", "TeachingHelp", "en",
            "Create a classroom exercise based on this study, with an explicit learner task, worked answer guidance, and a separate discussion question.",
            ["fine"], false, false),
        new("reproducibility-methods", "OwnPaperMethods", "en",
            "Explain how studies were selected, how replication success was measured, and one limitation on generalizing the findings.",
            ["reproducibility"], false, false),
        new("reproducibility-issues", "OwnPaperIssues", "tr",
            "Bulguların yorumlanmasında yeniden kontrol edilmesi gereken noktaları kaynaklarıyla göster; belirsizlik ile kanıtlanmış hatayı ayır.",
            ["reproducibility"], false, false),
        new("unsupported-bert-gpu", "ExploreOwnRecord", "tr",
            "Bu makalede kullanılan BERT modelinin katman sayısını ve eğitimde kullanılan GPU modelini kaynaklarıyla yaz.",
            ["fine"], false, true)
    ];

    public static async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        runCancellation = cancellationToken;
        string root = BroaderServiceAcceptancePreflight.FindRoot();
        string artifactDirectory = Path.Combine(root, "docs", Program.RunId);
        RequirePreflight(artifactDirectory);
        FinalAcceptanceArtifacts artifacts = new(root);
        artifacts.EnsureLiveIsNew();
        string inputDirectory = await FreezeInputsAsync(artifactDirectory);
        RetainedProviderCapture capture = new(Path.Combine(artifactDirectory, "provider"));
        ServiceAcceptanceBudget budget = new(artifacts.WriteBudgetAsync,
            maximumCalls: 96, maximumSpendUsd: 3.00m);
        BroaderDispatchGate dispatchGate = new();
        BroaderReplayAudit replay = new();
        string databaseName = NewDatabaseName();
        (string master, string database) = Connections(databaseName);
        JsonObject result = new()
        {
            ["runId"] = Program.RunId,
            ["databaseName"] = databaseName,
            ["startedAtUtc"] = DateTimeOffset.UtcNow,
            ["preservedDatabaseExcluded"] = BroaderServiceAcceptancePreflight.PreservedDatabase,
            ["providerBoundary"] = "Hash-pinned ORCID metadata and PDF transport are local fixtures; Gemini is live.",
            ["matrix"] = MatrixNode(),
            ["priorKnownLocalCostUsd"] = 3.16896075m,
            ["priorKnownLocalCalls"] = 178
        };

        try
        {
            await CreateDatabaseAsync(master, databaseName);
            await PhaseAsync("database-created", result, artifacts, budget, dispatchGate);
            await using WebApplication analysis = await BroaderAcceptanceHost.StartLiveAnalysisAsync(
                database, AnalysisUrl, budget, dispatchGate, capture);

            Guid batchId = Guid.NewGuid();
            BulkCollectionStatusResponse submitted;
            await using (WebApplication idle = await BroaderAcceptanceHost.StartCollectorAsync(root,
                database, CollectorUrl, AnalysisUrl, "idle", inputDirectory, replay))
            using (HttpClient client = Client())
            {
                submitted = await PostAsync<BulkCollectionStatusResponse>(client,
                    "/Services/AcademicPerformance/V1/Bulk/Submit", new BulkCollectionSubmitRequest
                    {
                        BatchId = batchId,
                        Researchers =
                        [
                            new() { PersonelId = BroaderAcceptanceHost.PrimarySubjectId,
                                Orcid = BroaderAcceptanceHost.PrimaryOrcid },
                            new() { PersonelId = BroaderAcceptanceHost.SecondarySubjectId,
                                Orcid = BroaderAcceptanceHost.SecondaryOrcid }
                        ]
                    });
            }
            Require(!submitted.WorkerEnabled && submitted.Jobs.Count == 2 &&
                submitted.Jobs.All(value => value.Status == "Pending"),
                "Worker-off bulk submission was not durably pending for both researchers.");
            result["bulkSubmitted"] = Node(submitted);
            await PhaseAsync("bulk-submitted-worker-off", result, artifacts, budget, dispatchGate);

            BulkCollectionStatusResponse collected;
            await using (WebApplication worker = await BroaderAcceptanceHost.StartCollectorAsync(root,
                database, CollectorUrl, AnalysisUrl, "bulk", inputDirectory, replay))
            using (HttpClient client = Client())
                collected = await PollBulkAsync(client, batchId);
            Require(collected.IsComplete && collected.Jobs.Count == 2 &&
                collected.Jobs.All(value => value.Status == "Partial" && value.Attempts == 1) &&
                replay.ProviderServed == 4,
                "Bounded two-researcher ORCID collection did not finish as expected.");
            WorkSet works = await ResolveWorksAsync(database);
            result["bulkCollected"] = Node(collected);
            result["works"] = Node(works);
            await PhaseAsync("collection-canonicalization-captured", result, artifacts, budget, dispatchGate);
            Require(works.CanonicalWorkIds.Length == 2 && works.AssociationCount == 3 &&
                works.ObservationCount == 3 && works.ReproducibilityOwnerCount == 2,
                "Shared-DOI canonicalization did not produce two works and three owned associations.");
            await PhaseAsync("collection-canonicalization-classified", result, artifacts, budget, dispatchGate);

            await using (WebApplication summaryWorker = await BroaderAcceptanceHost.StartCollectorAsync(root,
                database, CollectorUrl, AnalysisUrl, "summary", inputDirectory, replay))
                await PollSummariesAsync(database, works.CanonicalWorkIds);
            Require(budget.Snapshot().Calls <= 30,
                "Automatic summaries consumed the 66-call faculty headroom; faculty dispatch was refused.");

            SavedArticleSummaryResponse fineSummary;
            SavedArticleSummaryResponse reproducibilitySummary;
            await using (WebApplication idle = await BroaderAcceptanceHost.StartCollectorAsync(root,
                database, CollectorUrl, AnalysisUrl, "idle", inputDirectory, replay))
            using (HttpClient client = Client())
            {
                fineSummary = await PostAsync<SavedArticleSummaryResponse>(client,
                    "/Services/AcademicPerformance/V1/GetArticleSummary",
                    new ArticleSummaryRequest { PersonelID = BroaderAcceptanceHost.PrimarySubjectId,
                        AcademicWorkId = works.FineAcademicWorkId, Language = "tr" });
                reproducibilitySummary = await PostAsync<SavedArticleSummaryResponse>(client,
                    "/Services/AcademicPerformance/V1/GetArticleSummary",
                    new ArticleSummaryRequest { PersonelID = BroaderAcceptanceHost.PrimarySubjectId,
                        AcademicWorkId = works.ReproducibilityAcademicWorkId, Language = "tr" });
            }
            result["summaries"] = Node(new { fine = fineSummary, reproducibility = reproducibilitySummary });
            await PhaseAsync("summaries-captured", result, artifacts, budget, dispatchGate);
            await AuditSummarySourcesAsync(database, works);
            Require(replay.SourceServed == 2 && fineSummary.Report.Language == "tr" &&
                reproducibilitySummary.Report.Language == "tr",
                "The two fresh Turkish PDF summaries did not retain their expected sources.");
            await PhaseAsync("summaries-classified", result, artifacts, budget, dispatchGate);

            ResearcherPublicationMetricsStatusResponse primaryMetrics;
            ResearcherPublicationMetricsStatusResponse secondaryMetrics;
            await using (WebApplication metricsWorker = await BroaderAcceptanceHost.StartCollectorAsync(root,
                database, CollectorUrl, AnalysisUrl, "metrics", inputDirectory, replay))
            using (HttpClient client = Client())
            {
                _ = await PostAsync<ResearcherPublicationMetricsStatusResponse>(client,
                    "/Services/AcademicPerformance/V1/RefreshResearcherPublicationMetrics",
                    new ResearcherPublicationMetricsRequest { PersonelId = BroaderAcceptanceHost.PrimarySubjectId },
                    HttpStatusCode.Accepted);
                _ = await PostAsync<ResearcherPublicationMetricsStatusResponse>(client,
                    "/Services/AcademicPerformance/V1/RefreshResearcherPublicationMetrics",
                    new ResearcherPublicationMetricsRequest { PersonelId = BroaderAcceptanceHost.SecondarySubjectId },
                    HttpStatusCode.Accepted);
                primaryMetrics = await PollMetricsAsync(client, BroaderAcceptanceHost.PrimarySubjectId);
                secondaryMetrics = await PollMetricsAsync(client, BroaderAcceptanceHost.SecondarySubjectId);
            }
            Require(primaryMetrics is { Status: "Current", IsStale: false, Data.CanonicalWorkCount: 2 } &&
                secondaryMetrics is { Status: "Current", IsStale: false, Data.CanonicalWorkCount: 1 },
                "Publication metrics did not preserve the two-owner canonical counts.");
            result["metrics"] = Node(new { primary = primaryMetrics, secondary = secondaryMetrics });

            FacultyAssistantContextResponse context;
            HrEvidenceDossierResponse dossier;
            await using (WebApplication idle = await BroaderAcceptanceHost.StartCollectorAsync(root,
                database, CollectorUrl, AnalysisUrl, "idle", inputDirectory, replay))
            using (HttpClient client = Client(authenticated: true))
            {
                context = await PostAsync<FacultyAssistantContextResponse>(client,
                    "/Services/AcademicPerformance/V1/SaveFacultyAssistantContext",
                    new SaveFacultyAssistantContextRequest
                    {
                        PersonelId = BroaderAcceptanceHost.PrimarySubjectId,
                        ExpectedVersion = 0,
                        Context = new()
                        {
                            Language = "tr",
                            ResearchGoals = ["Davranışsal deneyler ve yeniden üretilebilirlik"],
                            Courses = ["Araştırma yöntemleri"],
                            TeachingAudience = "Lisansüstü",
                            Preferences = PrivateCanary
                        }
                    });
                dossier = await PostAsync<HrEvidenceDossierResponse>(client,
                    "/Services/AcademicPerformance/V1/CreateHrEvidenceDossier",
                    new CreateHrEvidenceDossierRequest
                    {
                        PersonelId = BroaderAcceptanceHost.PrimarySubjectId,
                        PublicationMetricSnapshotId = primaryMetrics.SnapshotId,
                        CanonicalWorkIds = works.CanonicalWorkIds.ToList(),
                        Language = "tr"
                    });
                HttpCapture denied = await CaptureAsync(client,
                    "/Services/AcademicPerformance/V1/SearchAcademicEvidence",
                    new AcademicEvidenceSearchRequest
                    {
                        PersonelId = BroaderAcceptanceHost.SecondarySubjectId,
                        Query = "replication", CanonicalWorkIds = [works.ReproducibilityCanonicalWorkId]
                    });
                Require(denied.Status == HttpStatusCode.NotFound &&
                    denied.Body == "{\"Message\":\"The requested resource was not found.\"}",
                    "The primary acceptance actor crossed the secondary owner boundary.");
            }
            Require(context.Version == 1 && dossier.Dossier.Works.Count == 2 &&
                dossier.Dossier.PublicationMetrics is { IsStale: false } &&
                !JsonSerializer.Serialize(dossier, JsonOptions).Contains(PrivateCanary, StringComparison.Ordinal),
                "The private context or current HR dossier boundary was not preserved.");
            result["context"] = Node(context);
            result["dossier"] = Node(dossier);
            await PhaseAsync("metrics-context-hr-isolation-complete", result, artifacts, budget, dispatchGate);

            Dictionary<string, AcademicEvidenceSearchResponse> retrieval =
                await CaptureCaseRetrievalAsync(root, database, inputDirectory, replay, works);
            result["caseQueryRetrieval"] = Node(retrieval);
            await PhaseAsync("fixed-query-retrieval-captured", result, artifacts, budget, dispatchGate);
            foreach (FacultyCase item in Cases.Where(value => !value.Negative))
            {
                AcademicEvidenceSearchResponse search = retrieval[item.Name];
                Require(search.Hits.Count > 0 && search.Hits.All(hit =>
                        item.WorkNames.Select(works.Id).Contains(hit.CanonicalWorkId)) &&
                    (!item.RequiresBothWorks || search.Hits.Select(hit => hit.CanonicalWorkId)
                        .Distinct().Order().SequenceEqual(works.CanonicalWorkIds.Order())),
                    $"Fixed query {item.Name} did not retrieve its required owned source coverage.");
            }
            await PhaseAsync("fixed-query-retrieval-classified", result, artifacts, budget, dispatchGate);

            List<FacultyExecution> faculty = [];
            foreach (FacultyCase item in Cases)
            {
                FacultyExecution execution = await ExecuteFacultyAsync(root, database, inputDirectory,
                    replay, budget, dispatchGate, item, works, context.Version);
                faculty.Add(execution);
                result[$"faculty-{item.Name}"] = execution.Json;
                await PhaseAsync($"faculty-{item.Name}-captured", result, artifacts, budget, dispatchGate);
                ValidateFaculty(item, execution, works);
                Require(!JsonSerializer.Serialize(execution, JsonOptions)
                    .Contains(PrivateCanary, StringComparison.Ordinal),
                    $"Private context leaked into public faculty result {item.Name}.");
                await PhaseAsync($"faculty-{item.Name}-classified", result, artifacts, budget, dispatchGate);
            }

            ServiceAcceptanceBudgetSnapshot beforeReplay = budget.Snapshot();
            JsonArray replayResults = await ReplayFacultyAsync(root, database, inputDirectory,
                replay, faculty);
            ServiceAcceptanceBudgetSnapshot afterReplay = budget.Snapshot();
            Require(afterReplay.Calls == beforeReplay.Calls &&
                afterReplay.CommittedSpendUsd == beforeReplay.CommittedSpendUsd,
                "Faculty readback, replay, or conflict checks dispatched Gemini.");
            result["zeroCallReplays"] = replayResults;
            result["sqlAudit"] = await AuditSqlAsync(database, budget.Snapshot(), works, faculty);
            result["providerReplay"] = Node(replay.Snapshot());
            result["budget"] = Node(budget.Snapshot());
            await PhaseAsync("replay-sql-cost-audit-complete", result, artifacts, budget, dispatchGate);

            ServiceAcceptanceBudgetSnapshot finalBudget = budget.Snapshot();
            Require(finalBudget is { DispatchStopped: false, UnknownUsageCalls: 0 } &&
                finalBudget.Calls <= 96 && finalBudget.CommittedSpendUsd <= 3.00m,
                "The final live usage ledger is unhealthy.");
            result["success"] = true;
            result["status"] = "awaiting_root_audit";
            result["completedAtUtc"] = DateTimeOffset.UtcNow;
            result["cleanup"] = "Owned hosts stopped; exact owned database preserved pending root audit.";
            await analysis.StopAsync();
            await artifacts.WriteResultAsync(result);
            await artifacts.WritePhaseAsync("awaiting-root-audit", new JsonObject
            {
                ["runId"] = Program.RunId,
                ["databaseName"] = databaseName,
                ["success"] = true,
                ["status"] = "awaiting_root_audit"
            }, finalBudget);
            await WriteArtifactManifestAsync(artifactDirectory);
            return 0;
        }
        catch (Exception exception)
        {
            result["success"] = false;
            result["status"] = "failed_database_preserved";
            result["failure"] = Node(new { type = exception.GetType().FullName,
                exception.Message, exception.StackTrace });
            result["budget"] = Node(budget.Snapshot());
            result["providerReplay"] = Node(replay.Snapshot());
            result["failureSqlSnapshot"] = await FailureSqlAsync(database);
            result["cleanup"] = "Owned hosts disposed; database and artifacts preserved; implicit rerun refused.";
            await artifacts.WriteFailureAsync(result);
            await artifacts.WritePhaseAsync("failed-database-preserved", new JsonObject
            {
                ["runId"] = Program.RunId,
                ["databaseName"] = databaseName,
                ["success"] = false,
                ["failureType"] = exception.GetType().FullName,
                ["failureMessage"] = exception.Message
            }, budget.Snapshot());
            await WriteArtifactManifestAsync(artifactDirectory);
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static async Task<int> CleanupAsync()
    {
        string root = BroaderServiceAcceptancePreflight.FindRoot();
        string directory = Path.Combine(root, "docs", Program.RunId);
        JsonObject state = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "state.json")))!.AsObject();
        JsonObject result = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "result.json")))!.AsObject();
        string name = state["databaseName"]?.GetValue<string>() ?? string.Empty;
        ValidateDatabaseName(name);
        if (state["runId"]?.GetValue<string>() != Program.RunId ||
            state["phase"]?.GetValue<string>() != "awaiting-root-audit" ||
            result["success"]?.GetValue<bool>() != true ||
            result["status"]?.GetValue<string>() != "awaiting_root_audit")
            throw new InvalidOperationException("The recorded lifecycle is not eligible for cleanup.");
        (string master, _) = Connections(name);
        await DropDatabaseAsync(master, name);
        result["status"] = "complete";
        result["cleanup"] = "Root-released cleanup dropped only the exact owned broader-acceptance database.";
        await FinalAcceptanceArtifacts.WriteAtomicAsync(Path.Combine(directory, "result.json"),
            result.ToJsonString(JsonOptions));
        return 0;
    }

    private static async Task<Dictionary<string, AcademicEvidenceSearchResponse>> CaptureCaseRetrievalAsync(
        string root, string database, string inputDirectory, BroaderReplayAudit replay, WorkSet works,
        IReadOnlyCollection<FacultyCase>? selectedCases = null)
    {
        Dictionary<string, AcademicEvidenceSearchResponse> results = new(StringComparer.Ordinal);
        await using WebApplication idle = await BroaderAcceptanceHost.StartCollectorAsync(root,
            database, CollectorUrl, AnalysisUrl, "idle", inputDirectory, replay);
        using HttpClient client = Client(authenticated: true);
        foreach (FacultyCase item in selectedCases ?? Cases)
        {
            results[item.Name] = await PostAsync<AcademicEvidenceSearchResponse>(client,
                "/Services/AcademicPerformance/V1/SearchAcademicEvidence",
                new AcademicEvidenceSearchRequest
                {
                    PersonelId = BroaderAcceptanceHost.PrimarySubjectId,
                    Query = item.Query,
                    CanonicalWorkIds = item.WorkNames.Select(works.Id).ToList(),
                    Take = 10
                });
        }
        results["diagnostic-fine-anchor"] = await PostAsync<AcademicEvidenceSearchResponse>(client,
            "/Services/AcademicPerformance/V1/SearchAcademicEvidence", new AcademicEvidenceSearchRequest
            {
                PersonelId = BroaderAcceptanceHost.PrimarySubjectId,
                Query = "randomly treatment control observed weeks fine delays",
                CanonicalWorkIds = [works.FineCanonicalWorkId], Take = 10
            });
        results["diagnostic-reproducibility-anchor"] = await PostAsync<AcademicEvidenceSearchResponse>(client,
            "/Services/AcademicPerformance/V1/SearchAcademicEvidence", new AcademicEvidenceSearchRequest
            {
                PersonelId = BroaderAcceptanceHost.PrimarySubjectId,
                Query = "quasi-random journals replication significance confidence interval",
                CanonicalWorkIds = [works.ReproducibilityCanonicalWorkId], Take = 10
            });
        Require(results["diagnostic-fine-anchor"].Hits.Count > 0 &&
            results["diagnostic-reproducibility-anchor"].Hits.Count > 0,
            "A declared source-anchor diagnostic found no saved exact evidence.");
        return results;
    }

    private static async Task<FacultyExecution> ExecuteFacultyAsync(string root, string database,
        string inputDirectory, BroaderReplayAudit replay, ServiceAcceptanceBudget budget,
        BroaderDispatchGate dispatchGate, FacultyCase item, WorkSet works, int contextVersion)
    {
        StartFacultyAssistantRequest request = new()
        {
            PersonelId = BroaderAcceptanceHost.PrimarySubjectId,
            ClientRequestId = Guid.NewGuid(),
            Mode = item.Mode,
            Language = item.Language,
            Query = item.Query,
            CanonicalWorkIds = item.WorkNames.Select(works.Id).ToList(),
            Take = 10,
            ContextVersion = contextVersion
        };
        int before = budget.Snapshot().Calls;
        FacultyAssistantRunResponse? queued = null;
        HttpCapture start;
        await using (WebApplication idle = await BroaderAcceptanceHost.StartCollectorAsync(root,
            database, CollectorUrl, AnalysisUrl, "idle", inputDirectory, replay))
        using (HttpClient client = Client(authenticated: true))
        {
            start = await CaptureAsync(client,
                "/Services/AcademicPerformance/V1/StartFacultyAssistant", request);
            if (start.Status == HttpStatusCode.Accepted)
                queued = JsonSerializer.Deserialize<FacultyAssistantRunResponse>(start.Body, JsonOptions)!;
        }
        if (queued is null)
        {
            Require(item.Negative && start.Status == HttpStatusCode.UnprocessableEntity &&
                budget.Snapshot().Calls == before,
                $"Faculty case {item.Name} was refused unexpectedly.");
            return new(item, request, start, null, 0,
                Node(new { request, startStatus = (int)start.Status, start.Body, calls = 0 }));
        }

        dispatchGate.SetPhase("faculty-" + item.Name, budget);
        FacultyAssistantRunResponse completed;
        await using (WebApplication worker = await BroaderAcceptanceHost.StartCollectorAsync(root,
            database, CollectorUrl, AnalysisUrl, "faculty", inputDirectory, replay))
        using (HttpClient client = Client(authenticated: true))
            completed = await PollFacultyAsync(client, queued.RunId);
        int calls = budget.Snapshot().Calls - before;
        Require(calls is >= 1 and <= 11, $"Faculty case {item.Name} escaped its one-to-eleven-call envelope.");
        return new(item, request, start, completed, calls,
            Node(new { request, queued, completed, calls }));
    }

    private static void ValidateFaculty(FacultyCase item, FacultyExecution execution, WorkSet works)
    {
        if (execution.Completed is null) return;
        FacultyAssistantRunResponse value = execution.Completed;
        FacultyAssistantAnalysisReport report = value.Report ??
            throw new InvalidOperationException($"Faculty case {item.Name} saved no report.");
        Require(value.Status == "Completed" && report.Mode == item.Mode && report.Language == item.Language &&
            report.Model == ServiceAcceptanceBudget.RequiredModel &&
            report.PromptVersion == FacultyAssistantPrompt.Version &&
            report.Generation is not null && report.SourceChecks is not null && report.Repair is not null,
            $"Faculty case {item.Name} did not return the exact released report envelope.");
        if (!item.Negative)
        {
            Require(report.Items.Count > 0 && report.RequestCoverage?.Status == "fulfilled" &&
                report.Outcome is "completed" or "partial",
                $"Positive faculty case {item.Name} did not fulfill its explicit request.");
            if (item.RequiresBothWorks)
            {
                Dictionary<string, int> owners = value.Retrieval.Evidence.ToDictionary(
                    evidence => evidence.EvidenceId, evidence => evidence.CanonicalWorkId);
                int[] citedWorks = report.Items.SelectMany(answer => answer.Citations)
                    .Select(citation => owners[citation.EvidenceId]).Distinct().Order().ToArray();
                Require(citedWorks.SequenceEqual(works.CanonicalWorkIds.Order()),
                    $"Both-paper faculty case {item.Name} did not cite both owned works.");
            }
        }
        else
        {
            Require(report.RequestCoverage?.Status is "partial" or "unanswered" ||
                report.Outcome == "no_supported_items",
                "The unsupported BERT/GPU request was incorrectly classified as fulfilled.");
            string publicText = string.Join('\n', report.Items.Select(value =>
                value.Basis + "\n" + value.Response));
            Require(!Regex.IsMatch(publicText,
                    @"\b\d+\s*(layer|layers|katman|katmanlı)\b|\b(A100|H100|V100|T4|RTX|Tesla|NVIDIA)\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                "The unsupported request invented a BERT layer count or GPU model.");
        }
    }

    private static async Task<JsonArray> ReplayFacultyAsync(string root, string database,
        string inputDirectory, BroaderReplayAudit replay, IReadOnlyList<FacultyExecution> executions)
    {
        JsonArray results = [];
        await using WebApplication idle = await BroaderAcceptanceHost.StartCollectorAsync(root,
            database, CollectorUrl, AnalysisUrl, "idle", inputDirectory, replay);
        using HttpClient client = Client(authenticated: true, timeout: TimeSpan.FromMinutes(2));
        foreach (FacultyExecution execution in executions)
        {
            if (execution.Completed is null)
            {
                HttpCapture repeated = await CaptureAsync(client,
                    "/Services/AcademicPerformance/V1/StartFacultyAssistant", execution.Request);
                Require(repeated.Status == HttpStatusCode.UnprocessableEntity,
                    "A deterministic no-evidence replay changed status.");
                results.Add(Node(new { execution.Case.Name, repeatedStatus = (int)repeated.Status }));
                continue;
            }
            FacultyAssistantRunResponse replayed = await PostAsync<FacultyAssistantRunResponse>(client,
                "/Services/AcademicPerformance/V1/StartFacultyAssistant", execution.Request,
                HttpStatusCode.Accepted);
            FacultyAssistantRunResponse read = await PostAsync<FacultyAssistantRunResponse>(client,
                "/Services/AcademicPerformance/V1/GetFacultyAssistantRun",
                new GetFacultyAssistantRunRequest
                {
                    PersonelId = BroaderAcceptanceHost.PrimarySubjectId,
                    RunId = execution.Completed.RunId
                });
            StartFacultyAssistantRequest changed = Clone(execution.Request);
            changed.Query += " değişti";
            HttpCapture conflict = await CaptureAsync(client,
                "/Services/AcademicPerformance/V1/StartFacultyAssistant", changed);
            Require(replayed.Reused && Equivalent(replayed.Report, execution.Completed.Report) &&
                Equivalent(read, execution.Completed) && conflict.Status == HttpStatusCode.Conflict,
                $"Faculty replay contract failed for {execution.Case.Name}.");
            results.Add(Node(new { execution.Case.Name, replayed, read, changedStatus = 409 }));
        }
        return results;
    }

    private static async Task<JsonObject> AuditSqlAsync(string database,
        ServiceAcceptanceBudgetSnapshot budget, WorkSet works, IReadOnlyList<FacultyExecution> executions,
        int baselineCalls = 0, decimal baselineCostUsd = 0m)
    {
        await using AcademicDbContext db = Open(database);
        List<CanonicalArticleAnalysisRun> runs = await db.CanonicalArticleAnalysisRuns.AsNoTracking()
            .Include(value => value.ArticleSourceSnapshot).ThenInclude(value => value!.Pages)
            .Include(value => value.ArticleSourceSnapshot).ThenInclude(value => value!.Spans)
            .Where(value => works.CanonicalWorkIds.Contains(value.CanonicalWorkId)).ToListAsync();
        foreach (FacultyExecution execution in executions.Where(value => value.Completed is not null))
        {
            FacultyAssistantRunResponse completed = execution.Completed!;
            FacultyAssistantRun row = await db.FacultyAssistantRuns.AsNoTracking()
                .SingleAsync(value => value.RunId == completed.RunId);
            Require(row.ReportJson is not null && Equivalent(
                    JsonSerializer.Deserialize<FacultyAssistantAnalysisReport>(row.ReportJson, JsonOptions),
                    completed.Report),
                $"Faculty SQL/API parity failed for {execution.Case.Name}.");
            FacultyAssistantAnalysisRequest input = JsonSerializer.Deserialize<FacultyAssistantAnalysisRequest>(
                row.AuthorizedInputJson!, JsonOptions)!;
            Dictionary<string, FacultyAssistantEvidence> authorized = input.Evidence.ToDictionary(value => value.EvidenceId);
            foreach (FacultyAssistantAnswerItem item in completed.Report!.Items)
            foreach (FacultyAssistantCitation citation in item.Citations)
            {
                FacultyAssistantEvidence evidence = authorized[citation.EvidenceId];
                ArticleSourceSpanSnapshot span = await db.ArticleSourceSpans.AsNoTracking()
                    .SingleAsync(value => value.Id == evidence.SourceSpanId);
                ArticleSourcePageSnapshot page = await db.ArticleSourcePages.AsNoTracking()
                    .SingleAsync(value => value.ArticleSourceSnapshotId == span.ArticleSourceSnapshotId &&
                        value.PageNumber == span.PageNumber);
                Require(citation.ExactQuote == evidence.ExactText && span.Text == evidence.ExactText &&
                    page.Text[span.StartOffset..span.EndOffset] == span.Text,
                    $"Faculty citation {citation.EvidenceId} escaped its exact SQL source slice.");
            }
        }

        await using SqlConnection connection = new(database);
        await connection.OpenAsync();
        JsonArray usage = [];
        decimal totalCost = 0;
        decimal observedBaselineCost = 0;
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT AttemptId,StartedAt,CompletedAt,RequestedModel,ReturnedModel,Outcome,HttpStatus,
              PromptTokenCount,CachedTokenCount,CandidateTokenCount,ThoughtTokenCount,TotalTokenCount,
              PricingVersion,EstimatedUsd
            FROM [analysis].[GeminiUsageAttempts] ORDER BY StartedAt,AttemptId
            """;
        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        int index = 0;
        while (await reader.ReadAsync())
        {
            DateTime started = reader.GetDateTime(1);
            string returned = reader.GetString(4);
            long prompt = reader.GetInt64(7), cached = reader.GetInt64(8), candidate = reader.GetInt64(9),
                thought = reader.GetInt64(10), total = reader.GetInt64(11);
            decimal cost = reader.GetDecimal(13);
            bool priced = GeminiUsagePricing.TryGetRates(returned,
                DateTime.SpecifyKind(started, DateTimeKind.Utc), out decimal inputRate,
                out decimal cachedRate, out decimal outputRate, out string? pricing);
            decimal recomputed = decimal.Round(((prompt - cached) * inputRate + cached * cachedRate +
                (candidate + thought) * outputRate) / 1_000_000m, 9, MidpointRounding.AwayFromZero);
            int ledgerIndex = index - baselineCalls;
            Require(priced && reader.GetString(3) == ServiceAcceptanceBudget.RequiredModel &&
                returned == ServiceAcceptanceBudget.RequiredModel && reader.GetInt32(6) == 200 &&
                total == checked(prompt + candidate + thought) && cached <= prompt &&
                pricing == reader.GetString(12) && recomputed == cost &&
                (index < baselineCalls || ledgerIndex < budget.Items.Count &&
                    budget.Items[ledgerIndex] is { Completed: true, ActualUsageReliable: true } ledger &&
                    ledger.ActualUsd == cost && ledger.ReturnedModel == returned),
                "A SQL usage row is not exactly attributable to the ordered budget ledger.");
            totalCost += cost;
            if (index < baselineCalls) observedBaselineCost += cost;
            usage.Add(Node(new { attemptId = reader.GetGuid(0), startedAt = started,
                completedAt = reader.GetDateTime(2), requestedModel = reader.GetString(3),
                returnedModel = returned, outcome = reader.GetString(5), httpStatus = reader.GetInt32(6),
                promptTokenCount = prompt, cachedTokenCount = cached, candidateTokenCount = candidate,
                thoughtTokenCount = thought, totalTokenCount = total, pricingVersion = pricing,
                estimatedUsd = cost }));
            index++;
        }
        Require(index == baselineCalls + budget.Calls && observedBaselineCost == baselineCostUsd &&
            totalCost == baselineCostUsd + budget.KnownActualSpendUsd,
            "SQL usage rows do not reconcile exactly with the aggregate ledger.");
        return Node(new { canonicalRuns = runs.Count, usageCalls = index,
            baselineCalls, baselineCostUsd, newCalls = budget.Calls,
            newCostUsd = budget.KnownActualSpendUsd, knownCostUsd = totalCost, usage,
            facultyRows = executions.Count(value => value.Completed is not null) });
    }

    private static async Task AuditSummarySourcesAsync(string database, WorkSet works)
    {
        await using AcademicDbContext db = Open(database);
        List<CanonicalArticleAnalysisRun> runs = await db.CanonicalArticleAnalysisRuns.AsNoTracking()
            .Include(value => value.ArticleSourceSnapshot).ThenInclude(value => value!.Pages)
            .Include(value => value.ArticleSourceSnapshot).ThenInclude(value => value!.Spans)
            .Include(value => value.Claims).ThenInclude(value => value.Evidence)
            .Where(value => works.CanonicalWorkIds.Contains(value.CanonicalWorkId)).ToListAsync();
        Require(runs.Count == 2, "Exactly two fresh canonical article analyses were not saved.");
        foreach (CanonicalArticleAnalysisRun run in runs)
        {
            ArticleSourceSnapshot source = run.ArticleSourceSnapshot!;
            SourceDefinition expected = run.CanonicalWorkId == works.FineCanonicalWorkId
                ? BroaderServiceAcceptancePreflight.Sources[0]
                : BroaderServiceAcceptancePreflight.Sources[1];
            Require(source.SourceKind == "pdf" && source.ExtractionVersion == ArticlePdfExtractor.Version &&
                run.Language == "tr" && run.Model.Split(',').All(value =>
                    value == ServiceAcceptanceBudget.RequiredModel) &&
                run.VerificationModel.Split(',').All(value =>
                    value == ServiceAcceptanceBudget.RequiredModel),
                $"Summary source/model boundary failed for {expected.Name}.");
            Dictionary<long, ArticleSourceSpanSnapshot> spans = source.Spans.ToDictionary(value => value.Id);
            foreach (ArticleSourceSpanSnapshot span in source.Spans)
            {
                ArticleSourcePageSnapshot page = source.Pages.Single(value => value.PageNumber == span.PageNumber);
                Require(page.Text[span.StartOffset..span.EndOffset] == span.Text,
                    $"Summary source slicing failed for {expected.Name}.");
            }
            Require(run.Claims.SelectMany(value => value.Evidence).All(link =>
                    spans.TryGetValue(link.ArticleSourceSpanId, out ArticleSourceSpanSnapshot? span) &&
                    span.ArticleSourceSnapshotId == source.Id),
                $"Summary claim evidence escaped the {expected.Name} source snapshot.");
        }
    }

    private static async Task<WorkSet> ResolveWorksAsync(string database)
    {
        await using AcademicDbContext db = Open(database);
        List<CanonicalWork> canonical = await db.CanonicalWorks.AsNoTracking()
            .Include(value => value.Observations).ThenInclude(value => value.AcademicWork)
            .Include(value => value.Researchers).ToListAsync();
        CanonicalWork fine = canonical.Single(value => value.NormalizedDoi == "10.1086/468061");
        CanonicalWork reproducibility = canonical.Single(value =>
            value.NormalizedDoi == "10.1126/science.aac4716");
        int fineAcademic = fine.Observations.Single(value =>
            value.PersonelId == BroaderAcceptanceHost.PrimarySubjectId).AcademicWorkId;
        int reproducibilityAcademic = reproducibility.Observations.Single(value =>
            value.PersonelId == BroaderAcceptanceHost.PrimarySubjectId).AcademicWorkId;
        return new(fine.Id, fineAcademic, reproducibility.Id, reproducibilityAcademic,
            canonical.Sum(value => value.Researchers.Count), canonical.Sum(value => value.Observations.Count),
            reproducibility.Researchers.Count);
    }

    private static async Task<BulkCollectionStatusResponse> PollBulkAsync(HttpClient client, Guid batchId)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMinutes(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            BulkCollectionStatusResponse response = await PostAsync<BulkCollectionStatusResponse>(client,
                "/Services/AcademicPerformance/V1/Bulk/Status", new BulkCollectionStatusRequest { BatchId = batchId });
            if (response.IsComplete) return response;
            await Task.Delay(250, runCancellation);
        }
        throw new TimeoutException("Bulk collection did not finish within two minutes.");
    }

    private static async Task PollSummariesAsync(string database, int[] workIds)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMinutes(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using AcademicDbContext db = Open(database);
            var jobs = await db.ArticleSummaryAutomationJobs.AsNoTracking()
                .Where(value => workIds.Contains(value.CanonicalWorkId))
                .Select(value => new { value.CanonicalWorkId, value.Status, value.LastOutcomeCode }).ToListAsync();
            if (jobs.Count == 2 && jobs.All(value => value.Status == ArticleSummaryAutomationJobStatus.Succeeded))
                return;
            if (jobs.Any(value => value.Status == ArticleSummaryAutomationJobStatus.Failed))
                throw new InvalidOperationException("Automatic summary failed: " +
                    string.Join(", ", jobs.Select(value => $"{value.CanonicalWorkId}:{value.LastOutcomeCode}")));
            await Task.Delay(500, runCancellation);
        }
        throw new TimeoutException("Automatic summaries did not finish within thirty minutes.");
    }

    private static async Task<ResearcherPublicationMetricsStatusResponse> PollMetricsAsync(
        HttpClient client, string personelId)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMinutes(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            HttpCapture capture = await CaptureAsync(client,
                "/Services/AcademicPerformance/V1/GetResearcherPublicationMetrics",
                new ResearcherPublicationMetricsRequest { PersonelId = personelId });
            if (capture.Status == HttpStatusCode.OK)
                return JsonSerializer.Deserialize<ResearcherPublicationMetricsStatusResponse>(capture.Body, JsonOptions)!;
            Require(capture.Status == HttpStatusCode.Accepted,
                "Publication metrics returned an unexpected status.");
            await Task.Delay(250, runCancellation);
        }
        throw new TimeoutException("Publication metrics did not become current within two minutes.");
    }

    private static async Task<FacultyAssistantRunResponse> PollFacultyAsync(HttpClient client, Guid runId)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMinutes(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            FacultyAssistantRunResponse response = await PostAsync<FacultyAssistantRunResponse>(client,
                "/Services/AcademicPerformance/V1/GetFacultyAssistantRun",
                new GetFacultyAssistantRunRequest
                { PersonelId = BroaderAcceptanceHost.PrimarySubjectId, RunId = runId });
            if (response.Status == "Completed") return response;
            if (IsTerminalFacultyStatus(response.Status))
                throw new InvalidOperationException($"Faculty run {runId} failed: {response.ErrorCode}: {response.ErrorMessage}");
            await Task.Delay(500, runCancellation);
        }
        throw new TimeoutException($"Faculty run {runId} did not finish within fifteen minutes.");
    }

    internal static void AssertStaticShape()
    {
        JsonArray matrix = MatrixNode().AsArray();
        Require(matrix.Count == 6 && matrix.OfType<JsonObject>()
                .Select(value => value["mode"]?.GetValue<string>()).Distinct().Count() == 5 &&
            matrix.OfType<JsonObject>().Count(value => value["negative"]?.GetValue<bool>() == true) == 1,
            "The frozen six-case, five-mode matrix cannot be serialized safely.");
        Require(IsTerminalFacultyStatus("Completed") && IsTerminalFacultyStatus("Failed") &&
            IsTerminalFacultyStatus("Interrupted") && !IsTerminalFacultyStatus("Running"),
            "Faculty terminal-state handling is incomplete.");
        Require(ContinuationFailuresNode([]).AsArray().Count == 0 &&
            ContinuationFailuresNode(["semantic-nonpass"]).AsArray().Single()!.GetValue<string>() ==
                "semantic-nonpass",
            "Continuation result arrays cannot be serialized safely.");
    }

    private static JsonNode MatrixNode() =>
        JsonSerializer.SerializeToNode(Cases, JsonOptions)!;

    private static bool IsTerminalFacultyStatus(string value) =>
        value is "Completed" or "Failed" or "Interrupted";

    private static void RequirePreflight(string directory)
    {
        string path = Path.Combine(directory, "preflight.json");
        JsonObject preflight = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Require(preflight["runId"]?.GetValue<string>() == Program.RunId &&
            preflight["ready"]?.GetValue<bool>() == true &&
            preflight["maximumCalls"]?.GetValue<int>() == 96 &&
            preflight["maximumSpendUsd"]?.GetValue<decimal>() == 3.00m,
            "The exact frozen preflight is absent or not ready.");
    }

    private static async Task<string> FreezeInputsAsync(string artifactDirectory)
    {
        string target = Path.Combine(artifactDirectory, "inputs");
        Directory.CreateDirectory(target);
        JsonArray manifest = [];
        foreach (SourceDefinition source in BroaderServiceAcceptancePreflight.Sources)
        {
            string origin = Path.Combine(BroaderServiceAcceptancePreflight.SourceDirectory, source.FileName);
            string copy = Path.Combine(target, source.FileName);
            byte[] bytes = await File.ReadAllBytesAsync(origin);
            Require(bytes.Length == source.ExpectedBytes &&
                BroaderServiceAcceptancePreflight.Hash(bytes) == source.ExpectedSha256,
                $"Source {source.Name} changed after preflight.");
            await using (FileStream stream = new(copy, FileMode.CreateNew, FileAccess.Write, FileShare.Read,
                81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes);
                await stream.FlushAsync();
            }
            manifest.Add(Node(new { source.Name, source.FileName, source.Doi, source.Title, source.Year,
                source.SourceUrl, bytes = bytes.Length, sha256 = source.ExpectedSha256 }));
        }
        await BroaderServiceAcceptancePreflight.WriteNewAsync(
            Path.Combine(artifactDirectory, "input-manifest.json"), manifest.ToJsonString(JsonOptions));
        return target;
    }

    private static async Task PhaseAsync(string phase, JsonObject result,
        FinalAcceptanceArtifacts artifacts, ServiceAcceptanceBudget budget, BroaderDispatchGate dispatchGate)
    {
        dispatchGate.SetPhase(phase, budget);
        result["phase"] = phase;
        result["budget"] = Node(budget.Snapshot());
        await artifacts.WritePhaseAsync(phase, (JsonObject)result.DeepClone(), budget.Snapshot());
    }

    private static async Task WriteArtifactManifestAsync(string directory, string? runId = null)
    {
        string path = Path.Combine(directory, "acceptance-artifact-manifest.json");
        if (File.Exists(path)) return;
        JsonArray entries = [];
        foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                     .Where(value => value != path).Order(StringComparer.Ordinal))
        {
            byte[] bytes = await File.ReadAllBytesAsync(file);
            entries.Add(Node(new { path = Path.GetRelativePath(directory, file).Replace('\\', '/'),
                bytes = bytes.Length, sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() }));
        }
        await BroaderServiceAcceptancePreflight.WriteNewAsync(path,
            Node(new { runId = runId ?? Program.RunId, generatedAtUtc = DateTimeOffset.UtcNow,
                artifacts = entries }).ToJsonString(JsonOptions));
    }

    private static async Task<JsonNode> FailureSqlAsync(string database)
    {
        try
        {
            await using SqlConnection connection = new(database);
            await connection.OpenAsync();
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                  (SELECT COUNT_BIG(*) FROM [analysis].[GeminiUsageAttempts]) AS UsageCalls,
                  (SELECT COUNT_BIG(*) FROM [analysis].[CanonicalArticleAnalysisRuns]) AS Summaries,
                  (SELECT COUNT_BIG(*) FROM [faculty].[AssistantRuns]) AS FacultyRuns,
                  (SELECT COUNT_BIG(*) FROM [hr].[EvidenceDossiers]) AS Dossiers
                FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
                """;
            return JsonNode.Parse((string)(await command.ExecuteScalarAsync() ?? "{}"))!;
        }
        catch (Exception exception) { return Node(new { unavailable = exception.Message }); }
    }

    private static HttpClient Client(bool authenticated = false, TimeSpan? timeout = null)
    {
        HttpClient client = new() { BaseAddress = new(CollectorUrl),
            Timeout = timeout ?? TimeSpan.FromSeconds(30) };
        if (authenticated)
            client.DefaultRequestHeaders.Add(BroaderAcceptanceHost.Header,
                BroaderAcceptanceHost.HeaderValue);
        return client;
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string path, object body,
        params HttpStatusCode[] accepted)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(path, body, JsonOptions, runCancellation);
        string text = await response.Content.ReadAsStringAsync(runCancellation);
        if (accepted.Length == 0) accepted = [HttpStatusCode.OK];
        if (!accepted.Contains(response.StatusCode))
            throw new InvalidOperationException($"{path} returned {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<T>(text, JsonOptions) ??
            throw new InvalidOperationException($"{path} returned no body.");
    }

    private static async Task<HttpCapture> CaptureAsync(HttpClient client, string path, object body)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(path, body, JsonOptions, runCancellation);
        return new(response.StatusCode, await response.Content.ReadAsStringAsync(runCancellation));
    }

    private static StartFacultyAssistantRequest Clone(StartFacultyAssistantRequest value) => new()
    {
        PersonelId = value.PersonelId,
        ClientRequestId = value.ClientRequestId,
        Mode = value.Mode,
        Language = value.Language,
        Query = value.Query,
        CanonicalWorkIds = value.CanonicalWorkIds.ToList(),
        Take = value.Take,
        ContextVersion = value.ContextVersion
    };

    private static bool Equivalent<T>(T left, T right) =>
        JsonSerializer.Serialize(left, JsonOptions) == JsonSerializer.Serialize(right, JsonOptions);

    private static JsonObject Node(object value) =>
        JsonSerializer.SerializeToNode(value, JsonOptions)!.AsObject();

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static string NewDatabaseName() => DatabasePrefix + Guid.NewGuid().ToString("N");

    private static (string Master, string Database) Connections(string name)
    {
        ValidateDatabaseName(name);
        SqlConnectionStringBuilder master = new()
        {
            DataSource = @"(localdb)\MSSQLLocalDB",
            InitialCatalog = "master",
            IntegratedSecurity = true,
            TrustServerCertificate = true,
            ConnectTimeout = 30
        };
        return (master.ConnectionString,
            new SqlConnectionStringBuilder(master.ConnectionString) { InitialCatalog = name }.ConnectionString);
    }

    private static AcademicDbContext Open(string connection) => new(
        new DbContextOptionsBuilder<AcademicDbContext>().UseSqlServer(connection).Options);

    private static async Task CreateDatabaseAsync(string masterConnection, string name)
    {
        ValidateDatabaseName(name);
        await using SqlConnection connection = new(masterConnection);
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{name}]";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(string masterConnection, string name)
    {
        ValidateDatabaseName(name);
        await using SqlConnection connection = new(masterConnection);
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = $"IF DB_ID(N'{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END";
        await command.ExecuteNonQueryAsync();
    }

    private static void ValidateDatabaseName(string name)
    {
        string suffix = name.StartsWith(DatabasePrefix, StringComparison.Ordinal)
            ? name[DatabasePrefix.Length..] : string.Empty;
        if (suffix.Length != 32 || suffix.Any(value => !Uri.IsHexDigit(value) || char.IsUpper(value)) ||
            name == BroaderServiceAcceptancePreflight.PreservedDatabase)
            throw new InvalidOperationException("The owned broader-acceptance database name is invalid.");
    }

    private sealed record FacultyCase(string Name, string Mode, string Language, string Query,
        string[] WorkNames, bool RequiresBothWorks, bool Negative);

    private sealed record FacultyExecution(FacultyCase Case, StartFacultyAssistantRequest Request,
        HttpCapture Start, FacultyAssistantRunResponse? Completed, int Calls, JsonObject Json);

    private sealed record HttpCapture(HttpStatusCode Status, string Body);

    private sealed record WorkSet(int FineCanonicalWorkId, int FineAcademicWorkId,
        int ReproducibilityCanonicalWorkId, int ReproducibilityAcademicWorkId,
        int AssociationCount, int ObservationCount, int ReproducibilityOwnerCount)
    {
        public int[] CanonicalWorkIds => [FineCanonicalWorkId, ReproducibilityCanonicalWorkId];
        public int Id(string name) => name switch
        {
            "fine" => FineCanonicalWorkId,
            "reproducibility" => ReproducibilityCanonicalWorkId,
            _ => throw new InvalidOperationException("Unknown work name: " + name)
        };
    }
}
