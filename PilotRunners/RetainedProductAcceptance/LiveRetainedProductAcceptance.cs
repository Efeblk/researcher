using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using AcademicCollector.Analysis.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleReviews;
using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Integrations.Gemini;

namespace ServiceAcceptancePilot;

internal static class LiveRetainedProductAcceptance
{
    private const string CollectorUrl = "http://127.0.0.1:5230";
    private const string AnalysisUrl = "http://127.0.0.1:5130";
    private const string MethodsQuery = "Bu makalenin yöntem ve sınırlılıklarını kaynaklarıyla açıkla; kendi çalışmamda hangi koşulları kontrol etmeliyim?";
    private const string TeachingQuery = "Bu makalenin yöntem ve bulgular bölümünden dersimde kullanabileceğim bir örnek ve bir tartışma sorusu hazırla.";
    private const string IssuesQuery = "Bu makalenin yöntem ve bulgularında yeniden kontrol edilmesi gereken noktaları, kesin hata ile belirsizliği ayırarak göster.";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    internal static async Task<int> RunAsync()
    {
        string root = RetainedAcceptancePreflight.FindRoot();
        FinalAcceptanceArtifacts artifacts = new(root);
        artifacts.EnsureLiveIsNew();
        JsonObject frozen = await RetainedSourceAudit.VerifyFrozenArtifactsAsync(root);
        JsonArray sources = await RetainedSourceAudit.CaptureExecutedSourcesAsync(root);
        RetainedDatabaseClone clone = await RetainedAcceptanceDatabase.CreateCloneAsync();
        RetainedBaseline baseline = await RetainedAcceptanceDatabase.InspectBaselineAsync(clone.DatabaseConnection);
        JsonObject result = new()
        {
            ["runId"] = Program.RunId, ["databaseName"] = clone.TargetName,
            ["sourceDatabase"] = RetainedAcceptanceDatabase.SourceName,
            ["cloneReceipt"] = Node(clone),
            ["startedAtUtc"] = DateTimeOffset.UtcNow, ["frozenArtifacts"] = frozen,
            ["executedSources"] = sources, ["baseline"] = Node(baseline),
            ["baselineIncludedInV10Budget"] = false
        };
        ServiceAcceptanceBudget budget = new(artifacts.WriteBudgetAsync);
        await PhaseAsync("clone-baseline-verified", result, artifacts, budget);
        try
        {
            await using FinalAcceptanceHeartbeat heartbeat = new(artifacts, budget, clone.TargetName,
                result["startedAtUtc"]!.GetValue<DateTimeOffset>());
            await using WebApplication analysis = await RetainedAcceptanceHost.StartLiveAnalysisAsync(
                clone.DatabaseConnection, AnalysisUrl, budget);

            heartbeat.Set("qualification-probes-six-reused");
            JsonObject qualification = await RetainedQualificationRegression.RunAsync(clone.DatabaseConnection);
            result["qualification"] = qualification;
            await PhaseAsync("qualification-probes-complete", result, artifacts, budget);

            (int adam, int football) = await ResolveWorksAndSummariesAsync(clone.DatabaseConnection);
            result["retainedSummaries"] = Node(new { adamCanonicalWorkId = adam, footballCanonicalWorkId = football,
                policy = "article-summary-v5", regenerated = false });

            CanonicalArticleReviewResponse review;
            ResearcherPublicationMetricsStatusResponse metrics;
            FacultyAssistantContextResponse context;
            HrEvidenceDossierResponse dossier;
            heartbeat.Set("fresh-review");
            budget.SetPhase("fresh-review");
            await using (WebApplication idle = await RetainedAcceptanceHost.StartCollectorAsync(
                root, clone.DatabaseConnection, CollectorUrl, AnalysisUrl, "idle"))
            using (HttpClient client = Client(true, TimeSpan.FromSeconds(1800)))
            {
                review = await PostAsync<CanonicalArticleReviewResponse>(client,
                    "/Services/AcademicPerformance/V1/ReviewCanonicalArticle", new CanonicalArticleReviewRequest
                    {
                        PersonelId = RetainedAcceptanceHost.SubjectId, CanonicalWorkId = adam,
                        Language = "tr", ForceRegeneration = false
                    });
                result["review"] = Node(review);
                await PhaseAsync("review-response-captured", result, artifacts, budget);
                metrics = await PostAsync<ResearcherPublicationMetricsStatusResponse>(client,
                    "/Services/AcademicPerformance/V1/GetResearcherPublicationMetrics",
                    new ResearcherPublicationMetricsRequest { PersonelId = RetainedAcceptanceHost.SubjectId });
                context = await PostAsync<FacultyAssistantContextResponse>(client,
                    "/Services/AcademicPerformance/V1/GetFacultyAssistantContext",
                    new GetFacultyAssistantContextRequest { PersonelId = RetainedAcceptanceHost.SubjectId, Version = 1 });
                dossier = await PostAsync<HrEvidenceDossierResponse>(client,
                    "/Services/AcademicPerformance/V1/CreateHrEvidenceDossier", new CreateHrEvidenceDossierRequest
                    {
                        PersonelId = RetainedAcceptanceHost.SubjectId, PublicationMetricSnapshotId = metrics.SnapshotId,
                        CanonicalWorkIds = [adam, football], Language = "tr"
                    });
            }
            result["publicationMetrics"] = Node(metrics);
            result["context"] = Node(context);
            result["dossier"] = Node(dossier);
            await PhaseAsync("metrics-context-dossier-responses-captured", result, artifacts, budget);
            ValidateReview(review);
            Require(metrics.Status == "Current" && !metrics.IsStale && metrics.SnapshotId.HasValue &&
                metrics.RequestedRevision == metrics.ComputedRevision, "Inherited metrics are not Current.");
            ValidateDossier(dossier, review.ReviewRunId, adam, football, context.Context.Preferences);
            await PhaseAsync("review-context-dossier-complete", result, artifacts, budget);

            heartbeat.Set("faculty-methods");
            budget.SetPhase("faculty-methods");
            FacultyRun methods = await ExecuteFacultyAsync(root, clone.DatabaseConnection, Guid.NewGuid(),
                "OwnPaperMethods", MethodsQuery, adam, context.Version);
            result["facultyMethods"] = methods.Json;
            await PhaseAsync("faculty-methods-response-captured", result, artifacts, budget);
            bool methodsMatched = ValidateFaculty(methods.Completed, "OwnPaperMethods", 3);
            result["facultyMethodsMatched"] = methodsMatched;
            heartbeat.Set("faculty-teaching");
            budget.SetPhase("faculty-teaching");
            FacultyRun teaching = await ExecuteFacultyAsync(root, clone.DatabaseConnection, Guid.NewGuid(),
                "TeachingHelp", TeachingQuery, football, context.Version);
            result["facultyTeaching"] = teaching.Json;
            await PhaseAsync("faculty-teaching-response-captured", result, artifacts, budget);
            bool teachingMatched = ValidateFaculty(teaching.Completed, "TeachingHelp", 2);
            result["facultyTeachingMatched"] = teachingMatched;
            heartbeat.Set("faculty-issues");
            budget.SetPhase("faculty-issues");
            FacultyRun issues = await ExecuteFacultyAsync(root, clone.DatabaseConnection, Guid.NewGuid(),
                "OwnPaperIssues", IssuesQuery, adam, context.Version);
            result["facultyIssues"] = issues.Json;
            await PhaseAsync("faculty-issues-response-captured", result, artifacts, budget);
            bool issuesMatched = ValidateFaculty(issues.Completed, "OwnPaperIssues", 2);
            result["facultyIssuesMatched"] = issuesMatched;

            heartbeat.Set("readback-replay");
            budget.SetPhase("readback-replay");
            ServiceAcceptanceBudgetSnapshot beforeReads = budget.Snapshot();
            JsonObject replay = await ReadbackAndReplayAsync(root, clone.DatabaseConnection,
                adam, review, context, dossier, [methods, teaching, issues]);
            Require(budget.Snapshot().Calls == beforeReads.Calls, "A read or replay added a Gemini call.");
            result["readbackAndReplay"] = replay;

            heartbeat.Set("sql-audit");
            budget.SetPhase("sql-audit");
            await RetainedAcceptanceDatabase.AssertBaselinePreservedAsync(clone.DatabaseConnection, baseline);
            JsonObject sql = await AuditSqlAsync(clone.DatabaseConnection, baseline, budget.Snapshot(), review,
                context, dossier, replay, [methods.Completed, teaching.Completed, issues.Completed]);
            SqlConnectionStringBuilder sourceBuilder = new(clone.DatabaseConnection)
            { InitialCatalog = RetainedAcceptanceDatabase.SourceName };
            RetainedBaseline finalSource = await RetainedAcceptanceDatabase.InspectBaselineAsync(sourceBuilder.ConnectionString);
            Require(JsonSerializer.Serialize(finalSource, JsonOptions) == JsonSerializer.Serialize(baseline, JsonOptions),
                "The original v5 source database changed anywhere during v10 execution.");
            result["sql"] = sql; result["budget"] = Node(budget.Snapshot());
            Require(qualification["allCriteriaMatched"]!.GetValue<bool>(), "Qualification verdict criteria failed.");
            Require(methodsMatched && teachingMatched && issuesMatched,
                "One or more faculty request-fulfillment criteria did not match.");
            Require(!budget.Snapshot().DispatchStopped && budget.Snapshot().UnknownUsageCalls == 0,
                "The v10 budget contains unknown usage.");
            result["success"] = true; result["status"] = "awaiting_root_audit";
            await artifacts.WriteResultAsync(result);
            await artifacts.WritePhaseAsync("awaiting-root-audit", (JsonObject)result.DeepClone(), budget.Snapshot());
            return 0;
        }
        catch (Exception exception)
        {
            result["success"] = false; result["status"] = "failed_database_preserved";
            result["failure"] = Node(new { type = exception.GetType().FullName, exception.Message });
            result["budget"] = Node(budget.Snapshot());
            await artifacts.WriteFailureAsync(result);
            await artifacts.WritePhaseAsync("failed-database-preserved", (JsonObject)result.DeepClone(), budget.Snapshot());
            return 1;
        }
    }

