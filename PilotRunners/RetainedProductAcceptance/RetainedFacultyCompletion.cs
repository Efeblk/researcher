using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using AcademicCollector.Analysis.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using ResearcherAnalysisService.Products.Api.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.SqlClient;

namespace ServiceAcceptancePilot;

internal static class RetainedFacultyCompletion
{
    private const string CollectorUrl = "http://127.0.0.1:5230";
    private const string AnalysisUrl = "http://127.0.0.1:5130";
    private const string MethodsQuery = "Bu makalenin y\u00f6ntem ve s\u0131n\u0131rl\u0131l\u0131klar\u0131n\u0131 kaynaklar\u0131yla a\u00e7\u0131kla; kendi \u00e7al\u0131\u015fmamda hangi ko\u015fullar\u0131 kontrol etmeliyim?";
    private const string TeachingQuery = "Bu makalenin y\u00f6ntem ve bulgular b\u00f6l\u00fcm\u00fcnden dersimde kullanabilece\u011fim bir \u00f6rnek ve bir tart\u0131\u015fma sorusu haz\u0131rla.";
    private const string IssuesQuery = "Bu makalenin y\u00f6ntem ve bulgular\u0131nda yeniden kontrol edilmesi gereken noktalar\u0131, kesin hata ile belirsizli\u011fi ay\u0131rarak g\u00f6ster.";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    internal static async Task<int> RunAsync()
    {
        string root = RetainedAcceptancePreflight.FindRoot();
        FinalAcceptanceArtifacts artifacts = new(root); artifacts.EnsureLiveIsNew();
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        JsonObject frozen = await VerifyV13Async(root);
        RetainedTeachingBaseline baseline = await RetainedTeachingBaselineAudit.InspectAsync();
        JsonArray sources = await RetainedSourceAudit.CaptureExecutedSourcesAsync(root);
        JsonObject result = new() { ["runId"] = Program.RunId, ["databaseName"] = RetainedTeachingBaselineAudit.DatabaseName,
            ["startedAtUtc"] = startedAt, ["operation"] = "fresh_three_mode_faculty_completion",
            ["frozenV13"] = frozen, ["baseline"] = Node(baseline), ["executedSources"] = sources,
            ["reviewHrRegenerated"] = false, ["clonePerformed"] = false };
        ServiceAcceptanceBudget budget = new(artifacts.WriteBudgetAsync, maximumCalls: 38, maximumSpendUsd: 2m);
        try
        {
            await artifacts.WritePhaseAsync("v13-baseline-verified", (JsonObject)result.DeepClone(), budget.Snapshot());
            RetainedProviderCapture capture = new(Path.Combine(artifacts.DirectoryPath, "provider-captures"));
            await using FinalAcceptanceHeartbeat heartbeat = new(artifacts, budget,
                RetainedTeachingBaselineAudit.DatabaseName, startedAt);
            await using WebApplication analysis = await RetainedAcceptanceHost.StartLiveAnalysisAsync(
                RetainedTeachingBaselineAudit.ConnectionString, AnalysisUrl, budget, capture);
            heartbeat.Set("faculty-verifier-qualification");
            JsonObject qualification = await RetainedFacultyQualificationRegression.RunAsync(
                analysis.Services, RetainedTeachingBaselineAudit.ConnectionString);
            result["facultyVerifierQualification"] = qualification;
            await artifacts.WritePhaseAsync("faculty-verifier-qualification-captured",
                (JsonObject)result.DeepClone(), budget.Snapshot());
            Require(qualification["allCriteriaMatched"]!.GetValue<bool>(),
                "V14 faculty verifier qualification criteria did not match.");
            (int adam, int football) = await LiveRetainedProductAcceptance.ResolveWorksAndSummariesAsync(
                RetainedTeachingBaselineAudit.ConnectionString);
            (CanonicalArticleReviewResponse review, FacultyAssistantContextResponse context,
                HrEvidenceDossierResponse dossier) = await ReadInheritedAsync(root, adam);
            result["inheritedReview"] = Node(review); result["inheritedContext"] = Node(context);
            result["inheritedDossier"] = Node(dossier);

            List<LiveRetainedProductAcceptance.FacultyRun> runs = [];
            (string Phase, string Mode, string Query, int Work, int Min)[] cases =
            [
                ("faculty-methods", "OwnPaperMethods", MethodsQuery, adam, 3),
                ("faculty-teaching", "TeachingHelp", TeachingQuery, football, 2),
                ("faculty-issues", "OwnPaperIssues", IssuesQuery, adam, 2)
            ];
            JsonObject matches = [];
            foreach (var item in cases)
            {
                heartbeat.Set(item.Phase);
                LiveRetainedProductAcceptance.FacultyRun run = await LiveRetainedProductAcceptance.ExecuteFacultyAsync(
                    root, RetainedTeachingBaselineAudit.ConnectionString, Guid.NewGuid(), item.Mode, item.Query,
                    item.Work, context.Version);
                runs.Add(run); result[item.Phase] = run.Json;
                await artifacts.WritePhaseAsync(item.Phase + "-response-captured",
                    (JsonObject)result.DeepClone(), budget.Snapshot());
                bool matched = LiveRetainedProductAcceptance.ValidateFaculty(run.Completed, item.Mode, item.Min);
                matches[item.Mode] = matched;
            }
            result["acceptanceCriteriaMatched"] = matches;

            heartbeat.Set("zero-call-readback-replay");
            int before = budget.Snapshot().Calls;
            JsonObject replay = await LiveRetainedProductAcceptance.ReadbackAndReplayAsync(root,
                RetainedTeachingBaselineAudit.ConnectionString, adam, review, context, dossier, runs,
                    "v14 retained faculty acceptance audit");
            Require(budget.Snapshot().Calls == before, "Readback/replay/action operations added an AI call.");
            result["readbackReplayActions"] = replay;
            await RetainedTeachingBaselineAudit.AssertOriginalRowsPreservedAsync(baseline, addedActions: 1);
            await LiveRetainedProductAcceptance.ValidatePersistedCitationsAsync(
                RetainedTeachingBaselineAudit.ConnectionString, review, runs.Select(x => x.Completed).ToArray());
            result["sql"] = await AuditSqlAsync(baseline, runs, budget.Snapshot(), replay, dossier);
            Require(matches.All(x => x.Value?.GetValue<bool>() == true),
                "V14 semantic regression or fixed-mode structural criteria did not match.");
            result["budget"] = Node(budget.Snapshot()); result["success"] = true;
            result["status"] = "completed_awaiting_root_semantic_audit";
            await artifacts.WriteResultAsync(result);
            await artifacts.WritePhaseAsync("completed-awaiting-root-audit", (JsonObject)result.DeepClone(), budget.Snapshot());
            return 0;
        }
        catch (Exception exception)
        {
            result["budget"] = Node(budget.Snapshot()); result["success"] = false;
            result["status"] = "failed_database_preserved";
            result["failure"] = Node(new { type = exception.GetType().FullName, exception.Message });
            await artifacts.WriteFailureAsync(result);
            await artifacts.WritePhaseAsync("failed-database-preserved", (JsonObject)result.DeepClone(), budget.Snapshot());
            return 1;
        }
    }

