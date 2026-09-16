using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AcademicCollector.Analysis.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using ResearcherAnalysisService.Products.Api.Contracts;
using ResearcherAnalysisService.Products.ArticleSummaries;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using ResearcherAnalysisService.Products.Data;

namespace ServiceAcceptancePilot;

internal static class LiveServiceAcceptance
{
    private const string CollectorUrl = "http://127.0.0.1:5210";
    private const string AnalysisUrl = "http://127.0.0.1:5110";
    private const string AdamQuery = "Bu makalenin yöntem ve sınırlılıklarını kaynaklarıyla açıkla; kendi çalışmamda hangi koşulları kontrol etmeliyim?";
    private const string FootballQuery = "Bu makalenin yöntem ve bulgular bölümünden dersimde kullanabileceğim bir örnek ve bir tartışma sorusu hazırla.";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<int> RunAsync()
    {
        string root = ServiceAcceptancePreflight.FindRoot();
        string directory = Path.Combine(root, "docs", Program.RunId);
        string resultPath = Path.Combine(directory, "result.json");
        string failurePath = Path.Combine(directory, "failure.json");
        string statePath = Path.Combine(directory, "run-state.json");
        AtomicBudgetStore store = new(Path.Combine(directory, "budget.json"));
        if (File.Exists(resultPath) || File.Exists(failurePath) || File.Exists(statePath))
            throw new InvalidOperationException("This run already has state or results; implicit rerun is refused pending root direction.");
        ServiceAcceptanceBudget budget = new(store.WriteAsync, await store.ReadAsync());
        if (budget.Snapshot().DispatchStopped) throw new InvalidOperationException("The preserved ledger blocks paid resume.");
        string name = "AcademicServiceAcceptance_" + Guid.NewGuid().ToString("N");
        (string master, string database) = ServiceAcceptanceRehearsal.Connections(name);
        ReplayAudit replay = new();
        JsonObject result = new()
        {
            ["runId"] = Program.RunId, ["databaseName"] = name, ["startedAtUtc"] = DateTimeOffset.UtcNow,
            ["restartBoundary"] = "disposed and rebuilt in-process collector WebApplication host; not OS crash/soak",
            ["collectionScope"] = "synthetic faculty affiliation and two ORCID-shaped public-paper metadata responses; bounded replay, not live provider collection"
        };
        try
        {
            await CreateDatabaseAsync(master, name);
            await WriteAsync(statePath, new JsonObject { ["runId"] = Program.RunId,
                ["databaseName"] = name, ["phase"] = "database-created", ["startedAtUtc"] = DateTimeOffset.UtcNow });
            await using AcceptanceHeartbeat heartbeat = new(directory, budget, result);
            await using WebApplication analysis = await ServiceAcceptanceHost.StartLiveAnalysisAsync(database, AnalysisUrl, budget);
            result["phase"] = "bulk-submit-and-restart";
            Guid batchId = Guid.NewGuid();
            await using (WebApplication idle = await ServiceAcceptanceHost.StartCollectorAsync(root, database,
                CollectorUrl, AnalysisUrl, "idle", replay))
            using (HttpClient client = Client())
                result["bulkSubmittedPending"] = await PostNodeAsync(client,
                    "/Services/AcademicPerformance/V1/Bulk/Submit", new { BatchId = batchId,
                        Researchers = new[] { new { PersonelID = ServiceAcceptanceHost.SubjectId, ORCID = ServiceAcceptanceHost.Orcid } } });
            await using (WebApplication bulk = await ServiceAcceptanceHost.StartCollectorAsync(root, database,
                CollectorUrl, AnalysisUrl, "bulk", replay))
            using (HttpClient client = Client()) result["bulkAfterHostRestart"] = await PollBulkAsync(client, batchId);

            int adamId;
            int footballId;
            int[] workIds;
            await using (AnalysisDbContext db = ServiceAcceptanceRehearsal.Database(database))
            {
                var identities = await db.CanonicalResearcherWorks.Where(value => value.PersonelId == ServiceAcceptanceHost.SubjectId)
                    .Select(value => new { value.CanonicalWorkId, value.CanonicalWork!.NormalizedDoi,
                        Title = value.CanonicalWork.Observations.OrderBy(item => item.Id).Select(item => item.TitleObserved).FirstOrDefault() })
                    .ToListAsync();
                adamId = identities.Single(value => value.NormalizedDoi == "10.48550/arxiv.1412.6980" &&
                    value.Title == "Adam: A Method for Stochastic Optimization").CanonicalWorkId;
                footballId = identities.Single(value => value.NormalizedDoi == "10.1038/s41598-022-12547-0" &&
                    value.Title == "Multiagent off-screen behavior prediction in football").CanonicalWorkId;
                workIds = [adamId, footballId];
                if (workIds.Length != 2 || await db.ArticleSummaryAutomationJobs.CountAsync(value =>
                    workIds.Contains(value.CanonicalWorkId) && value.Status == ArticleSummaryAutomationJobStatus.Pending) != 2)
                    throw new InvalidOperationException("Collection did not persist two canonical works and pending summaries.");
            }
            result["phase"] = "automatic-pdf-summaries";
            await using (WebApplication summary = await ServiceAcceptanceHost.StartCollectorAsync(root, database,
                CollectorUrl, AnalysisUrl, "summary", replay)) await WaitForSummariesAsync(database, workIds);
            result["summaries"] = await SummaryAuditAsync(database, workIds);
            if (replay.SourceServed != 2) throw new InvalidOperationException("Exactly two reviewed PDFs were not replayed.");

            long snapshotId;
            result["phase"] = "publication-metrics";
            await using (WebApplication metric = await ServiceAcceptanceHost.StartCollectorAsync(root, database,
                CollectorUrl, AnalysisUrl, "metrics", replay))
            using (HttpClient client = Client())
            {
                _ = await PostNodeAsync(client, AnalysisUrl + "/api/v1/researchers/metrics/refresh",
                    new { PersonelID = ServiceAcceptanceHost.SubjectId });
                JsonObject metrics = await PollMetricsAsync(client);
                result["metrics"] = metrics;
                snapshotId = (metrics["snapshotId"] ?? metrics["SnapshotId"])!.GetValue<long>();
            }

            FacultyAssistantContextResponse context;
            result["phase"] = "hr-and-faculty";
            HrEvidenceDossierResponse dossier;
            HrEvidenceDossierResponse dossierRead;
            Guid adamRequest = Guid.NewGuid();
            FacultyAssistantRunResponse adamPending;
            await using (WebApplication idle = await ServiceAcceptanceHost.StartCollectorAsync(root, database,
                CollectorUrl, AnalysisUrl, "idle", replay))
            using (HttpClient client = Client(true))
            {
                context = await PostAsync<FacultyAssistantContextResponse>(client,
                    AnalysisUrl + "/api/v1/faculty/context/save", new { PersonelID = ServiceAcceptanceHost.SubjectId,
                        ExpectedVersion = 0, Context = new { Language = "tr", ResearchGoals = new[] { "Yöntem karşılaştırması" },
                            Courses = new[] { "Makine öğrenmesi" }, TeachingAudience = "Lisansüstü" } });
                dossier = await PostAsync<HrEvidenceDossierResponse>(client,
                    AnalysisUrl + "/api/v1/hr/dossiers/create", new { PersonelID = ServiceAcceptanceHost.SubjectId,
                        PublicationMetricSnapshotId = snapshotId, CanonicalWorkIds = workIds, Language = "tr" });
                dossierRead = await PostAsync<HrEvidenceDossierResponse>(client,
                    AnalysisUrl + "/api/v1/hr/dossiers", new { PersonelID = ServiceAcceptanceHost.SubjectId,
                        dossier.DossierId });
                if (JsonSerializer.Serialize(dossier, JsonOptions) != JsonSerializer.Serialize(dossierRead, JsonOptions))
                    throw new InvalidOperationException("HR dossier create/read typed payloads differ.");
                adamPending = await StartFacultyAsync(client, adamRequest, "OwnPaperMethods", AdamQuery, [adamId], context.Version);
            }
            FacultyAssistantRunResponse adam = await ProcessFacultyAsync(root, database, replay, adamPending.RunId);
            Guid footballRequest = Guid.NewGuid();
            FacultyAssistantRunResponse footballPending;
            await using (WebApplication idle = await ServiceAcceptanceHost.StartCollectorAsync(root, database,
                CollectorUrl, AnalysisUrl, "idle", replay))
            using (HttpClient client = Client(true))
                footballPending = await StartFacultyAsync(client, footballRequest, "TeachingHelp", FootballQuery, [footballId], context.Version);
            FacultyAssistantRunResponse football = await ProcessFacultyAsync(root, database, replay, footballPending.RunId);

            ServiceAcceptanceBudgetSnapshot beforeReplay = budget.Snapshot();
            result["phase"] = "readback-replay-audit";
            FacultyAssistantRunResponse replayedAdam;
            FacultyAssistantRunResponse replayedFootball;
            await using (WebApplication idle = await ServiceAcceptanceHost.StartCollectorAsync(root, database,
                CollectorUrl, AnalysisUrl, "idle", replay))
            using (HttpClient client = Client(true))
            {
                replayedAdam = await StartFacultyAsync(client, adamRequest, "OwnPaperMethods", AdamQuery, [adamId], context.Version);
                replayedFootball = await StartFacultyAsync(client, footballRequest, "TeachingHelp", FootballQuery, [footballId], context.Version);
                if (!replayedAdam.Reused || !replayedFootball.Reused)
                    throw new InvalidOperationException("Faculty duplicate requests were not reused.");
            }
            ServiceAcceptanceBudgetSnapshot finalBudget = budget.Snapshot();
            if (finalBudget.Calls != beforeReplay.Calls) throw new InvalidOperationException("Replay dispatched new AI calls.");
            result["context"] = JsonSerializer.SerializeToNode(context, JsonOptions);
            result["hrDossier"] = JsonSerializer.SerializeToNode(new { created = dossier, read = dossierRead }, JsonOptions);
            result["faculty"] = JsonSerializer.SerializeToNode(new { adamQuery = AdamQuery, adam, replayedAdam,
                footballQuery = FootballQuery, football, replayedFootball,
                replayAddedCalls = finalBudget.Calls - beforeReplay.Calls }, JsonOptions);
            result["replayAudit"] = replay.ToJson(); result["budget"] = JsonSerializer.SerializeToNode(finalBudget, JsonOptions);
            result["sql"] = await DeepAuditAsync(database, finalBudget);
            bool success = finalBudget is { DispatchStopped: false, UnknownUsageCalls: 0, Calls: <= 32,
                CommittedSpendUsd: <= 1m } && ExactCompleted(adam) && ExactCompleted(football);
            result["success"] = success; result["completedAtUtc"] = DateTimeOffset.UtcNow;
            await WriteAsync(resultPath, result);
            await analysis.StopAsync();
            if (!success) throw new InvalidOperationException("Acceptance invariants failed.");
            result["cleanup"] = "owned hosts stopped; database preserved pending root audit and explicit cleanup direction";
            result["status"] = "awaiting_root_cleanup";
            await WriteAsync(resultPath, result);
            await WriteAsync(statePath, new JsonObject { ["runId"] = Program.RunId,
                ["databaseName"] = name, ["phase"] = "awaiting-root-cleanup", ["updatedAtUtc"] = DateTimeOffset.UtcNow });
            return 0;
        }
        catch (Exception exception)
        {
            result["success"] = false; result["failure"] = new JsonObject
            { ["type"] = exception.GetType().Name, ["message"] = exception.Message };
            result["budget"] = JsonSerializer.SerializeToNode(budget.Snapshot(), JsonOptions);
            result["cleanup"] = "owned hosts disposed; database/artifacts preserved for diagnosis";
            await WriteAsync(failurePath, result);
            Console.Error.WriteLine(exception.Message); return 1;
        }
    }

