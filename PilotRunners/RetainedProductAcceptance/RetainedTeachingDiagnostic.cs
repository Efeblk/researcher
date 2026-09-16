using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using ResearcherAnalysisService.Products.Api.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.SqlClient;

namespace ServiceAcceptancePilot;

internal static class RetainedTeachingDiagnostic
{
    private const string CollectorUrl = "http://127.0.0.1:5230";
    private const string AnalysisUrl = "http://127.0.0.1:5130";
    private const string Query = "Bu makalenin y\u00f6ntem ve bulgular b\u00f6l\u00fcm\u00fcnden dersimde kullanabilece\u011fim bir \u00f6rnek ve bir tart\u0131\u015fma sorusu haz\u0131rla.";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    internal static async Task<int> RunAsync()
    {
        string root = RetainedAcceptancePreflight.FindRoot();
        FinalAcceptanceArtifacts artifacts = new(root);
        artifacts.EnsureLiveIsNew();
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        JsonObject frozenV10 = await RetainedV10Audit.InspectAsync(root);
        RetainedTeachingBaseline baseline = await RetainedTeachingBaselineAudit.InspectAsync();
        JsonArray sources = await RetainedSourceAudit.CaptureExecutedSourcesAsync(root);
        JsonObject result = new()
        {
            ["runId"] = Program.RunId,
            ["databaseName"] = RetainedTeachingBaselineAudit.DatabaseName,
            ["operation"] = "additive_teaching_only_diagnostic",
            ["startedAtUtc"] = startedAt,
            ["frozenV10"] = frozenV10,
            ["baseline"] = Node(baseline),
            ["executedSources"] = sources,
            ["clonePerformed"] = false,
            ["acceptedReviewHrMethodsReplayed"] = false
        };
        ServiceAcceptanceBudget budget = new(artifacts.WriteBudgetAsync, maximumCalls: 4,
            maximumSpendUsd: 0.30m);
        try
        {
            await artifacts.WritePhaseAsync("v10-baseline-frozen", (JsonObject)result.DeepClone(), budget.Snapshot());
            RetainedProviderCapture capture = new(Path.Combine(artifacts.DirectoryPath, "provider-captures"));
            await using FinalAcceptanceHeartbeat heartbeat = new(artifacts, budget,
                RetainedTeachingBaselineAudit.DatabaseName, startedAt);
            heartbeat.Set("teaching-diagnostic");
            await using WebApplication analysis = await RetainedAcceptanceHost.StartLiveAnalysisAsync(
                RetainedTeachingBaselineAudit.ConnectionString, AnalysisUrl, budget, capture);
            (int football, int contextVersion) = await ResolveInputAsync();
            Guid clientRequestId = Guid.NewGuid();
            StartFacultyAssistantRequest request = new()
            {
                PersonelId = RetainedAcceptanceHost.SubjectId,
                ClientRequestId = clientRequestId,
                Mode = "TeachingHelp", Language = "tr", Query = Query,
                CanonicalWorkIds = [football], Take = 10, ContextVersion = contextVersion
            };
            FacultyAssistantRunResponse queued;
            await using (WebApplication idle = await RetainedAcceptanceHost.StartCollectorAsync(root,
                RetainedTeachingBaselineAudit.ConnectionString, CollectorUrl, AnalysisUrl, "idle"))
            using (HttpClient client = Client(TimeSpan.FromSeconds(30)))
                queued = await PostAsync<FacultyAssistantRunResponse>(client,
                    AnalysisUrl + "/api/v1/faculty/assistant/start", request, HttpStatusCode.Accepted);
            Require(!queued.Reused && queued.Status == "Pending", "The diagnostic did not create one fresh pending row.");
            result["request"] = Node(request); result["queued"] = Node(queued);
            await artifacts.WritePhaseAsync("teaching-queued", (JsonObject)result.DeepClone(), budget.Snapshot());

            FacultyAssistantRunResponse terminal;
            await using (WebApplication worker = await RetainedAcceptanceHost.StartCollectorAsync(root,
                RetainedTeachingBaselineAudit.ConnectionString, CollectorUrl, AnalysisUrl, "faculty"))
            using (HttpClient client = Client(TimeSpan.FromMinutes(14)))
                terminal = await PollTerminalAsync(client, queued.RunId);
            result["terminal"] = Node(terminal);
            result["budget"] = Node(budget.Snapshot());
            await artifacts.WritePhaseAsync("teaching-terminal-response-captured",
                (JsonObject)result.DeepClone(), budget.Snapshot());

            await RetainedTeachingBaselineAudit.AssertOriginalRowsPreservedAsync(baseline);
            JsonObject delta = await AuditDeltaAsync(baseline, queued.RunId, budget.Snapshot());
            result["delta"] = delta;
            result["diagnosticOutcome"] = terminal.Status == "Completed" ? "completed_report_captured" :
                $"{terminal.Status.ToLowerInvariant()}_{terminal.ErrorCode ?? "unknown"}";
            result["status"] = "diagnostic_captured_awaiting_root_inspection";
            result["success"] = true;
            await artifacts.WriteResultAsync(result);
            await artifacts.WritePhaseAsync("diagnostic-captured-awaiting-root",
                (JsonObject)result.DeepClone(), budget.Snapshot());
            return 0;
        }
        catch (Exception exception)
        {
            result["budget"] = Node(budget.Snapshot());
            result["status"] = "diagnostic_failed_database_preserved";
            result["success"] = false;
            result["failure"] = Node(new { type = exception.GetType().FullName, exception.Message });
            await artifacts.WriteFailureAsync(result);
            await artifacts.WritePhaseAsync("diagnostic-failed-database-preserved",
                (JsonObject)result.DeepClone(), budget.Snapshot());
            return 1;
        }
    }