    private static async Task<(CanonicalArticleReviewResponse, FacultyAssistantContextResponse, HrEvidenceDossierResponse)>
        ReadInheritedAsync(string root, int adam)
    {
        long dossierId;
        await using (SqlConnection connection = new(RetainedTeachingBaselineAudit.ConnectionString))
        {
            await connection.OpenAsync(); await using SqlCommand command = new(
                "SELECT MAX(Id) FROM [hr].[EvidenceDossiers]", connection);
            dossierId = Convert.ToInt64(await command.ExecuteScalarAsync());
        }
        await using WebApplication idle = await RetainedAcceptanceHost.StartCollectorAsync(root,
            RetainedTeachingBaselineAudit.ConnectionString, CollectorUrl, AnalysisUrl, "idle");
        using HttpClient client = Client();
        CanonicalArticleReviewResponse review = (await PostAsync<CanonicalArticleAnalysisResponse>(client,
            AnalysisUrl + "/api/v1/articles/analysis",
            new CanonicalArticleAnalysisRequest { PersonelId = RetainedAcceptanceHost.SubjectId,
                CanonicalWorkId = adam, Language = "tr" })).Review!;
        FacultyAssistantContextResponse context = await PostAsync<FacultyAssistantContextResponse>(client,
            AnalysisUrl + "/api/v1/faculty/context",
            new GetFacultyAssistantContextRequest { PersonelId = RetainedAcceptanceHost.SubjectId, Version = 1 });
        HrEvidenceDossierResponse dossier = await PostAsync<HrEvidenceDossierResponse>(client,
            AnalysisUrl + "/api/v1/hr/dossiers",
            new GetHrEvidenceDossierRequest { PersonelId = RetainedAcceptanceHost.SubjectId, DossierId = dossierId });
        return (review, context, dossier);
    }