    private static bool ExactCompleted(FacultyAssistantRunResponse value) => value.Status == "Completed" &&
        value.Report?.Model == ServiceAcceptanceBudget.RequiredModel &&
        value.Report.Verification.Model == ServiceAcceptanceBudget.RequiredModel;
    private static HttpClient Client(bool authenticated = false)
    {
        HttpClient value = new() { BaseAddress = new(CollectorUrl), Timeout = TimeSpan.FromSeconds(340) };
        value.DefaultRequestHeaders.Add("X-Analysis-Key", ServiceAcceptanceHost.ServiceKey);
        if (authenticated) value.DefaultRequestHeaders.Add(ServiceAcceptanceHost.Header, ServiceAcceptanceHost.HeaderValue);
        return value;
    }
    private static async Task<JsonObject> PollBulkAsync(HttpClient client, Guid id)
    {
        for (int i = 0; i < 120; i++) { JsonObject value = await PostNodeAsync(client,
            "/Services/AcademicPerformance/V1/Bulk/Status", new { BatchId = id });
            if ((value["isComplete"] ?? value["IsComplete"])?.GetValue<bool>() == true) return value; await Task.Delay(250); }
        throw new TimeoutException("Bulk worker timed out.");
    }
    private static async Task WaitForSummariesAsync(string connection, int[] ids)
    {
        for (int i = 0; i < 1200; i++) { await using AnalysisDbContext db = ServiceAcceptanceRehearsal.Database(connection);
            string[] states = await db.ArticleSummaryAutomationJobs.AsNoTracking().Where(value => ids.Contains(value.CanonicalWorkId))
                .Select(value => value.Status).ToArrayAsync();
            if (states.Length == 2 && states.All(value => value == ArticleSummaryAutomationJobStatus.Succeeded)) return;
            if (states.Any(value => value == ArticleSummaryAutomationJobStatus.Failed)) throw new InvalidOperationException("Summary failed terminally.");
            await Task.Delay(500); } throw new TimeoutException("Summary worker timed out.");
    }
    private static async Task<JsonObject> PollMetricsAsync(HttpClient client)
    {
        for (int i = 0; i < 120; i++)
        {
            ResearcherPublicationMetricsStatusResponse value = await PostAsync<ResearcherPublicationMetricsStatusResponse>(client,
                AnalysisUrl + "/api/v1/researchers/metrics", new { PersonelID = ServiceAcceptanceHost.SubjectId });
            if (value.Status == "Current" && value.SnapshotId.HasValue && value.Data is not null &&
                value.RequestedRevision == value.ComputedRevision && !value.IsStale)
                return JsonSerializer.SerializeToNode(value, JsonOptions)!.AsObject();
            if (value.Status == "Failed") throw new InvalidOperationException($"Metrics refresh failed: {value.RefreshOutcome.Code}");
            await Task.Delay(250);
        }
        throw new TimeoutException("Metrics worker timed out.");
    }
    private static async Task<FacultyAssistantRunResponse> ProcessFacultyAsync(string root, string database, ReplayAudit replay, Guid id)
    {
        await using WebApplication host = await ServiceAcceptanceHost.StartCollectorAsync(root, database, CollectorUrl, AnalysisUrl, "faculty", replay);
        using HttpClient client = Client(true);
        for (int i = 0; i < 700; i++) { FacultyAssistantRunResponse value = await PostAsync<FacultyAssistantRunResponse>(client,
            AnalysisUrl + "/api/v1/faculty/assistant/run", new { PersonelID = ServiceAcceptanceHost.SubjectId, RunId = id });
            if (value.Status == "Completed") return value; if (value.Status is "Failed" or "Interrupted")
                throw new InvalidOperationException($"Faculty ended {value.Status}: {value.ErrorCode}"); await Task.Delay(500); }
        throw new TimeoutException("Faculty worker timed out.");
    }
    private static Task<FacultyAssistantRunResponse> StartFacultyAsync(HttpClient client, Guid id, string mode,
        string query, int[] works, int version) => PostAsync<FacultyAssistantRunResponse>(client,
        AnalysisUrl + "/api/v1/faculty/assistant/start", new { PersonelID = ServiceAcceptanceHost.SubjectId,
            ClientRequestId = id, Mode = mode, Language = "tr", Query = query, CanonicalWorkIds = works, Take = 10, ContextVersion = version });
    private static object FacultyAudit(FacultyAssistantRunResponse value) => new { value.RunId, value.Status,
        value.AttemptCount, value.Report?.Outcome, value.Report?.Model, verificationModel = value.Report?.Verification.Model,
        value.Report?.Coverage, itemCount = value.Report?.Items.Count, evidenceCount = value.Retrieval.Evidence.Count,
        provenance = value.Retrieval.Evidence.SelectMany(item => item.MatchProvenance ?? []).Select(item => item.Kind).Distinct() };
    private static async Task<JsonNode> SummaryAuditAsync(string connection, int[] ids)
    {
        await using AnalysisDbContext db = ServiceAcceptanceRehearsal.Database(connection);
        var rows = await db.CanonicalArticleAnalysisRuns.AsNoTracking().Include(value => value.ArticleSourceSnapshot)
            .ThenInclude(value => value!.Pages).Where(value => ids.Contains(value.CanonicalWorkId)).OrderBy(value => value.CanonicalWorkId).ToListAsync();
        string[] hashes = rows.Select(value => value.ArticleSourceSnapshot!.ExtractedTextHash).Order().ToArray();
        if (rows.Count != 2 || rows.Any(value => value.ArticleSourceSnapshot?.SourceKind != "pdf") || !hashes.SequenceEqual(
            new[] { "c2f7f4ac5265813d06ac7f3776abc9b7dcdfe0c21efc1b943fc4059afab950ab", "ef1663dd32671bce737af77d0cfd0777fd8ebd95bb86f5d0ff2b4aa457675fa3" }))
            throw new InvalidOperationException("PDF source identities did not match preflight.");
        return JsonSerializer.SerializeToNode(rows.Select(value => new { value.CanonicalWorkId,
            sourceKind = value.ArticleSourceSnapshot!.SourceKind, sourceHash = value.ArticleSourceSnapshot.ExtractedTextHash,
            pages = value.ArticleSourceSnapshot.Pages.Count, value.Model, value.VerificationModel, value.ProcessedChunks,
            value.TotalChunks, value.ProcessedPages, value.TotalPages, value.IsPartial, value.ScopeReason }), JsonOptions)!;
    }
    private static async Task<JsonNode> SqlAuditAsync(string connection)
    {
        await using AnalysisDbContext db = ServiceAcceptanceRehearsal.Database(connection);
        return JsonSerializer.SerializeToNode(new { researchers = await db.Researchers.CountAsync(), works = await db.AcademicWorks.CountAsync(),
            canonicalWorks = await db.CanonicalWorks.CountAsync(), summaryRuns = await db.CanonicalArticleAnalysisRuns.CountAsync(),
            metricSnapshots = await db.PublicationMetricSnapshots.CountAsync(), dossiers = await db.HrEvidenceDossiers.CountAsync(),
            facultyRuns = await db.FacultyAssistantRuns.CountAsync() }, JsonOptions)!;
    }
    private static async Task<JsonNode> DeepAuditAsync(string connection, ServiceAcceptanceBudgetSnapshot ledger)
    {
        await using AnalysisDbContext db = ServiceAcceptanceRehearsal.Database(connection);
        var summaries = await db.CanonicalArticleAnalysisRuns.AsNoTracking().Include(value => value.ArticleSourceSnapshot)
            .ThenInclude(value => value!.Spans).Include(value => value.ArticleSourceSnapshot).ThenInclude(value => value!.Pages)
            .Include(value => value.SavedArticleSummary).OrderBy(value => value.Id).ToListAsync();
        var dossiers = await db.HrEvidenceDossiers.AsNoTracking().OrderBy(value => value.Id).ToListAsync();
        var faculty = await db.FacultyAssistantRuns.AsNoTracking().OrderBy(value => value.Id).ToListAsync();
        bool citationsExact = faculty.All(value => ExactPersistedCitations(value.AuthorizedInputJson, value.ReportJson));
        if (!citationsExact) throw new InvalidOperationException("A persisted faculty citation is not an exact substring of its authorized evidence.");
        await using Microsoft.Data.SqlClient.SqlConnection sql = new(connection); await sql.OpenAsync();
        using Microsoft.Data.SqlClient.SqlCommand command = sql.CreateCommand();
        command.CommandText = "SELECT COUNT_BIG(*),COALESCE(SUM(CASE WHEN EstimatedUsd IS NULL OR ReturnedModel IS NULL OR ReturnedModel<>'gemini-3.8-flash' THEN CAST(1 AS bigint) ELSE CAST(0 AS bigint) END),0),CASE WHEN SUM(CASE WHEN EstimatedUsd IS NULL THEN 1 ELSE 0 END)=0 THEN COALESCE(SUM(EstimatedUsd),0) ELSE NULL END FROM [analysis].[GeminiUsageAttempts]";
        await using var reader = await command.ExecuteReaderAsync(); await reader.ReadAsync();
        long calls = reader.GetInt64(0); long unknown = reader.GetInt64(1); decimal? spend = reader.IsDBNull(2) ? null : reader.GetDecimal(2);
        bool reconciled = calls == ledger.Calls && unknown == 0 && spend == ledger.CommittedSpendUsd;
        if (!reconciled) throw new InvalidOperationException("SQL Gemini usage does not reconcile with the acceptance ledger.");
        return JsonSerializer.SerializeToNode(new { usage = new { calls, unknown, spend, reconciled }, citationsExact,
            summaries = summaries.Select(value => new { value.Id, value.CanonicalWorkId, value.Model, value.VerificationModel,
                source = new { value.ArticleSourceSnapshotId, value.ArticleSourceSnapshot!.SourceKind,
                    value.ArticleSourceSnapshot.ExtractedTextHash,
                    pages = value.ArticleSourceSnapshot.Pages.OrderBy(page => page.Ordinal).Select(page => new
                    { page.Ordinal, number = page.PageNumber, page.Text }),
                    spans = value.ArticleSourceSnapshot.Spans.OrderBy(span => span.Ordinal).Select(span => new
                    { span.Id, span.SourceId, span.PageNumber, span.StartOffset, span.EndOffset, span.Text }) },
                savedReportJson = value.SavedArticleSummary!.ReportJson }),
            dossiers = dossiers.Select(value => new { value.Id, value.PersonelId, value.CreatedByActorId,
                value.CreatedAt, value.PolicyVersion, value.PublicationMetricSnapshotId, value.InputFingerprint,
                value.InputManifestJson, value.DossierJson }),
            faculty = faculty.Select(value => new { value.RunId, value.Status, value.RequestJson,
                value.RetrievalManifestJson, value.AuthorizedInputJson, value.ReportJson }) }, JsonOptions)!;
    }
    private static bool ExactPersistedCitations(string? inputJson, string? reportJson)
    {
        if (inputJson is null || reportJson is null) return false;
        FacultyAssistantAnalysisRequest? input = JsonSerializer.Deserialize<FacultyAssistantAnalysisRequest>(inputJson, JsonOptions);
        FacultyAssistantAnalysisReport? report = JsonSerializer.Deserialize<FacultyAssistantAnalysisReport>(reportJson, JsonOptions);
        if (input is null || report is null) return false;
        Dictionary<string, string> evidence = input.Evidence.ToDictionary(value => value.EvidenceId, value => value.ExactText);
        return report.Items.SelectMany(value => value.Citations).All(value => evidence.TryGetValue(value.EvidenceId, out string? text) &&
            text.Contains(value.ExactQuote, StringComparison.Ordinal));
    }
    private static async Task<T> PostAsync<T>(HttpClient client, string path, object body)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(path, body); string text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{path} returned {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<T>(text, JsonOptions) ?? throw new JsonException();
    }
    private static async Task<JsonObject> PostNodeAsync(HttpClient client, string path, object body)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(path, body); string text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{path} returned {(int)response.StatusCode}: {text}");
        return JsonNode.Parse(text)?.AsObject() ?? throw new JsonException();
    }
    private static async Task CreateDatabaseAsync(string master, string name)
    {
        await using Microsoft.Data.SqlClient.SqlConnection connection = new(master); await connection.OpenAsync();
        using Microsoft.Data.SqlClient.SqlCommand command = connection.CreateCommand(); command.CommandText = $"CREATE DATABASE [{name}]";
        await command.ExecuteNonQueryAsync();
    }
    private static Task WriteAsync(string path, JsonObject value) => File.WriteAllTextAsync(path,
        JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8);
}