    internal static async Task<(int WorkId, int ContextVersion)> ResolveInputAsync()
    {
        await using SqlConnection connection = new(RetainedTeachingBaselineAudit.ConnectionString);
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT cw.Id,(SELECT MAX(Version) FROM [faculty].[AssistantContextVersions] WHERE PersonelId=@person) FROM [core].[CanonicalWorks] cw WHERE cw.NormalizedDoi='10.1038/s41598-022-12547-0'";
        command.Parameters.AddWithValue("@person", RetainedAcceptanceHost.SubjectId);
        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        Require(await reader.ReadAsync() && !reader.IsDBNull(0) && !reader.IsDBNull(1),
            "The retained football work or context was not uniquely available.");
        int workId = reader.GetInt32(0);
        int contextVersion = reader.GetInt32(1);
        Require(!await reader.ReadAsync(), "The retained football work was not unique.");
        return (workId, contextVersion);
    }

    private static async Task<FacultyAssistantRunResponse> PollTerminalAsync(HttpClient client, Guid runId)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMinutes(14);
        while (DateTimeOffset.UtcNow < deadline)
        {
            FacultyAssistantRunResponse response = await PostAsync<FacultyAssistantRunResponse>(client,
                AnalysisUrl + "/api/v1/faculty/assistant/run",
                new GetFacultyAssistantRunRequest { PersonelId = RetainedAcceptanceHost.SubjectId, RunId = runId });
            if (response.Status is "Completed" or "Failed" or "Interrupted") return response;
            await Task.Delay(500);
        }
        throw new TimeoutException("The Teaching diagnostic exceeded its 14-minute poll deadline.");
    }

    private static async Task<JsonObject> AuditDeltaAsync(RetainedTeachingBaseline baseline, Guid runId,
        ServiceAcceptanceBudgetSnapshot budget)
    {
        Require(budget.Calls is >= 1 and <= 4 && budget.MaximumCalls == 4 && budget.MaximumSpendUsd == 0.30m &&
            budget.UnknownUsageCalls == 0 && !budget.DispatchStopped,
            "The diagnostic budget is incomplete, unknown, or outside its released limit.");
        await using SqlConnection connection = new(RetainedTeachingBaselineAudit.ConnectionString);
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Id FROM [faculty].[AssistantRuns] WHERE RunId=@run";
        command.Parameters.AddWithValue("@run", runId);
        object? runValue = await command.ExecuteScalarAsync();
        Require(runValue is not null, "The new diagnostic faculty row is missing.");
        long newFacultyRowId = Convert.ToInt64(runValue);
        long facultyCount = await ScalarLongAsync(connection, "SELECT COUNT_BIG(*) FROM [faculty].[AssistantRuns]");
        long usageCount = await ScalarLongAsync(connection, "SELECT COUNT_BIG(*) FROM [analysis].[GeminiUsageAttempts]");
        decimal usageCost = await ScalarDecimalAsync(connection,
            "SELECT COALESCE(SUM(EstimatedUsd),0) FROM [analysis].[GeminiUsageAttempts]");
        List<Guid> newAttemptIds = [];
        await using (SqlCommand attempts = new("SELECT AttemptId FROM [analysis].[GeminiUsageAttempts] ORDER BY AttemptId", connection))
        await using (SqlDataReader reader = await attempts.ExecuteReaderAsync())
            while (await reader.ReadAsync())
            {
                Guid attemptId = reader.GetGuid(0);
                if (!baseline.AttemptIds.Contains(attemptId)) newAttemptIds.Add(attemptId);
            }
        Require(facultyCount == baseline.FacultyRunIds.Count + 1 &&
            usageCount == baseline.AttemptIds.Count + budget.Calls &&
            newAttemptIds.Count == budget.Calls && usageCost == baseline.UsageCostUsd + budget.KnownActualSpendUsd,
            "The diagnostic SQL delta does not match the request and in-memory usage ledger.");
        return Node(new { inheritedUsageCalls = baseline.AttemptIds.Count,
            newUsageCalls = budget.Calls, inheritedKnownUsd = baseline.UsageCostUsd,
            newKnownUsd = budget.KnownActualSpendUsd, totalUsageCalls = usageCount,
            totalKnownUsd = usageCost, inheritedFacultyRuns = baseline.FacultyRunIds.Count,
            newFacultyRuns = 1, newFacultyRowId, runId, newAttemptIds });
    }

    private static HttpClient Client(TimeSpan timeout)
    {
        HttpClient client = new() { BaseAddress = new(CollectorUrl), Timeout = timeout };
        client.DefaultRequestHeaders.Add("X-Analysis-Key", RetainedAcceptanceHost.ServiceKey);
        client.DefaultRequestHeaders.Add(RetainedAcceptanceHost.Header, RetainedAcceptanceHost.HeaderValue);
        return client;
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string path, object request,
        params HttpStatusCode[] accepted)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(path, request, JsonOptions);
        string body = await response.Content.ReadAsStringAsync();
        if (accepted.Length == 0) accepted = [HttpStatusCode.OK];
        if (!accepted.Contains(response.StatusCode))
            throw new InvalidOperationException($"{path} returned {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, JsonOptions) ??
            throw new InvalidOperationException($"{path} returned no body.");
    }

    private static async Task<long> ScalarLongAsync(SqlConnection connection, string sql)
    { await using SqlCommand command = new(sql, connection); return Convert.ToInt64(await command.ExecuteScalarAsync()); }
    private static async Task<decimal> ScalarDecimalAsync(SqlConnection connection, string sql)
    { await using SqlCommand command = new(sql, connection); return Convert.ToDecimal(await command.ExecuteScalarAsync()); }
    private static JsonObject Node(object value) => JsonSerializer.SerializeToNode(value, JsonOptions)!.AsObject();
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