    private static async Task<JsonObject> AuditSqlAsync(RetainedTeachingBaseline baseline,
        IReadOnlyList<LiveRetainedProductAcceptance.FacultyRun> runs, ServiceAcceptanceBudgetSnapshot budget,
        JsonObject replay, HrEvidenceDossierResponse dossier)
    {
        Require(budget.Calls is >= 8 and <= 38 && budget.UnknownUsageCalls == 0 && !budget.DispatchStopped,
            "V14 usage is incomplete or unknown.");
        await using SqlConnection connection = new(RetainedTeachingBaselineAudit.ConnectionString); await connection.OpenAsync();
        long usage = await ScalarLongAsync(connection, "SELECT COUNT_BIG(*) FROM [analysis].[GeminiUsageAttempts]");
        decimal cost = await ScalarDecimalAsync(connection, "SELECT COALESCE(SUM(EstimatedUsd),0) FROM [analysis].[GeminiUsageAttempts]");
        long faculty = await ScalarLongAsync(connection, "SELECT COUNT_BIG(*) FROM [faculty].[AssistantRuns]");
        long actions = await ScalarLongAsync(connection, "SELECT COUNT_BIG(*) FROM [hr].[DossierReviewActions]");
        Require(usage == baseline.AttemptIds.Count + budget.Calls && cost == baseline.UsageCostUsd + budget.KnownActualSpendUsd &&
            faculty == baseline.FacultyRunIds.Count + 3 && actions == baseline.ActionCount + 1,
            "V14 SQL counts/cost do not reconcile.");
        HashSet<Guid> knownInvalid = runs.SelectMany(x => x.Completed.Report?.SourceChecks?.Checks ?? [])
            .Where(x => x.Status == "unverified_invalid_response").Select(x => x.AttemptId)
            .Concat(runs.Select(x => x.Completed.Report?.Repair)
                .Where(x => x is { Status: "invalid_response", Attempt: not null })
                .Select(x => x!.Attempt!.AttemptId)).ToHashSet();
        JsonArray newUsage = await LiveRetainedProductAcceptance.AuditNewUsageAsync(
            connection, baseline.AttemptIds, budget, knownInvalid);
        Dictionary<Guid, string> outcomes = newUsage.ToDictionary(
            x => Guid.Parse(x!["attemptId"]!.GetValue<string>()),
            x => x!["outcome"]!.GetValue<string>());
        foreach (FacultyAssistantRunResponse run in runs.Select(x => x.Completed))
        {
            FacultyAssistantAnalysisReport report = run.Report!;
            foreach (FacultyAssistantGenerationAttempt attempt in report.Generation!.Attempts)
                Require(outcomes.TryGetValue(attempt.AttemptId, out string? outcome) && outcome == attempt.Outcome,
                    "A generation audit attempt does not match its SQL usage outcome.");
            foreach (FacultyAssistantSourceCheck check in report.SourceChecks!.Checks)
            {
                Require(outcomes.TryGetValue(check.AttemptId, out string? outcome),
                    "A source-check attempt is absent from new SQL usage.");
                bool matchesOutcome = check.Status switch
                {
                    "supported" or "unsupported" or "uncertain" => outcome == "Success",
                    "unverified_output_limit" => outcome == "OutputLimit",
                    "unverified_invalid_response" => outcome is "Success" or "InvalidJson" or "IncompleteOutput" or
                        "InvalidResponse" or "InvalidEvidence",
                    _ => false
                };
                Require(matchesOutcome, "A source-check status does not match its SQL usage outcome.");
            }
            if (report.Repair!.Attempt is { } repairAttempt)
            {
                Require(outcomes.TryGetValue(repairAttempt.AttemptId, out string? outcome),
                    "A repair attempt is absent from new SQL usage.");
                string expected = report.Repair.Status switch
                {
                    "completed" => "Success", "output_limit" => "OutputLimit", _ => outcome!
                };
                Require(outcome == expected && (report.Repair.Status != "invalid_response" ||
                    outcome is "Success" or "InvalidJson" or "IncompleteOutput" or "InvalidResponse" or "InvalidEvidence"),
                    "A repair status does not match its SQL usage outcome.");
            }
        }
        HrDossierReviewActionResponse publicAction = replay["first"]!
            .Deserialize<HrDossierReviewActionResponse>(JsonOptions)!;
        Guid actionRequestId = replay["actionRequestId"]!.GetValue<Guid>();
        await using (SqlCommand action = connection.CreateCommand())
        {
            action.CommandText = "SELECT Id,ClientRequestId,ActionType,EvidenceReference,Note,ActorAuditId,RecordedAt FROM [hr].[DossierReviewActions] WHERE DossierId=@id";
            action.Parameters.AddWithValue("@id", dossier.DossierId);
            await using SqlDataReader reader = await action.ExecuteReaderAsync();
            Require(await reader.ReadAsync() && reader.GetInt64(0) == publicAction.Action.Id &&
                reader.GetGuid(1) == actionRequestId && reader.GetString(2) == publicAction.Action.ActionType &&
                (reader.IsDBNull(3) ? null : reader.GetString(3)) == publicAction.Action.EvidenceReference &&
                (reader.IsDBNull(4) ? null : reader.GetString(4)) == publicAction.Action.Note &&
                reader.GetString(5) == publicAction.Action.ActorAuditId &&
                reader.GetDateTimeOffset(6) == publicAction.Action.RecordedAt && !await reader.ReadAsync(),
                "The HR action SQL/API/request fields differ.");
        }
        return Node(new { inheritedUsageCalls = baseline.AttemptIds.Count, newUsageCalls = budget.Calls,
            inheritedKnownUsd = baseline.UsageCostUsd, newKnownUsd = budget.KnownActualSpendUsd,
            totalUsageCalls = usage, totalKnownUsd = cost, newFacultyRunIds = runs.Select(x => x.Completed.RunId),
            actionCount = actions, exactModelRows = budget.Items.All(x => x.ReturnedModel == ServiceAcceptanceBudget.RequiredModel),
            newUsage });
    }