    internal static async Task<(int Adam, int Football)> ResolveWorksAndSummariesAsync(string database)
    {
        await using SqlConnection connection = new(database); await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT cw.Id,cw.NormalizedDoi,ar.PolicyVersion,ss.ExtractedTextHash
            FROM [core].[CanonicalWorks] cw
            INNER JOIN [analysis].[CanonicalArticleAnalysisRuns] ar ON ar.CanonicalWorkId=cw.Id
            INNER JOIN [analysis].[ArticleSourceSnapshots] ss ON ss.Id=ar.ArticleSourceSnapshotId
            WHERE cw.NormalizedDoi IN ('10.48550/arxiv.1412.6980','10.1038/s41598-022-12547-0')
            """;
        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        Dictionary<string, (int Id, string Policy, string Hash)> rows = [];
        while (await reader.ReadAsync()) rows.Add(reader.GetString(1), (reader.GetInt32(0), reader.GetString(2), reader.GetString(3)));
        Require(rows.Count == 2 && rows.Values.All(value => value.Policy == "article-summary-v5"),
            "Exactly two retained current summary runs were not found.");
        Require(rows["10.48550/arxiv.1412.6980"].Hash == RetainedSourceAudit.AdamSnapshotSha &&
            rows["10.1038/s41598-022-12547-0"].Hash == RetainedSourceAudit.FootballSnapshotSha,
            "Retained source snapshot hashes changed.");
        return (rows["10.48550/arxiv.1412.6980"].Id, rows["10.1038/s41598-022-12547-0"].Id);
    }

    internal static async Task<JsonObject> ValidateReadOnlySqlAsync(string database)
    {
        (int adam, int football) = await ResolveWorksAndSummariesAsync(database);
        RetainedBaseline baseline = await RetainedAcceptanceDatabase.InspectBaselineAsync(database);
        await using SqlConnection connection = new(database);
        await connection.OpenAsync();
        long usageCount = await ScalarAsync(connection, "SELECT COUNT_BIG(*) FROM [analysis].[GeminiUsageAttempts]");
        decimal usageCost = await ScalarDecimalAsync(connection,
            "SELECT COALESCE(SUM(EstimatedUsd),0) FROM [analysis].[GeminiUsageAttempts]");
        long facultyCount = await ScalarAsync(connection, "SELECT COUNT_BIG(*) FROM [faculty].[AssistantRuns]");
        long reviewCount = await ScalarAsync(connection,
            "SELECT COUNT_BIG(*) FROM [analysis].[CanonicalArticleReviewRuns]");
        long contextCount = await ScalarAsync(connection,
            "SELECT COUNT_BIG(*) FROM [faculty].[AssistantContextVersions]");
        long dossierCount = await ScalarAsync(connection, "SELECT COUNT_BIG(*) FROM [hr].[EvidenceDossiers]");
        long actionCount = await ScalarAsync(connection, "SELECT COUNT_BIG(*) FROM [hr].[DossierReviewActions]");

        await using (SqlCommand context = connection.CreateCommand())
        {
            context.CommandText = "SELECT ContextFingerprint FROM [faculty].[AssistantContextVersions] " +
                "WHERE Version=1 AND PersonelId=@person";
            context.Parameters.AddWithValue("@person", RetainedAcceptanceHost.SubjectId);
            Require(await context.ExecuteScalarAsync() is string,
                "Context audit SQL found no retained row.");
        }
        await using (SqlCommand review = connection.CreateCommand())
        {
            review.CommandText = "SELECT TOP(1) ReportJson FROM [analysis].[CanonicalArticleReviewRuns] ORDER BY Id";
            string json = (string)(await review.ExecuteScalarAsync() ??
                throw new InvalidOperationException("Review audit SQL found no retained row."));
            Require(JsonSerializer.Deserialize<ArticleReviewReport>(json, JsonOptions) is not null,
                "Review audit SQL returned invalid stored JSON.");
        }
        await using (SqlCommand dossier = connection.CreateCommand())
        {
            dossier.CommandText = "SELECT TOP(1) DossierJson FROM [hr].[EvidenceDossiers] ORDER BY Id";
            string json = (string)(await dossier.ExecuteScalarAsync() ??
                throw new InvalidOperationException("Dossier audit SQL found no retained row."));
            Require(JsonSerializer.Deserialize<HrEvidenceDossierContent>(json, JsonOptions) is not null,
                "Dossier audit SQL returned invalid stored JSON.");
        }
        await using (SqlCommand action = connection.CreateCommand())
        {
            action.CommandText = "SELECT Id,ClientRequestId,ActionType,EvidenceReference,Note,ActorAuditId,RecordedAt " +
                "FROM [hr].[DossierReviewActions] WHERE DossierId=@id";
            action.Parameters.AddWithValue("@id", long.MinValue);
            await using SqlDataReader reader = await action.ExecuteReaderAsync();
            Require(reader.FieldCount == 7 && !await reader.ReadAsync(),
                "Dossier action audit SQL shape is invalid.");
        }
        await using (SqlCommand faculty = connection.CreateCommand())
        {
            faculty.CommandText = "SELECT TOP(1) ReportJson FROM [faculty].[AssistantRuns] " +
                "WHERE ReportJson IS NOT NULL ORDER BY Id";
            string json = (string)(await faculty.ExecuteScalarAsync() ??
                throw new InvalidOperationException("Faculty audit SQL found no retained report."));
            Require(JsonSerializer.Deserialize<FacultyAssistantAnalysisReport>(json, JsonOptions) is not null,
                "Faculty audit SQL returned invalid stored JSON.");
        }
        await using (SqlCommand usage = CreateNewUsageCommand(connection,
            baseline.UsageAttemptIds.Skip(1).ToArray()))
        await using (SqlDataReader reader = await usage.ExecuteReaderAsync())
        {
            Require(await reader.ReadAsync() && reader.GetGuid(0) != Guid.Empty && !reader.IsDBNull(2) &&
                reader.GetString(3).Length > 0 && reader.GetString(4).Length > 0 &&
                reader.GetString(5).Length > 0 && reader.GetInt32(6) > 0 && reader.GetInt64(7) >= 0 &&
                reader.GetInt64(8) >= 0 && reader.GetInt64(9) >= 0 && reader.GetInt64(10) >= 0 &&
                reader.GetInt64(11) >= 0 && reader.GetString(12).Length > 0 && reader.GetDecimal(13) >= 0,
                "Usage audit SQL did not return its exact typed shape.");
        }
        return Node(new { adamCanonicalWorkId = adam, footballCanonicalWorkId = football,
            usageCount, usageCost, facultyCount, reviewCount, contextCount, dossierCount, actionCount,
            rawSqlStatementsValidated = 14 });
    }

    private static void ValidateReview(CanonicalArticleReviewResponse value)
    {
        Require(!value.Reused && !value.IsStale && value.StaleReasons.Count == 0 &&
            value.Report.PolicyVersion == "article-specialist-review-policy-v5" &&
            value.Report.PromptVersion == ArticleReviewPrompt.Version &&
            value.Report.Verification.PromptVersion == ArticleReviewVerificationPrompt.Version &&
            value.Report.Reviews.Select(x => x.Role).SequenceEqual(["method", "quantitative", "claim_evidence", "teaching"]) &&
            value.Report.Coverage.ProcessedRoles == 4 && value.Report.Coverage.TotalRoles == 4,
            "The fresh v10 specialist review is incomplete or stale.");
    }

    private static void ValidateDossier(HrEvidenceDossierResponse value, long reviewId, int adam, int football,
        string? privateSentinel = null)
    {
        Require(value.Dossier.PublicationMetrics is { IsStale: false } &&
            value.Dossier.Works.Single(x => x.CanonicalWorkId == adam).Reviews.Any(x =>
                x.ReviewRunId == reviewId && !x.IsStale && x.AnalysisFreshnessStatus == "Current") &&
            value.Dossier.Works.Single(x => x.CanonicalWorkId == football).Reviews.Count == 0,
            "The fresh dossier did not retain the expected current review evidence.");
        if (!string.IsNullOrWhiteSpace(privateSentinel))
            Require(!JsonSerializer.Serialize(value, JsonOptions).Contains(privateSentinel, StringComparison.Ordinal),
                "Inherited private context leaked into the HR dossier.");
    }

    internal static async Task<FacultyRun> ExecuteFacultyAsync(string root, string database, Guid requestId,
        string mode, string query, int workId, int contextVersion)
    {
        StartFacultyAssistantRequest request = new() { PersonelId = RetainedAcceptanceHost.SubjectId,
            ClientRequestId = requestId, Mode = mode, Language = "tr", Query = query,
            CanonicalWorkIds = [workId], Take = 10, ContextVersion = contextVersion };
        FacultyAssistantRunResponse queued;
        await using (WebApplication idle = await RetainedAcceptanceHost.StartCollectorAsync(root, database,
            CollectorUrl, AnalysisUrl, "idle"))
        using (HttpClient client = Client(true))
            queued = await PostAsync<FacultyAssistantRunResponse>(client,
                "/Services/AcademicPerformance/V1/StartFacultyAssistant", request, HttpStatusCode.Accepted);
        Require(!queued.Reused && queued.Status == "Pending", $"{mode} did not create a new pending row.");
        FacultyAssistantRunResponse completed;
        await using (WebApplication worker = await RetainedAcceptanceHost.StartCollectorAsync(root, database,
            CollectorUrl, AnalysisUrl, "faculty"))
        using (HttpClient client = Client(true, TimeSpan.FromMinutes(31)))
            completed = await PollFacultyAsync(client, queued.RunId);
        return new(request, queued, completed, Node(new { query, request, queued, completed }));
    }

    private static async Task<FacultyAssistantRunResponse> PollFacultyAsync(HttpClient client, Guid runId)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMinutes(31);
        while (DateTimeOffset.UtcNow < deadline)
        {
            FacultyAssistantRunResponse value = await PostAsync<FacultyAssistantRunResponse>(client,
                "/Services/AcademicPerformance/V1/GetFacultyAssistantRun", new GetFacultyAssistantRunRequest
                { PersonelId = RetainedAcceptanceHost.SubjectId, RunId = runId });
            if (value.Status == "Completed") return value;
            if (value.Status is "Failed" or "Interrupted")
                throw new InvalidOperationException($"Faculty {runId} failed: {value.ErrorCode}: {value.ErrorMessage}");
            await Task.Delay(500);
        }
        throw new TimeoutException($"Faculty {runId} exceeded the 31-minute poll deadline.");
    }

    internal static bool ValidateFaculty(FacultyAssistantRunResponse value, string mode, int minimumRequirements)
    {
        FacultyAssistantAnalysisReport report = value.Report ?? throw new InvalidOperationException("Fresh report absent.");
        FacultyAssistantCoverage coverage = report.Coverage ?? throw new InvalidOperationException("Support coverage absent.");
        FacultyRequestCoverage requests = report.RequestCoverage ?? throw new InvalidOperationException("Request coverage absent.");
        FacultyAssistantGeneration generation = report.Generation ?? throw new InvalidOperationException("Generation audit absent.");
        FacultyAssistantSourceChecks sourceChecks = report.SourceChecks ??
            throw new InvalidOperationException("Source-check audit absent.");
        FacultyAssistantRepair repair = report.Repair ?? throw new InvalidOperationException("Repair audit absent.");
        bool verificationValid = coverage.CandidateItems == 0
            ? report.Verification.Status == "not_run" && report.Verification.Model.Length == 0 &&
                report.Verification.PromptVersion.Length == 0
            : report.Verification.Status == (coverage.UnverifiedItems == 0 ? "automatically_checked" : "partially_checked") &&
                report.Verification.Model == ServiceAcceptanceBudget.RequiredModel &&
                report.Verification.PromptVersion == FacultyAssistantVerificationPrompt.Version;
        bool deterministicEmpty = report.Items.Count == 0 && requests.Status == "unanswered" &&
            requests.Requirements.Count is >= 1 and <= 8 && requests.Model.Length == 0 &&
            requests.PromptVersion.Length == 0 && !requests.UsesSameModelFamily &&
            requests.Requirements.All(x => x.Status == "unanswered" && x.ItemIndexes.Count == 0);
        bool unavailable = report.Items.Count > 0 && requests.Status == "unavailable" &&
            requests.Requirements.Count == 0 && requests.Model.Length == 0 &&
            requests.PromptVersion.Length == 0 && !requests.UsesSameModelFamily;
        bool checkedCoverage = report.Items.Count > 0 && requests.Status is "fulfilled" or "partial" or "unanswered" &&
            requests.Model == ServiceAcceptanceBudget.RequiredModel &&
            requests.PromptVersion == FacultyRequestCoveragePrompt.Version && requests.UsesSameModelFamily &&
            requests.Requirements.Count is >= 1 and <= 8 &&
            requests.Requirements.All(x => x.Status is "fulfilled" or "partial" or "unanswered" &&
                (x.Status == "unanswered" ? x.ItemIndexes.Count == 0 : x.ItemIndexes.Count > 0) &&
                x.ItemIndexes.All(index => index >= 1 && index <= report.Items.Count));
        string expectedOutcome = report.Items.Count == 0 ? "no_supported_items" : unavailable ? "partial" :
            coverage.OmittedItems == 0 && requests.Status == "fulfilled" ? "completed" : "partial";
        bool sourceAuditValid = sourceChecks.TotalCandidates == sourceChecks.Checks.Count &&
            sourceChecks.CompletedChecks == sourceChecks.SupportedItems + sourceChecks.UnsupportedItems + sourceChecks.UncertainItems &&
            sourceChecks.TotalCandidates == sourceChecks.CompletedChecks + sourceChecks.UnverifiedOutputLimitItems +
                sourceChecks.UnverifiedInvalidResponseItems &&
            sourceChecks.Checks.All(x => x.AttemptId != Guid.Empty && x.Model == ServiceAcceptanceBudget.RequiredModel &&
                x.PromptVersion == FacultyAssistantVerificationPrompt.Version && x.Origin is "initial" or "repair" &&
                x.Status is "supported" or "unsupported" or "uncertain" or "unverified_output_limit" or "unverified_invalid_response" &&
                x.EvidenceIds.Count is >= 1 and <= 2);
        bool repairValid = repair.Status == "not_run"
            ? repair.RequestedCandidates == 0 && repair.GeneratedCandidates == 0 && repair.RetainedItems == 0 && repair.Attempt is null
            : repair.Status is "completed" or "output_limit" or "invalid_response" && repair.RequestedCandidates is >= 1 and <= 2 &&
                repair.Model == ServiceAcceptanceBudget.RequiredModel && repair.PromptVersion == FacultyAssistantRepairPrompt.Version &&
                repair.Attempt is not null && repair.Attempt.AttemptId != Guid.Empty && repair.Attempt.ThinkingLevel == "medium" &&
                repair.Attempt.Model == ServiceAcceptanceBudget.RequiredModel && repair.Attempt.UsagePersisted;
        Guid[] attemptIds = generation.Attempts.Select(x => x.AttemptId)
            .Concat(sourceChecks.Checks.Select(x => x.AttemptId))
            .Concat(repair.Attempt is null ? [] : [repair.Attempt.AttemptId]).ToArray();
        Require(value.Status == "Completed" && value.AttemptCount == 1 && report.Mode == mode &&
            report.Model == ServiceAcceptanceBudget.RequiredModel && report.PromptVersion == FacultyAssistantPrompt.Version &&
            verificationValid && (deterministicEmpty || unavailable || checkedCoverage) &&
            coverage.SupportedItems == report.Items.Count &&
            coverage.CandidateItems == coverage.SupportedItems + coverage.UnsupportedItems + coverage.UncertainItems + coverage.UnverifiedItems &&
            coverage.OmittedItems == coverage.UnsupportedItems + coverage.UncertainItems + coverage.UnverifiedItems &&
            coverage.IsPartial == (coverage.OmittedItems > 0) &&
            coverage.AutomaticallyCheckedItems == coverage.CandidateItems - coverage.UnverifiedItems &&
            coverage.RepairCandidateItems == sourceChecks.Checks.Count(x => x.Origin == "repair") &&
            sourceAuditValid && repairValid && attemptIds.All(x => x != Guid.Empty) &&
            attemptIds.Distinct().Count() == attemptIds.Length &&
            report.Items.All(x => !string.IsNullOrWhiteSpace(x.CandidateId)) &&
            report.Outcome == expectedOutcome &&
            value.Retrieval.Evidence.Count >= 1 && value.Retrieval.StaleAnalysisRunCount == 0 &&
            value.Retrieval.UnknownAnalysisRunCount == 0 && value.Retrieval.Evidence.All(x =>
                x.PinnedFreshnessStatus == "Current" && x.CurrentFreshnessStatus == "Current") &&
            generation.Attempts.Count is 1 or 2 && generation.Attempts.Select((x, i) => x.Ordinal == i + 1).All(x => x) &&
            generation.Attempts.All(x => x.AttemptId != Guid.Empty && x.Model == ServiceAcceptanceBudget.RequiredModel && x.UsagePersisted &&
                x.TotalTokenCount > 0 && x.EstimatedUsd >= 0 && !string.IsNullOrWhiteSpace(x.PricingVersion)) &&
            (generation.Attempts.Count == 1
                ? !generation.UsedOutputLimitRecovery && generation.Attempts[0].ThinkingLevel is "high" or "medium" && generation.Attempts[0].Outcome == "Success"
                : generation.UsedOutputLimitRecovery && generation.Attempts[0].ThinkingLevel == "high" &&
                    generation.Attempts[0].Outcome == "OutputLimit" && generation.Attempts[1].ThinkingLevel == "medium" &&
                    generation.Attempts[1].Outcome == "Success"), $"{mode} failed v10 structural or audit validation.");
        return requests.Status == "fulfilled" && requests.Requirements.Count >= minimumRequirements &&
            requests.Requirements.All(x => x.Status == "fulfilled") && coverage.SupportedItems >= 1;
    }

    internal static async Task<JsonObject> ReadbackAndReplayAsync(string root, string database, int adam,
        CanonicalArticleReviewResponse review, FacultyAssistantContextResponse context, HrEvidenceDossierResponse dossier,
        IReadOnlyList<FacultyRun> runs, string actionNote = "v10 acceptance audit")
    {
        await using WebApplication idle = await RetainedAcceptanceHost.StartCollectorAsync(root, database,
            CollectorUrl, AnalysisUrl, "idle");
        using HttpClient client = Client(true, TimeSpan.FromSeconds(1800));
        CanonicalArticleReviewResponse reviewRead = await PostAsync<CanonicalArticleReviewResponse>(client,
            "/Services/AcademicPerformance/V1/GetCanonicalArticleReview", new CanonicalArticleReviewRequest
            { PersonelId = RetainedAcceptanceHost.SubjectId, CanonicalWorkId = adam, Language = "tr" });
        CanonicalArticleReviewResponse reviewReplay = await PostAsync<CanonicalArticleReviewResponse>(client,
            "/Services/AcademicPerformance/V1/ReviewCanonicalArticle", new CanonicalArticleReviewRequest
            { PersonelId = RetainedAcceptanceHost.SubjectId, CanonicalWorkId = adam, Language = "tr" });
        Require(reviewRead.ReviewRunId == review.ReviewRunId && reviewReplay.Reused &&
            reviewReplay.ReviewRunId == review.ReviewRunId && Equivalent(review.Report, reviewRead.Report) &&
            Equivalent(review.Report, reviewReplay.Report), "Review read/cache replay mismatch.");
        FacultyAssistantContextResponse contextRead = await PostAsync<FacultyAssistantContextResponse>(client,
            "/Services/AcademicPerformance/V1/GetFacultyAssistantContext", new GetFacultyAssistantContextRequest
            { PersonelId = RetainedAcceptanceHost.SubjectId, Version = context.Version });
        HrEvidenceDossierResponse dossierRead = await PostAsync<HrEvidenceDossierResponse>(client,
            "/Services/AcademicPerformance/V1/GetHrEvidenceDossier", new GetHrEvidenceDossierRequest
            { PersonelId = RetainedAcceptanceHost.SubjectId, DossierId = dossier.DossierId });
        Require(Equivalent(context, contextRead) && Equivalent(dossier, dossierRead), "Context/dossier readback mismatch.");
        Guid actionId = Guid.NewGuid();
        AppendHrDossierReviewActionRequest action = new() { PersonelId = RetainedAcceptanceHost.SubjectId,
            DossierId = dossier.DossierId, ClientRequestId = actionId, ActionType = "Opened",
            EvidenceReference = $"review:{review.ReviewRunId}", Note = actionNote };
        HrDossierReviewActionResponse first = await PostAsync<HrDossierReviewActionResponse>(client,
            "/Services/AcademicPerformance/V1/AppendHrDossierReviewAction", action);
        HrDossierReviewActionResponse second = await PostAsync<HrDossierReviewActionResponse>(client,
            "/Services/AcademicPerformance/V1/AppendHrDossierReviewAction", action);
        HrDossierReviewActionListResponse list = await PostAsync<HrDossierReviewActionListResponse>(client,
            "/Services/AcademicPerformance/V1/ListHrDossierReviewActions", new ListHrDossierReviewActionsRequest
            { PersonelId = RetainedAcceptanceHost.SubjectId, DossierId = dossier.DossierId, Take = 100 });
        HttpCapture conflict = await CaptureAsync(client, "/Services/AcademicPerformance/V1/AppendHrDossierReviewAction",
            new AppendHrDossierReviewActionRequest { PersonelId = action.PersonelId, DossierId = action.DossierId,
                ClientRequestId = actionId, ActionType = "Opened", Note = "changed" });
        Require(!first.Reused && second.Reused && first.DossierId == dossier.DossierId &&
            Equivalent(first.Action, second.Action) && list.TotalCount == 1 && list.Actions.Count == 1 &&
            Equivalent(first.Action, list.Actions.Single()) && conflict.Status == HttpStatusCode.Conflict,
            "HR action replay/list/conflict failed.");
        JsonArray facultyReadbacks = [];
        foreach (FacultyRun run in runs)
        {
            FacultyAssistantRunResponse replayed = await PostAsync<FacultyAssistantRunResponse>(client,
                "/Services/AcademicPerformance/V1/StartFacultyAssistant", run.Request, HttpStatusCode.Accepted);
            FacultyAssistantRunResponse read = await PostAsync<FacultyAssistantRunResponse>(client,
                "/Services/AcademicPerformance/V1/GetFacultyAssistantRun", new GetFacultyAssistantRunRequest
                { PersonelId = RetainedAcceptanceHost.SubjectId, RunId = run.Completed.RunId });
            Require(replayed.Reused && replayed.RunId == run.Completed.RunId && Equivalent(read, run.Completed) &&
                Equivalent(replayed.Report, run.Completed.Report),
                "Faculty replay/readback mismatch.");
            StartFacultyAssistantRequest changed = new() { PersonelId = run.Request.PersonelId,
                ClientRequestId = run.Request.ClientRequestId, Mode = run.Request.Mode, Language = run.Request.Language,
                Query = run.Request.Query + " değişti", CanonicalWorkIds = run.Request.CanonicalWorkIds,
                Take = run.Request.Take, ContextVersion = run.Request.ContextVersion };
            Require((await CaptureAsync(client, "/Services/AcademicPerformance/V1/StartFacultyAssistant", changed)).Status ==
                HttpStatusCode.Conflict, "Changed faculty replay did not return 409.");
            facultyReadbacks.Add(Node(new { original = run.Completed, replayed, read,
                changedPayloadStatus = 409 }));
        }
        return Node(new { reviewRead, reviewReplay, contextRead, dossierRead, actionRequestId = actionId,
            first, second, list,
            changedActionStatus = (int)conflict.Status, facultyReadbacks });
    }

    private static async Task<JsonObject> AuditSqlAsync(string database, RetainedBaseline baseline,
        ServiceAcceptanceBudgetSnapshot budget, CanonicalArticleReviewResponse review,
        FacultyAssistantContextResponse context, HrEvidenceDossierResponse dossier, JsonObject replay,
        IReadOnlyList<FacultyAssistantRunResponse> faculty)
    {
        await using SqlConnection connection = new(database); await connection.OpenAsync();
        long usageCount = await ScalarAsync(connection, "SELECT COUNT_BIG(*) FROM [analysis].[GeminiUsageAttempts]");
        decimal usageCost = await ScalarDecimalAsync(connection, "SELECT COALESCE(SUM(EstimatedUsd),0) FROM [analysis].[GeminiUsageAttempts]");
        long facultyCount = await ScalarAsync(connection, "SELECT COUNT_BIG(*) FROM [faculty].[AssistantRuns]");
        long reviewCount = await ScalarAsync(connection, "SELECT COUNT_BIG(*) FROM [analysis].[CanonicalArticleReviewRuns]");
        long contextCount = await ScalarAsync(connection, "SELECT COUNT_BIG(*) FROM [faculty].[AssistantContextVersions]");
        long dossierCount = await ScalarAsync(connection, "SELECT COUNT_BIG(*) FROM [hr].[EvidenceDossiers]");
        long actionCount = await ScalarAsync(connection, "SELECT COUNT_BIG(*) FROM [hr].[DossierReviewActions]");
        Require(usageCount == baseline.UsageCalls + budget.Calls && usageCost == baseline.UsageCostUsd + budget.KnownActualSpendUsd,
            "Baseline plus v10 usage does not reconcile exactly.");
        Require(facultyCount == baseline.FacultyRuns + 3 && reviewCount == baseline.ReviewRuns + 1,
            "Fresh review/faculty row deltas are incorrect.");
        Require(contextCount == baseline.FacultyContexts && dossierCount == baseline.Dossiers + 1 &&
            actionCount == baseline.DossierActions + 1, "Context/dossier/action row deltas are incorrect.");
        await using (SqlCommand contextCommand = connection.CreateCommand())
        {
            contextCommand.CommandText = "SELECT ContextFingerprint FROM [faculty].[AssistantContextVersions] WHERE Version=1 AND PersonelId=@person";
            contextCommand.Parameters.AddWithValue("@person", RetainedAcceptanceHost.SubjectId);
            Require((string)(await contextCommand.ExecuteScalarAsync() ?? "") == context.Fingerprint,
                "Inherited context fingerprint changed.");
        }
        await using (SqlCommand reviewCommand = connection.CreateCommand())
        {
            reviewCommand.CommandText = "SELECT ReportJson FROM [analysis].[CanonicalArticleReviewRuns] WHERE Id=@id";
            reviewCommand.Parameters.AddWithValue("@id", review.ReviewRunId);
            string json = (string)(await reviewCommand.ExecuteScalarAsync() ??
                throw new InvalidOperationException("Review SQL row absent."));
            ArticleReviewReport sql = JsonSerializer.Deserialize<ArticleReviewReport>(json, JsonOptions)!;
            Require(Equivalent(sql, review.Report), "Review public/SQL report parity failed.");
        }
        await using (SqlCommand dossierCommand = connection.CreateCommand())
        {
            dossierCommand.CommandText = "SELECT DossierJson FROM [hr].[EvidenceDossiers] WHERE Id=@id";
            dossierCommand.Parameters.AddWithValue("@id", dossier.DossierId);
            string json = (string)(await dossierCommand.ExecuteScalarAsync() ??
                throw new InvalidOperationException("Dossier SQL row absent."));
            HrEvidenceDossierContent sql = JsonSerializer.Deserialize<HrEvidenceDossierContent>(json, JsonOptions)!;
            Require(Equivalent(sql, dossier.Dossier), "Dossier public/SQL parity failed.");
        }
        HrDossierReviewActionResponse publicAction = replay["first"]!.Deserialize<HrDossierReviewActionResponse>(JsonOptions)!;
        Guid actionRequestId = replay["actionRequestId"]!.GetValue<Guid>();
        await using (SqlCommand actionCommand = connection.CreateCommand())
        {
            actionCommand.CommandText = "SELECT Id,ClientRequestId,ActionType,EvidenceReference,Note,ActorAuditId,RecordedAt FROM [hr].[DossierReviewActions] WHERE DossierId=@id";
            actionCommand.Parameters.AddWithValue("@id", dossier.DossierId);
            await using SqlDataReader actionReader = await actionCommand.ExecuteReaderAsync();
            Require(await actionReader.ReadAsync(), "New dossier action SQL row absent.");
            Require(actionReader.GetInt64(0) == publicAction.Action.Id && actionReader.GetGuid(1) == actionRequestId &&
                actionReader.GetString(2) == publicAction.Action.ActionType &&
                (actionReader.IsDBNull(3) ? null : actionReader.GetString(3)) == publicAction.Action.EvidenceReference &&
                (actionReader.IsDBNull(4) ? null : actionReader.GetString(4)) == publicAction.Action.Note &&
                actionReader.GetString(5) == publicAction.Action.ActorAuditId &&
                actionReader.GetDateTimeOffset(6) == publicAction.Action.RecordedAt && !await actionReader.ReadAsync(),
                "Dossier action public/SQL/request parity failed.");
        }
        foreach (FacultyAssistantRunResponse response in faculty)
        {
            await using SqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT ReportJson FROM [faculty].[AssistantRuns] WHERE RunId=@id";
            cmd.Parameters.AddWithValue("@id", response.RunId);
            string json = (string)(await cmd.ExecuteScalarAsync() ?? throw new InvalidOperationException("Faculty SQL row absent."));
            FacultyAssistantAnalysisReport sql = JsonSerializer.Deserialize<FacultyAssistantAnalysisReport>(json, JsonOptions)!;
            Require(Equivalent(sql, response.Report), "Faculty public/SQL report parity failed.");
        }
        JsonArray newUsage = await AuditNewUsageAsync(connection, baseline.UsageAttemptIds, budget);
        await ValidatePersistedCitationsAsync(database, review, faculty);
        return Node(new { usageCount, usageCost, newUsageRows = usageCount - baseline.UsageCalls,
            newUsageCostUsd = usageCost - baseline.UsageCostUsd, facultyCount, reviewCount,
            contextCount, dossierCount, actionCount, newUsage });
    }

    internal static async Task<JsonArray> AuditNewUsageAsync(SqlConnection connection,
        IReadOnlyList<Guid> baselineAttemptIds, ServiceAcceptanceBudgetSnapshot budget,
        IReadOnlySet<Guid>? knownInvalidAttemptIds = null)
    {
        await using SqlCommand command = CreateNewUsageCommand(connection, baselineAttemptIds);
        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        JsonArray rows = []; decimal totalCost = 0; int rowIndex = 0;
        while (await reader.ReadAsync())
        {
            Guid id = reader.GetGuid(0); DateTime started = reader.GetDateTime(1);
            Require(!reader.IsDBNull(2) && !reader.IsDBNull(4) && !reader.IsDBNull(6) &&
                !reader.IsDBNull(12) && !reader.IsDBNull(13), "A new usage row is incomplete.");
            string requested = reader.GetString(3); string returned = reader.GetString(4);
            string outcome = reader.GetString(5); int httpStatus = reader.GetInt32(6);
            DateTime completed = reader.GetDateTime(2);
            long prompt = reader.GetInt64(7), cached = reader.GetInt64(8), candidate = reader.GetInt64(9),
                thought = reader.GetInt64(10), total = reader.GetInt64(11);
            decimal cost = reader.GetDecimal(13);
            bool priced = GeminiUsagePricing.TryGetRates(returned,
                DateTime.SpecifyKind(started, DateTimeKind.Utc), out decimal inputRate,
                out decimal cachedRate, out decimal outputRate, out string? pricing);
            bool acceptedOutcome = outcome is "Success" or "OutputLimit" ||
                knownInvalidAttemptIds?.Contains(id) == true &&
                outcome is "InvalidJson" or "IncompleteOutput" or "InvalidResponse" or "InvalidEvidence";
            Require(requested == ServiceAcceptanceBudget.RequiredModel && returned == requested &&
                prompt >= 0 && cached >= 0 && candidate >= 0 && thought >= 0 && cached <= prompt &&
                total == checked(prompt + candidate + thought) && cost >= 0 && priced &&
                pricing == reader.GetString(12) && acceptedOutcome && httpStatus == 200 &&
                completed >= started, "A new usage row is not exactly attributable.");
            decimal recomputed = decimal.Round(((prompt - cached) * inputRate + cached * cachedRate +
                (candidate + thought) * outputRate) / 1_000_000m, 9, MidpointRounding.AwayFromZero);
            Require(recomputed == cost, "A new usage row price did not recompute exactly.");
            Require(rowIndex < budget.Items.Count && budget.Items[rowIndex] is { Completed: true,
                    ActualUsageReliable: true } ledger && ledger.ReturnedModel == returned &&
                ledger.HttpStatus == httpStatus && ledger.PricingVersion == pricing && ledger.ActualUsd == cost,
                "A new SQL usage row differs from its ordered budget item.");
            totalCost += cost;
            rows.Add(Node(new { attemptId = id, startedAt = started, completedAt = reader.GetDateTime(2),
                requestedModel = requested, returnedModel = returned, outcome = reader.GetString(5),
                httpStatus = reader.GetInt32(6), promptTokenCount = prompt, cachedTokenCount = cached,
                candidateTokenCount = candidate, thoughtTokenCount = thought, totalTokenCount = total,
                pricingVersion = pricing, estimatedUsd = cost }));
            rowIndex++;
        }
        Require(rows.Count == budget.Calls && totalCost == budget.KnownActualSpendUsd,
            "New SQL usage rows do not reconcile to the v10 ledger.");
        return rows;
    }

    private static SqlCommand CreateNewUsageCommand(SqlConnection connection,
        IReadOnlyList<Guid> attemptIds)
    {
        SqlCommand command = connection.CreateCommand();
        List<string> parameters = [];
        for (int index = 0; index < attemptIds.Count; index++)
        {
            string name = "@id" + index; parameters.Add(name);
            command.Parameters.AddWithValue(name, attemptIds[index]);
        }
        command.CommandText = $"""
            SELECT AttemptId,StartedAt,CompletedAt,RequestedModel,ReturnedModel,Outcome,HttpStatus,
              PromptTokenCount,CachedTokenCount,CandidateTokenCount,ThoughtTokenCount,TotalTokenCount,
              PricingVersion,EstimatedUsd
            FROM [analysis].[GeminiUsageAttempts]
            WHERE AttemptId NOT IN ({string.Join(',', parameters)}) ORDER BY StartedAt,AttemptId
            """;
        return command;
    }

    internal static async Task ValidatePersistedCitationsAsync(string database,
        CanonicalArticleReviewResponse review, IReadOnlyList<FacultyAssistantRunResponse> faculty)
    {
        DbContextOptions<AcademicDbContext> options = new DbContextOptionsBuilder<AcademicDbContext>()
            .UseSqlServer(database).Options;
        await using AcademicDbContext db = new(options);
        CanonicalArticleReviewRun persistedReview = await db.CanonicalArticleReviewRuns.AsNoTracking()
            .Include(x => x.ArticleSourceSnapshot).ThenInclude(x => x!.Pages)
            .Include(x => x.ArticleSourceSnapshot).ThenInclude(x => x!.Spans)
            .SingleAsync(x => x.Id == review.ReviewRunId);
        ArticleSourceSnapshot source = persistedReview.ArticleSourceSnapshot!;
        Dictionary<string, ArticleSourceSpanSnapshot> reviewSpans = source.Spans.ToDictionary(x => x.SourceId);
        foreach (ArticleSourceSpanSnapshot span in source.Spans)
        {
            ArticleSourcePageSnapshot page = source.Pages.Single(x => x.PageNumber == span.PageNumber);
            Require(span.ArticleSourceSnapshotId == source.Id && page.ArticleSourceSnapshotId == source.Id &&
                span.StartOffset >= 0 && span.EndOffset <= page.Text.Length &&
                page.Text.Substring(span.StartOffset, span.EndOffset - span.StartOffset) == span.Text,
                "Review source catalog failed exact UTF-16 slicing.");
        }
        foreach (ArticleReviewEvidence item in review.Report.Reviews.SelectMany(x => x.Findings).SelectMany(x => x.Evidence))
        {
            Require(reviewSpans.TryGetValue(item.SourceId, out ArticleSourceSpanSnapshot? span) &&
                span.PageNumber == item.PageNumber && span.StartOffset == item.StartOffset &&
                span.EndOffset == item.EndOffset && span.Text == item.Quote,
                "Review evidence escaped its persisted source snapshot.");
        }
        foreach (FacultyAssistantRunResponse api in faculty)
        {
            var row = await db.FacultyAssistantRuns.AsNoTracking().SingleAsync(x => x.RunId == api.RunId);
            FacultyAssistantAnalysisRequest input = JsonSerializer.Deserialize<FacultyAssistantAnalysisRequest>(
                row.AuthorizedInputJson!, JsonOptions)!;
            Dictionary<string, FacultyAssistantEvidence> authorized = input.Evidence.ToDictionary(x => x.EvidenceId);
            Dictionary<string, FacultyAssistantRetrievedEvidence> retrieved = api.Retrieval.Evidence.ToDictionary(x => x.EvidenceId);
            long[] ids = authorized.Values.Select(x => x.SourceSpanId).ToArray();
            Dictionary<long, ArticleSourceSpanSnapshot> spans = await db.ArticleSourceSpans.AsNoTracking()
                .Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
            long[] snapshots = spans.Values.Select(x => x.ArticleSourceSnapshotId).Distinct().ToArray();
            List<ArticleSourcePageSnapshot> pages = await db.ArticleSourcePages.AsNoTracking()
                .Where(x => snapshots.Contains(x.ArticleSourceSnapshotId)).ToListAsync();
            foreach (FacultyAssistantEvidence item in authorized.Values)
            {
                Require(retrieved.TryGetValue(item.EvidenceId, out FacultyAssistantRetrievedEvidence? publicItem) &&
                    publicItem.ArticleSourceSpanId == item.SourceSpanId && publicItem.SourceId == item.SourceId &&
                    publicItem.PageNumber == item.PageNumber && publicItem.StartOffset == item.StartOffset &&
                    publicItem.EndOffset == item.EndOffset && spans.TryGetValue(item.SourceSpanId, out ArticleSourceSpanSnapshot? span) &&
                    span.SourceId == item.SourceId && span.PageNumber == item.PageNumber && span.StartOffset == item.StartOffset &&
                    span.EndOffset == item.EndOffset && span.Text == item.ExactText &&
                    pages.Single(x => x.ArticleSourceSnapshotId == span.ArticleSourceSnapshotId &&
                        x.PageNumber == span.PageNumber).Text.Substring(span.StartOffset, span.EndOffset - span.StartOffset) == item.ExactText,
                    "Faculty authorized/public/SQL evidence differs.");
            }
            foreach (FacultyAssistantCitation citation in api.Report!.Items.SelectMany(x => x.Citations))
                Require(authorized.TryGetValue(citation.EvidenceId, out FacultyAssistantEvidence? item) &&
                    citation.ExactQuote == item.ExactText, "Faculty citation escaped its authorized evidence set.");
            FacultyAssistantAnalysisReport sql = JsonSerializer.Deserialize<FacultyAssistantAnalysisReport>(row.ReportJson!, JsonOptions)!;
            Require(Equivalent(sql, api.Report), "Faculty SQL/API typed report parity failed.");
        }
    }

    private static async Task<long> ScalarAsync(SqlConnection connection, string sql)
    { await using SqlCommand c = connection.CreateCommand(); c.CommandText = sql; return Convert.ToInt64(await c.ExecuteScalarAsync()); }
    private static async Task<decimal> ScalarDecimalAsync(SqlConnection connection, string sql)
    { await using SqlCommand c = connection.CreateCommand(); c.CommandText = sql; return Convert.ToDecimal(await c.ExecuteScalarAsync()); }
    private static async Task PhaseAsync(string phase, JsonObject state, FinalAcceptanceArtifacts artifacts,
        ServiceAcceptanceBudget budget)
    { state["phase"] = phase; state["budget"] = Node(budget.Snapshot()); await artifacts.WritePhaseAsync(phase, (JsonObject)state.DeepClone(), budget.Snapshot()); }
    private static HttpClient Client(bool authenticated, TimeSpan? timeout = null)
    { HttpClient c = new() { BaseAddress = new(CollectorUrl), Timeout = timeout ?? TimeSpan.FromSeconds(30) };
      if (authenticated) c.DefaultRequestHeaders.Add(RetainedAcceptanceHost.Header, RetainedAcceptanceHost.HeaderValue); return c; }
    private static async Task<T> PostAsync<T>(HttpClient client, string path, object request,
        params HttpStatusCode[] accepted)
    { using HttpResponseMessage response = await client.PostAsJsonAsync(path, request, JsonOptions);
      if (accepted.Length == 0) accepted = [HttpStatusCode.OK];
      string body = await response.Content.ReadAsStringAsync();
      if (!accepted.Contains(response.StatusCode)) throw new InvalidOperationException($"{path} returned {(int)response.StatusCode}: {body}");
      return JsonSerializer.Deserialize<T>(body, JsonOptions) ?? throw new InvalidOperationException($"{path} returned no body."); }
    private static async Task<HttpCapture> CaptureAsync(HttpClient client, string path, object request)
    { using HttpResponseMessage response = await client.PostAsJsonAsync(path, request, JsonOptions);
      return new(response.StatusCode, await response.Content.ReadAsStringAsync()); }
    private static bool Equivalent<T>(T first, T second) => JsonSerializer.Serialize(first, JsonOptions) == JsonSerializer.Serialize(second, JsonOptions);
    private static JsonObject Node(object value) => JsonSerializer.SerializeToNode(value, JsonOptions)!.AsObject();
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    internal sealed record FacultyRun(StartFacultyAssistantRequest Request, FacultyAssistantRunResponse Queued,
        FacultyAssistantRunResponse Completed, JsonObject Json);
    private sealed record HttpCapture(HttpStatusCode Status, string Body);
}