internal sealed class AcceptanceHeartbeat : IAsyncDisposable
{
    private readonly string path;
    private readonly ServiceAcceptanceBudget budget;
    private readonly JsonObject result;
    private readonly DateTimeOffset started = DateTimeOffset.UtcNow;
    private readonly PeriodicTimer timer = new(TimeSpan.FromSeconds(30));
    private readonly CancellationTokenSource stop = new();
    private readonly Task loop;

    public AcceptanceHeartbeat(string directory, ServiceAcceptanceBudget budget, JsonObject result)
    {
        path = Path.Combine(directory, "heartbeat.json"); this.budget = budget; this.result = result;
        loop = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            while (await timer.WaitForNextTickAsync(stop.Token))
            {
                JsonObject value = new() { ["runId"] = Program.RunId,
                    ["updatedAtUtc"] = DateTimeOffset.UtcNow,
                    ["elapsedSeconds"] = (DateTimeOffset.UtcNow - started).TotalSeconds,
                    ["phase"] = result["phase"]?.DeepClone(),
                    ["budget"] = JsonSerializer.SerializeToNode(budget.Snapshot()) };
                await File.WriteAllTextAsync(path, value.ToJsonString(new() { WriteIndented = true }), stop.Token);
            }
        }
        catch (OperationCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        stop.Cancel(); timer.Dispose(); await loop; stop.Dispose();
    }
}