    internal static async Task<JsonObject> VerifyV11Async(string root)
    {
        string path = Path.Combine(root, "docs", "service-acceptance-20260914-v11", "diagnostic-artifact-manifest.json");
        string sha = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))).ToLowerInvariant();
        Require(sha == "9d6fe92a386e8f2d465dd01a3ac8ed321db1f2fc9ae9a69376a3b3323afbc7fd",
            "The frozen V11 manifest changed.");
        JsonObject manifest = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        JsonArray entries = manifest["entries"]!.AsArray();
        string directory = Path.GetDirectoryName(path)!;
        foreach (JsonNode? node in entries)
        {
            JsonObject entry = node!.AsObject(); string file = Path.GetFullPath(Path.Combine(directory,
                entry["path"]!.GetValue<string>().Replace('/', Path.DirectorySeparatorChar)));
            Require(File.Exists(file) && new FileInfo(file).Length == entry["bytes"]!.GetValue<long>() &&
                Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(file))).ToLowerInvariant() == entry["sha256"]!.GetValue<string>(),
                "A frozen V11 artifact changed.");
        }
        return new() { ["manifest"] = path, ["manifestSha256"] = sha, ["artifactsVerified"] = entries.Count };
    }

    internal static async Task<JsonObject> VerifyV13Async(string root)
    {
        string path = Path.Combine(root, "docs", "service-acceptance-20260914-v13", "failure-artifact-manifest.json");
        string sha = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))).ToLowerInvariant();
        Require(sha == "cc7eafad1b524e95ac4c347ad327edf2d037a421c75d4c415ecd555d1b169061",
            "The frozen V13 manifest changed.");
        JsonObject manifest = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        JsonArray entries = manifest["entries"]!.AsArray(); string directory = Path.GetDirectoryName(path)!;
        foreach (JsonNode? node in entries)
        {
            JsonObject entry = node!.AsObject(); string file = Path.GetFullPath(Path.Combine(directory,
                entry["path"]!.GetValue<string>().Replace('/', Path.DirectorySeparatorChar)));
            Require(File.Exists(file) && new FileInfo(file).Length == entry["bytes"]!.GetValue<long>() &&
                Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(file))).ToLowerInvariant() == entry["sha256"]!.GetValue<string>(),
                "A frozen V13 artifact changed.");
        }
        return new() { ["manifest"] = path, ["manifestSha256"] = sha, ["artifactsVerified"] = entries.Count };
    }

    private static HttpClient Client() { HttpClient c = new() { BaseAddress = new(CollectorUrl), Timeout = TimeSpan.FromSeconds(1800) };
        c.DefaultRequestHeaders.Add(RetainedAcceptanceHost.Header, RetainedAcceptanceHost.HeaderValue); return c; }
    private static async Task<T> PostAsync<T>(HttpClient client, string path, object request)
    { using HttpResponseMessage response = await client.PostAsJsonAsync(path, request, JsonOptions); string body = await response.Content.ReadAsStringAsync();
      Require(response.StatusCode == HttpStatusCode.OK, $"{path} returned {(int)response.StatusCode}: {body}");
      return JsonSerializer.Deserialize<T>(body, JsonOptions)!; }
    private static async Task<long> ScalarLongAsync(SqlConnection c, string sql)
    { await using SqlCommand q = new(sql, c); return Convert.ToInt64(await q.ExecuteScalarAsync()); }
    private static async Task<decimal> ScalarDecimalAsync(SqlConnection c, string sql)
    { await using SqlCommand q = new(sql, c); return Convert.ToDecimal(await q.ExecuteScalarAsync()); }
    private static JsonObject Node(object value) => JsonSerializer.SerializeToNode(value, JsonOptions)!.AsObject();
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
