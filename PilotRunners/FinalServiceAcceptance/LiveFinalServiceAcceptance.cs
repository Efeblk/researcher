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

namespace ServiceAcceptancePilot;

internal static class LiveFinalServiceAcceptance
{
    private const string CollectorUrl = "http://127.0.0.1:5230";
    private const string AnalysisUrl = "http://127.0.0.1:5130";
    private const string AdamDoi = "10.48550/arxiv.1412.6980";
    private const string FootballDoi = "10.1038/s41598-022-12547-0";
    private const string AdamTitle = "Adam: A Method for Stochastic Optimization";
    private const string FootballTitle = "Multiagent off-screen behavior prediction in football";
    private const string MethodsQuery = "Bu makalenin yöntem ve sınırlılıklarını kaynaklarıyla açıkla; kendi çalışmamda hangi koşulları kontrol etmeliyim?";
    private const string TeachingQuery = "Bu makalenin yöntem ve bulgular bölümünden dersimde kullanabileceğim bir örnek ve bir tartışma sorusu hazırla.";
    private const string IssuesQuery = "Bu makalenin yöntem ve bulgularında yeniden kontrol edilmesi gereken noktaları, kesin hata ile belirsizliği ayırarak göster.";
    private const string PrivateSentinel = "PRIVATE-FINAL-ACCEPTANCE-SENTINEL-7f1c67f7";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static Task<int> RunAsync() => RunAsync(null);

    public static async Task<int> RunAsync(
        Func<WebApplication, string, ServiceAcceptanceBudget, Task<JsonObject>>? beforeFlow)
    {
        string root = FinalServiceAcceptancePreflight.FindRoot();
        FinalAcceptanceArtifacts artifacts = new(root);
        artifacts.EnsureLiveIsNew();
        ServiceAcceptanceBudget budget = new(artifacts.WriteBudgetAsync);
        string databaseName = FinalAcceptanceDatabase.NewName();
        (string master, string database) = FinalAcceptanceDatabase.Connections(databaseName);
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        ReplayAudit replay = new();
        JsonObject result = new()
        {
            ["runId"] = Program.RunId,
            ["databaseName"] = databaseName,
            ["startedAtUtc"] = startedAt,
            ["collectorUrl"] = CollectorUrl,
            ["analysisUrl"] = AnalysisUrl,
            ["restartBoundary"] = "disposed and rebuilt in-process collector WebApplication host",
            ["collectionScope"] = "bounded ORCID metadata and immutable reviewed-PDF replay; no live provider collection",
            ["priorLifecycles"] = Program.IncludeQualifiedPriorSpend
                ? new JsonArray
                {
                    new JsonObject
                    {
                        ["runId"] = "service-acceptance-20260914-v4",
                        ["knownCostUsd"] = 0.608958m,
                        ["includedInThisLedger"] = false,
                        ["outcome"] = "mechanical_pass_request_fulfillment_failed"
                    },
                    new JsonObject
                    {
                        ["runId"] = "service-acceptance-20260914-v3",
                        ["knownCostUsd"] = 0.032181m,
                        ["includedInThisLedger"] = false,
                        ["outcome"] = "qualified_positive_uncertain"
                    },
                    new JsonObject
                    {
                        ["runId"] = "service-acceptance-20260914-v2",
                        ["knownCostUsd"] = 0.0322485m,
                        ["includedInThisLedger"] = false,
                        ["outcome"] = "known_output_limit_failure"
                    },
                    new JsonObject
                    {
                        ["runId"] = "service-acceptance-20260914-v1",
                        ["knownCostUsd"] = 0.551427m,
                        ["includedInThisLedger"] = false
                    },
                    new JsonObject
                    {
                        ["runId"] = "service-acceptance-20260913-v1",
                        ["unknownReservationUsd"] = 0.06427125m,
                        ["includedInThisLedger"] = false
                    }
                }
                : new JsonArray
                {
                    new JsonObject
                    {
                        ["runId"] = "service-acceptance-20260913-v1",
                        ["unknownReservationUsd"] = 0.06427125m,
                        ["includedInThisLedger"] = false
                    }
                }
        };
        JsonObject facultyCriteria = [];
        result["facultyCriteria"] = facultyCriteria;
        bool allFacultyCriteriaMatched = true;

        try
        {
            await FinalAcceptanceDatabase.CreateAsync(master, databaseName);
            await PhaseAsync("database-created", result, artifacts, budget);
            await using FinalAcceptanceHeartbeat heartbeat = new(artifacts, budget, databaseName, startedAt);

            await using WebApplication analysis = await ServiceAcceptanceHost.StartLiveAnalysisAsync(
                database, AnalysisUrl, budget);

            heartbeat.Set("bulk-submit-worker-off");
            Guid batchId = Guid.NewGuid();
            BulkCollectionStatusResponse submitted;
            await using (WebApplication idle = await ServiceAcceptanceHost.StartCollectorAsync(
                root, database, CollectorUrl, AnalysisUrl, "idle", replay))
            using (HttpClient client = CollectorClient())
            {
                if (beforeFlow is not null)
                {
                    heartbeat.Set("qualification-regression");
                    result["qualificationRegression"] = await beforeFlow(analysis, database, budget);
                    await PhaseAsync("qualification-regression-complete", result, artifacts, budget);
                    heartbeat.Set("bulk-submit-worker-off");
                }
                submitted = await PostAsync<BulkCollectionStatusResponse>(client,
                    "/Services/AcademicPerformance/V1/Bulk/Submit", new BulkCollectionSubmitRequest
                    {
                        BatchId = batchId,
                        Researchers =
                        [
                            new()
                            {
                                PersonelId = ServiceAcceptanceHost.SubjectId,
                                Orcid = ServiceAcceptanceHost.Orcid
                            }
                        ]
                    });
            }
            Require(!submitted.WorkerEnabled && !submitted.IsComplete &&
                submitted.Jobs.Count == 1 && submitted.Jobs[0].Status == "Pending",
                "Worker-off bulk submission was not durably pending.");
            result["bulkSubmittedPending"] = Node(submitted);
            await PhaseAsync("bulk-submitted-worker-off", result, artifacts, budget);

            heartbeat.Set("bulk-worker-after-host-restart");
            BulkCollectionStatusResponse bulk;
            await using (WebApplication worker = await ServiceAcceptanceHost.StartCollectorAsync(
                root, database, CollectorUrl, AnalysisUrl, "bulk", replay))
            using (HttpClient client = CollectorClient())
                bulk = await PollBulkAsync(client, batchId);
            Require(bulk.IsComplete && bulk.Jobs.Count == 1 && bulk.Jobs[0].Status == "Partial" &&
                bulk.Jobs[0].Attempts == 1,
                "The bounded ORCID replay did not finish in the expected terminal Partial state.");
            Require(replay.ProviderServed == 2,
                "The ORCID replay did not serve exactly the record and two-work requests.");
            result["bulkAfterHostRestart"] = Node(bulk);

            WorkIdentity works = await ResolveWorksAsync(database);
            result["canonicalWorks"] = Node(works);
            await PhaseAsync("bulk-collected-and-canonicalized", result, artifacts, budget);

            heartbeat.Set("automatic-pdf-summaries");
            await using (WebApplication summaryWorker = await ServiceAcceptanceHost.StartCollectorAsync(
                root, database, CollectorUrl, AnalysisUrl, "summary", replay))
                await WaitForTwoSequentialSummariesAsync(database, works.CanonicalWorkIds);
            Require(replay.SourceServed == 2,
                "Exactly two immutable reviewed PDF fixtures were not served.");

            SummaryEvidence summaries;
            await using (WebApplication idle = await ServiceAcceptanceHost.StartCollectorAsync(
                root, database, CollectorUrl, AnalysisUrl, "idle", replay))
            using (HttpClient client = CollectorClient())
                summaries = await AuditSummariesAsync(client, database, works);
            result["summaries"] = summaries.Json;
            await PhaseAsync("automatic-pdf-summaries-complete", result, artifacts, budget);

            heartbeat.Set("fresh-adam-specialist-review");
            CanonicalArticleReviewResponse review;
            int callsBeforeReview = budget.Snapshot().Calls;
            await using (WebApplication idle = await ServiceAcceptanceHost.StartCollectorAsync(
                root, database, CollectorUrl, AnalysisUrl, "idle", replay))
            using (HttpClient client = CollectorClient(authenticated: true, timeout: TimeSpan.FromSeconds(620)))
                review = await PostAsync<CanonicalArticleReviewResponse>(client,
                    "/Services/AcademicPerformance/V1/ReviewCanonicalArticle", new CanonicalArticleReviewRequest
                    {
                        PersonelId = ServiceAcceptanceHost.SubjectId,
                        CanonicalWorkId = works.AdamCanonicalWorkId,
                        Language = "tr",
                        ForceRegeneration = false
                    });
            Require(!review.Reused && !review.IsStale && review.StaleReasons.Count == 0 &&
                review.Report.Reviews.Select(value => value.Role).SequenceEqual(
                    new[] { "method", "quantitative", "claim_evidence", "teaching" }) &&
                review.Report.Coverage.ProcessedRoles == 4 && review.Report.Coverage.TotalRoles == 4 &&
                review.Report.Model.Split(',').All(value => value == ServiceAcceptanceBudget.RequiredModel) &&
                review.Report.Verification.Model.Split(',').All(value => value == ServiceAcceptanceBudget.RequiredModel) &&
                budget.Snapshot().Calls > callsBeforeReview,
                "The Adam four-role specialist review was not a fresh exact-model result.");
            result["adamReview"] = Node(review);
            await PhaseAsync("fresh-adam-specialist-review-complete", result, artifacts, budget);

            heartbeat.Set("publication-metrics-refresh-read");
            ResearcherPublicationMetricsStatusResponse metrics;
            await using (WebApplication metricsWorker = await ServiceAcceptanceHost.StartCollectorAsync(
                root, database, CollectorUrl, AnalysisUrl, "metrics", replay))
            using (HttpClient client = CollectorClient())
            {
                _ = await PostAsync<ResearcherPublicationMetricsStatusResponse>(client,
                    "/Services/AcademicPerformance/V1/RefreshResearcherPublicationMetrics",
                    new ResearcherPublicationMetricsRequest { PersonelId = ServiceAcceptanceHost.SubjectId },
                    HttpStatusCode.Accepted);
                metrics = await PollMetricsAsync(client);
            }
            Require(metrics.Status == "Current" && !metrics.IsStale && metrics.SnapshotId.HasValue &&
                metrics.Data is not null && metrics.RequestedRevision == metrics.ComputedRevision &&
                metrics.Data.CanonicalWorkCount == 2,
                "Publication metrics were not a current two-work snapshot.");
            result["publicationMetrics"] = Node(metrics);
            await PhaseAsync("publication-metrics-current", result, artifacts, budget);

            heartbeat.Set("faculty-context-and-hr-dossier");
            FacultyAssistantContextResponse savedContext;
            FacultyAssistantContextResponse readContext;
            HrEvidenceDossierResponse createdDossier;
            HrEvidenceDossierResponse readDossier;
            await using (WebApplication idle = await ServiceAcceptanceHost.StartCollectorAsync(
                root, database, CollectorUrl, AnalysisUrl, "idle", replay))
            using (HttpClient client = CollectorClient(authenticated: true))
            {
                savedContext = await PostAsync<FacultyAssistantContextResponse>(client,
                    "/Services/AcademicPerformance/V1/SaveFacultyAssistantContext",
                    new SaveFacultyAssistantContextRequest
                    {
                        PersonelId = ServiceAcceptanceHost.SubjectId,
                        ExpectedVersion = 0,
                        Context = new()
                        {
                            Language = "tr",
                            ResearchGoals = ["Yöntem karşılaştırması"],
                            Courses = ["Makine öğrenmesi"],
                            TeachingAudience = "Lisansüstü",
                            Preferences = PrivateSentinel
                        }
                    });
                readContext = await PostAsync<FacultyAssistantContextResponse>(client,
                    "/Services/AcademicPerformance/V1/GetFacultyAssistantContext",
                    new GetFacultyAssistantContextRequest
                    {
                        PersonelId = ServiceAcceptanceHost.SubjectId,
                        Version = savedContext.Version
                    });
                createdDossier = await PostAsync<HrEvidenceDossierResponse>(client,
                    "/Services/AcademicPerformance/V1/CreateHrEvidenceDossier",
                    new CreateHrEvidenceDossierRequest
                    {
                        PersonelId = ServiceAcceptanceHost.SubjectId,
                        PublicationMetricSnapshotId = metrics.SnapshotId,
                        CanonicalWorkIds = works.CanonicalWorkIds.ToList(),
                        Language = "tr"
                    });
                readDossier = await PostAsync<HrEvidenceDossierResponse>(client,
                    "/Services/AcademicPerformance/V1/GetHrEvidenceDossier",
                    new GetHrEvidenceDossierRequest
                    {
                        PersonelId = ServiceAcceptanceHost.SubjectId,
                        DossierId = createdDossier.DossierId
                    });
            }
            Require(savedContext.Version == 1 && Equivalent(savedContext, readContext),
                "Faculty private-context versioned save/read parity failed.");
            Require(Equivalent(createdDossier, readDossier),
                "HR dossier typed create/read parity failed.");
            ValidateDossier(createdDossier, works, review.ReviewRunId, PrivateSentinel);
            result["facultyContext"] = Node(new { saved = savedContext, read = readContext });
            result["hrDossier"] = Node(new { created = createdDossier, read = readDossier });
            await PhaseAsync("faculty-context-and-hr-dossier-complete", result, artifacts, budget);

            heartbeat.Set("positive-faculty-methods");
            FacultyRunEvidence methods = await ExecuteFacultyAsync(root, database, replay,
                Guid.NewGuid(), "OwnPaperMethods", MethodsQuery,
                [works.AdamCanonicalWorkId], savedContext.Version);
            result["facultyMethods"] = methods.Json;
            bool methodsMatched = ValidatePositiveFaculty(methods.Completed, "OwnPaperMethods", 3);
            facultyCriteria["methods"] = Node(new
            {
                matched = methodsMatched,
                methods.Completed.Report!.Outcome,
                requestCoverage = methods.Completed.Report.RequestCoverage
            });
            allFacultyCriteriaMatched &= methodsMatched;
            await PhaseAsync("positive-faculty-methods-complete", result, artifacts, budget);

            heartbeat.Set("positive-faculty-teaching");
            FacultyRunEvidence teaching = await ExecuteFacultyAsync(root, database, replay,
                Guid.NewGuid(), "TeachingHelp", TeachingQuery,
                [works.FootballCanonicalWorkId], savedContext.Version);
            result["facultyTeaching"] = teaching.Json;
            bool teachingMatched = ValidatePositiveFaculty(teaching.Completed, "TeachingHelp", 2);
            facultyCriteria["teaching"] = Node(new
            {
                matched = teachingMatched,
                teaching.Completed.Report!.Outcome,
                requestCoverage = teaching.Completed.Report.RequestCoverage
            });
            allFacultyCriteriaMatched &= teachingMatched;
            await PhaseAsync("positive-faculty-teaching-complete", result, artifacts, budget);

            FacultyRunEvidence? issues = null;
            ServiceAcceptanceBudgetSnapshot afterPositive = budget.Snapshot();
            if (!afterPositive.DispatchStopped && afterPositive.UnknownUsageCalls == 0 &&
                afterPositive.Calls < ServiceAcceptanceBudget.MaximumCalls &&
                afterPositive.CommittedSpendUsd < ServiceAcceptanceBudget.MaximumSpendUsd)
            {
                heartbeat.Set("optional-faculty-issues");
                issues = await ExecuteFacultyAsync(root, database, replay,
                    Guid.NewGuid(), "OwnPaperIssues", IssuesQuery,
                    [works.AdamCanonicalWorkId], savedContext.Version);
                result["facultyIssues"] = issues.Json;
                bool issuesMatched = ValidateIssuesFaculty(issues.Completed);
                facultyCriteria["issues"] = Node(new
                {
                    matched = issuesMatched,
                    issues.Completed.Report!.Outcome,
                    requestCoverage = issues.Completed.Report.RequestCoverage
                });
                allFacultyCriteriaMatched &= issuesMatched;
                await PhaseAsync("optional-faculty-issues-complete", result, artifacts, budget);
            }
            else
            {
                result["facultyIssues"] = new JsonObject
                {
                    ["executed"] = false,
                    ["reason"] = "positive runs completed but aggregate budget state was not healthy"
                };
                facultyCriteria["issues"] = Node(new { matched = false, reason = "budget_not_healthy" });
                allFacultyCriteriaMatched = false;
            }

            heartbeat.Set("zero-call-readback-and-idempotency");
            ServiceAcceptanceBudgetSnapshot beforeReadback = budget.Snapshot();
            ReadbackEvidence readbacks;
            await using (WebApplication idle = await ServiceAcceptanceHost.StartCollectorAsync(
                root, database, CollectorUrl, AnalysisUrl, "idle", replay))
            using (HttpClient client = CollectorClient(authenticated: true, timeout: TimeSpan.FromSeconds(620)))
                readbacks = await RunReadbackAndReplayChecksAsync(client, works, review, savedContext,
                    createdDossier, methods, teaching, issues);
            ServiceAcceptanceBudgetSnapshot afterReadback = budget.Snapshot();
            Require(afterReadback.Calls == beforeReadback.Calls &&
                afterReadback.CommittedSpendUsd == beforeReadback.CommittedSpendUsd,
                "A readback, identical replay, or conflict probe dispatched a new Gemini call.");
            result["readbackAndReplay"] = readbacks.Json;
            result["readbackAddedGeminiCalls"] = afterReadback.Calls - beforeReadback.Calls;
            await PhaseAsync("zero-call-readback-and-idempotency-complete", result, artifacts, budget);

            heartbeat.Set("sql-and-citation-audit");
            JsonObject sql = await DeepSqlAuditAsync(database, budget.Snapshot(), works, review,
                methods.Completed, teaching.Completed, issues?.Completed);
            result["sql"] = sql;
            result["providerReplay"] = replay.ToJson();
            result["budget"] = Node(budget.Snapshot());
            await PhaseAsync("sql-and-citation-audit-complete", result, artifacts, budget);

            ServiceAcceptanceBudgetSnapshot finalBudget = budget.Snapshot();
            Require(!finalBudget.DispatchStopped && finalBudget.UnknownUsageCalls == 0 &&
                finalBudget.Calls <= ServiceAcceptanceBudget.MaximumCalls &&
                finalBudget.CommittedSpendUsd <= ServiceAcceptanceBudget.MaximumSpendUsd,
                "The aggregate acceptance ledger is not healthy.");
            if (result["qualificationRegression"] is JsonObject qualification)
                Require(qualification["allCriteriaMatched"]?.GetValue<bool>() == true,
                    "One or more semantic qualification regression criteria did not match.");
            Require(allFacultyCriteriaMatched,
                "One or more fixed faculty request-fulfillment criteria did not match.");
            result["success"] = true;
            result["status"] = "awaiting_root_audit";
            result["cleanup"] = "owned hosts stopped; exact isolated database preserved pending independent root audit";
            result["completedAtUtc"] = DateTimeOffset.UtcNow;
            await analysis.StopAsync();
            await artifacts.WriteResultAsync(result);
            await artifacts.WritePhaseAsync("awaiting-root-audit", new JsonObject
            {
                ["runId"] = Program.RunId,
                ["databaseName"] = databaseName,
                ["success"] = true,
                ["status"] = "awaiting_root_audit"
            }, finalBudget);
            return 0;
        }
        catch (Exception exception)
        {
            ServiceAcceptanceBudgetSnapshot snapshot = budget.Snapshot();
            result["success"] = false;
            result["status"] = "failed_database_preserved";
            result["failure"] = Node(new
            {
                type = exception.GetType().FullName,
                exception.Message,
                exception.StackTrace
            });
            result["budget"] = Node(snapshot);
            result["providerReplay"] = replay.ToJson();
            result["failureSqlSnapshot"] = await TryFailureSqlAuditAsync(database);
            result["cleanup"] = "owned hosts disposed; database, ledger, state, and failure evidence preserved";
            await artifacts.WriteFailureAsync(result);
            await artifacts.WritePhaseAsync("failed-database-preserved", new JsonObject
            {
                ["runId"] = Program.RunId,
                ["databaseName"] = databaseName,
                ["success"] = false,
                ["failureType"] = exception.GetType().FullName,
                ["failureMessage"] = exception.Message
            }, snapshot);
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static async Task PhaseAsync(string phase, JsonObject result,
        FinalAcceptanceArtifacts artifacts, ServiceAcceptanceBudget budget)
    {
        result["phase"] = phase;
        await artifacts.WritePhaseAsync(phase, (JsonObject)result.DeepClone(), budget.Snapshot());
    }

    private static HttpClient CollectorClient(bool authenticated = false, TimeSpan? timeout = null)
    {
        HttpClient client = new()
        {
            BaseAddress = new(CollectorUrl),
            Timeout = timeout ?? TimeSpan.FromSeconds(340)
        };
        if (authenticated)
            client.DefaultRequestHeaders.Add(ServiceAcceptanceHost.Header, ServiceAcceptanceHost.HeaderValue);
        return client;
    }

    private static async Task<BulkCollectionStatusResponse> PollBulkAsync(HttpClient client, Guid batchId)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMinutes(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            BulkCollectionStatusResponse status = await PostAsync<BulkCollectionStatusResponse>(client,
                "/Services/AcademicPerformance/V1/Bulk/Status",
                new BulkCollectionStatusRequest { BatchId = batchId });
            if (status.IsComplete) return status;
            await Task.Delay(250);
        }
        throw new TimeoutException("Bulk collection did not become terminal within two minutes.");
    }

    private static async Task<WorkIdentity> ResolveWorksAsync(string database)
    {
        await using AcademicDbContext db = FinalAcceptanceDatabase.Open(database);
        var candidates = await db.CanonicalResearcherWorks.AsNoTracking()
            .Where(value => value.PersonelId == ServiceAcceptanceHost.SubjectId)
            .Select(value => new
            {
                value.CanonicalWorkId,
                value.CanonicalWork!.NormalizedDoi,
                Observations = value.CanonicalWork.Observations.OrderBy(item => item.Id)
                    .Select(item => new { item.AcademicWorkId, item.TitleObserved }).ToList()
            }).ToListAsync();
        Require(candidates.Count == 2, "Bulk collection did not create exactly two canonical works.");
        var adam = candidates.Single(value => value.NormalizedDoi == AdamDoi &&
            value.Observations.Any(item => item.TitleObserved == AdamTitle));
        var football = candidates.Single(value => value.NormalizedDoi == FootballDoi &&
            value.Observations.Any(item => item.TitleObserved == FootballTitle));
        int adamAcademicWorkId = adam.Observations.Single(item => item.TitleObserved == AdamTitle).AcademicWorkId;
        int footballAcademicWorkId = football.Observations.Single(item => item.TitleObserved == FootballTitle).AcademicWorkId;
        int[] ids = [adam.CanonicalWorkId, football.CanonicalWorkId];
        int pending = await db.ArticleSummaryAutomationJobs.AsNoTracking().CountAsync(value =>
            ids.Contains(value.CanonicalWorkId) && value.Status == ArticleSummaryAutomationJobStatus.Pending);
        Require(pending == 2, "The two canonical works were not both queued for automatic summaries.");
        return new(adam.CanonicalWorkId, adamAcademicWorkId,
            football.CanonicalWorkId, footballAcademicWorkId);
    }

    private static async Task WaitForTwoSequentialSummariesAsync(string database, int[] canonicalWorkIds)
    {
        HashSet<int> completed = [];
        DateTimeOffset lifecycleDeadline = DateTimeOffset.UtcNow.AddMinutes(24);
        while (DateTimeOffset.UtcNow < lifecycleDeadline)
        {
            await using AcademicDbContext db = FinalAcceptanceDatabase.Open(database);
            var states = await db.ArticleSummaryAutomationJobs.AsNoTracking()
                .Where(value => canonicalWorkIds.Contains(value.CanonicalWorkId))
                .Select(value => new { value.CanonicalWorkId, value.Status, value.Attempts,
                    value.StartedAt, value.CompletedAt, value.LastOutcomeCode })
                .ToListAsync();
            Require(states.Count == 2, "One of the two automatic summary jobs disappeared.");
            foreach (var state in states)
            {
                if (state.Status == ArticleSummaryAutomationJobStatus.Failed)
                    throw new InvalidOperationException(
                        $"Summary {state.CanonicalWorkId} failed terminally: {state.LastOutcomeCode}");
                if (state.Status == ArticleSummaryAutomationJobStatus.Succeeded)
                {
                    completed.Add(state.CanonicalWorkId);
                    continue;
                }
                if (state.StartedAt.HasValue && DateTimeOffset.UtcNow -
                    new DateTimeOffset(DateTime.SpecifyKind(state.StartedAt.Value, DateTimeKind.Utc)) >
                    TimeSpan.FromMinutes(11))
                    throw new TimeoutException(
                        $"Summary {state.CanonicalWorkId} exceeded its independent eleven-minute window.");
            }
            if (completed.Count == 2) return;
            await Task.Delay(500);
        }
        throw new TimeoutException("The two sequential summaries exceeded the 24-minute lifecycle window.");
    }

    private static async Task<SummaryEvidence> AuditSummariesAsync(
        HttpClient client, string database, WorkIdentity works)
    {
        SavedArticleSummaryResponse adam = await PostAsync<SavedArticleSummaryResponse>(client,
            "/Services/AcademicPerformance/V1/GetArticleSummary",
            new ArticleSummaryRequest
            {
                PersonelID = ServiceAcceptanceHost.SubjectId,
                AcademicWorkId = works.AdamAcademicWorkId,
                Language = "tr"
            });
        SavedArticleSummaryResponse football = await PostAsync<SavedArticleSummaryResponse>(client,
            "/Services/AcademicPerformance/V1/GetArticleSummary",
            new ArticleSummaryRequest
            {
                PersonelID = ServiceAcceptanceHost.SubjectId,
                AcademicWorkId = works.FootballAcademicWorkId,
                Language = "tr"
            });
        await using AcademicDbContext db = FinalAcceptanceDatabase.Open(database);
        var rows = await db.CanonicalArticleAnalysisRuns.AsNoTracking()
            .Include(value => value.SavedArticleSummary)
            .Include(value => value.ArticleSourceSnapshot)!.ThenInclude(value => value!.Pages)
            .Include(value => value.ArticleSourceSnapshot)!.ThenInclude(value => value!.Spans)
            .Include(value => value.Claims).ThenInclude(value => value.Evidence)
                .ThenInclude(value => value.ArticleSourceSpan)
            .Where(value => works.CanonicalWorkIds.Contains(value.CanonicalWorkId))
            .OrderBy(value => value.Id).AsSplitQuery().ToListAsync();
        Require(rows.Count == 2, "Exactly two canonical summary runs were not persisted.");
        IReadOnlyDictionary<long, AnalysisFreshnessResult> freshness =
            await AnalysisFreshnessEvaluator.EvaluateAsync(db, rows.Select(value => value.Id).ToArray(),
                new ArticleSummaryAutomationOptions().PolicyVersion, CancellationToken.None);
        Require(freshness.Count == 2 && freshness.Values.All(value =>
                value.Status == AnalysisFreshnessStatus.Current && value.Reasons.Count == 0),
            "Fresh automatic summaries were not classified Current.");
        foreach (CanonicalArticleAnalysisRun row in rows)
        {
            SavedArticleSummaryResponse api = row.CanonicalWorkId == works.AdamCanonicalWorkId ? adam : football;
            Require(row.SavedArticleSummary is not null && EquivalentJson<ArticleSummaryReport>(
                    row.SavedArticleSummary.ReportJson, api.Report),
                "A typed summary API report differs from its persisted ReportJson.");
            Require(row.Language == "tr" && row.ArticleSourceSnapshot?.SourceKind == "pdf" &&
                row.Model == ServiceAcceptanceBudget.RequiredModel &&
                row.VerificationModel == ServiceAcceptanceBudget.RequiredModel,
                "A fresh Turkish PDF summary did not use the exact released models.");
            ValidateSummaryCitations(row, api.Report);
        }
        string[] hashes = rows.Select(value => value.ArticleSourceSnapshot!.ExtractedTextHash)
            .Order(StringComparer.Ordinal).ToArray();
        Require(hashes.SequenceEqual(new[]
            {
                "c2f7f4ac5265813d06ac7f3776abc9b7dcdfe0c21efc1b943fc4059afab950ab",
                "ef1663dd32671bce737af77d0cfd0777fd8ebd95bb86f5d0ff2b4aa457675fa3"
            }), "Extracted PDF source hashes differ from the reviewed fixtures.");
        JsonNode json = Node(new
        {
            typedApi = new { adam, football },
            currentFreshness = freshness.Values.OrderBy(value => value.AnalysisRunId),
            persisted = rows.Select(value => new
            {
                value.Id,
                value.CanonicalWorkId,
                value.AnalyzedAt,
                value.PolicyVersion,
                value.Model,
                value.VerificationModel,
                value.SavedArticleSummary!.SnapshotJson,
                value.SavedArticleSummary.ReportJson,
                source = new
                {
                    value.ArticleSourceSnapshotId,
                    value.ArticleSourceSnapshot!.SourceKind,
                    value.ArticleSourceSnapshot.ExtractionVersion,
                    value.ArticleSourceSnapshot.ExtractedTextHash,
                    pages = value.ArticleSourceSnapshot.Pages.OrderBy(page => page.Ordinal)
                        .Select(page => new { page.Ordinal, page.PageNumber, page.Text }),
                    spans = value.ArticleSourceSnapshot.Spans.OrderBy(span => span.Ordinal)
                        .Select(span => new { span.Id, span.Ordinal, span.SourceId, span.PageNumber,
                            span.StartOffset, span.EndOffset, span.Text })
                }
            })
        });
        return new(adam, football, json);
    }

    private static void ValidateSummaryCitations(CanonicalArticleAnalysisRun row,
        ArticleSummaryReport report)
    {
        ArticleSourceSnapshot source = row.ArticleSourceSnapshot ??
            throw new InvalidOperationException("A summary source snapshot is absent.");
        ValidateSpanCatalog(source);
        IReadOnlyList<(string Section, int SectionOrder, IReadOnlyList<ArticleClaim> Claims)> sections =
        [
            ("Purpose", 0, report.Sections.Purpose),
            ("Methods", 1, report.Sections.Methods),
            ("Data", 2, report.Sections.Data),
            ("Findings", 3, report.Sections.Findings),
            ("Limitations", 4, report.Sections.Limitations)
        ];
        Dictionary<string, ArticleSourceSpanSnapshot> spans = source.Spans.ToDictionary(
            value => value.SourceId, StringComparer.Ordinal);
        foreach ((string section, int sectionOrder, IReadOnlyList<ArticleClaim> claims) in sections)
        {
            for (int ordinal = 0; ordinal < claims.Count; ordinal++)
            {
                ArticleClaim claim = claims[ordinal];
                CanonicalArticleClaim persisted = row.Claims.Single(value =>
                    value.Section == section && value.SectionOrder == sectionOrder &&
                    value.Ordinal == ordinal);
                Require(persisted.Text == claim.Text && persisted.ExternalClaimId == claim.ClaimId &&
                    persisted.Evidence.Count == claim.Evidence.Count,
                    "A typed summary claim differs from its persisted SQL claim.");
                for (int evidenceOrdinal = 0; evidenceOrdinal < claim.Evidence.Count; evidenceOrdinal++)
                {
                    ArticleEvidence evidence = claim.Evidence[evidenceOrdinal];
                    CanonicalArticleClaimEvidence link = persisted.Evidence.Single(value =>
                        value.Ordinal == evidenceOrdinal);
                    Require(evidence.SourceId is not null &&
                        spans.TryGetValue(evidence.SourceId, out ArticleSourceSpanSnapshot? span) &&
                        link.ArticleSourceSpanId == span.Id &&
                        span.PageNumber == evidence.PageNumber && span.StartOffset == evidence.StartOffset &&
                        span.EndOffset == evidence.EndOffset && span.Text == evidence.Quote,
                        "A summary citation is not an exact typed API/SQL source-span match.");
                }
            }
        }
    }

    private static async Task<ResearcherPublicationMetricsStatusResponse> PollMetricsAsync(HttpClient client)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMinutes(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            ResearcherPublicationMetricsStatusResponse value =
                await PostAsync<ResearcherPublicationMetricsStatusResponse>(client,
                    "/Services/AcademicPerformance/V1/GetResearcherPublicationMetrics",
                    new ResearcherPublicationMetricsRequest
                    {
                        PersonelId = ServiceAcceptanceHost.SubjectId
                    }, HttpStatusCode.OK, HttpStatusCode.Accepted);
            if (value.Status == "Current" && value.Data is not null) return value;
            if (value.Status == "Failed")
                throw new InvalidOperationException(
                    $"Publication metrics failed: {value.RefreshOutcome.Code}: {value.RefreshOutcome.Message}");
            await Task.Delay(250);
        }
        throw new TimeoutException("Publication metrics did not become Current within two minutes.");
    }

    private static void ValidateDossier(HrEvidenceDossierResponse dossier, WorkIdentity works,
        long adamReviewRunId, string privateSentinel)
    {
        Require(dossier.Dossier.PublicationMetrics is { IsStale: false } &&
            dossier.Dossier.PublicationMetrics.StaleReasons.Count == 0,
            "The HR dossier did not pin current publication metrics.");
        HrDossierWork adam = dossier.Dossier.Works.Single(value =>
            value.CanonicalWorkId == works.AdamCanonicalWorkId);
        HrDossierWork football = dossier.Dossier.Works.Single(value =>
            value.CanonicalWorkId == works.FootballCanonicalWorkId);
        Require(adam.Reviews.Count > 0 && adam.Reviews.All(value =>
                value.ReviewRunId == adamReviewRunId && !value.IsStale &&
                value.AnalysisFreshnessStatus == AnalysisFreshnessStatus.Current &&
                (value.AnalysisFreshnessReasons?.Count ?? 0) == 0),
            "The HR dossier did not contain the current saved Adam review evidence.");
        Require(football.Reviews.Count == 0,
            "The football work must honestly retain an empty review list.");
        Require(!JsonSerializer.Serialize(dossier, JsonOptions).Contains(privateSentinel,
                StringComparison.Ordinal),
            "Private faculty context leaked into the HR dossier.");
    }

    private static async Task<FacultyRunEvidence> ExecuteFacultyAsync(string root, string database,
        ReplayAudit replay, Guid clientRequestId, string mode, string query, int[] workIds,
        int contextVersion)
    {
        StartFacultyAssistantRequest request = new()
        {
            PersonelId = ServiceAcceptanceHost.SubjectId,
            ClientRequestId = clientRequestId,
            Mode = mode,
            Language = "tr",
            Query = query,
            CanonicalWorkIds = workIds.ToList(),
            Take = 10,
            ContextVersion = contextVersion
        };
        FacultyAssistantRunResponse queued;
        await using (WebApplication idle = await ServiceAcceptanceHost.StartCollectorAsync(
            root, database, CollectorUrl, AnalysisUrl, "idle", replay))
        using (HttpClient client = CollectorClient(authenticated: true))
            queued = await PostAsync<FacultyAssistantRunResponse>(client,
                "/Services/AcademicPerformance/V1/StartFacultyAssistant", request,
                HttpStatusCode.Accepted);
        Require(!queued.Reused && queued.Status == "Pending",
            $"{mode} did not enqueue a fresh pending faculty run.");
        FacultyAssistantRunResponse completed;
        await using (WebApplication worker = await ServiceAcceptanceHost.StartCollectorAsync(
            root, database, CollectorUrl, AnalysisUrl, "faculty", replay))
        using (HttpClient client = CollectorClient(authenticated: true))
            completed = await PollFacultyAsync(client, queued.RunId);
        return new(clientRequestId, request, queued, completed,
            Node(new { query, request, queued, completed }));
    }

    private static async Task<FacultyAssistantRunResponse> PollFacultyAsync(HttpClient client, Guid runId)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMinutes(7);
        while (DateTimeOffset.UtcNow < deadline)
        {
            FacultyAssistantRunResponse value = await PostAsync<FacultyAssistantRunResponse>(client,
                "/Services/AcademicPerformance/V1/GetFacultyAssistantRun",
                new GetFacultyAssistantRunRequest
                {
                    PersonelId = ServiceAcceptanceHost.SubjectId,
                    RunId = runId
                });
            if (value.Status == "Completed") return value;
            if (value.Status is "Failed" or "Interrupted")
                throw new InvalidOperationException(
                    $"Faculty run {runId} ended {value.Status}: {value.ErrorCode}: {value.ErrorMessage}");
            await Task.Delay(500);
        }
        throw new TimeoutException($"Faculty run {runId} did not complete within seven minutes.");
    }

    private static bool ValidatePositiveFaculty(
        FacultyAssistantRunResponse value, string mode, int minimumRequirements)
    {
        FacultyAssistantAnalysisReport report = value.Report ??
            throw new InvalidOperationException($"{mode} completed without a report.");
        FacultyAssistantCoverage coverage = report.Coverage ??
            throw new InvalidOperationException($"{mode} omitted coverage arithmetic.");
        FacultyRequestCoverage requestCoverage = ValidateRequestCoverage(report, minimumRequirements);
        bool verificationValid = coverage.CandidateItems == 0
            ? report.Verification.Status == "not_run" && report.Verification.Model.Length == 0 &&
                report.Verification.PromptVersion.Length == 0
            : report.Verification.Status == "automatically_checked" &&
                report.Verification.Model == ServiceAcceptanceBudget.RequiredModel &&
                report.Verification.PromptVersion == FacultyAssistantVerificationPrompt.Version;
        Require(value.Status == "Completed" && report.Mode == mode && report.Language == "tr" &&
            report.Model == ServiceAcceptanceBudget.RequiredModel &&
            report.PromptVersion == FacultyAssistantPrompt.Version &&
            verificationValid && report.Items.Count == coverage.SupportedItems &&
            coverage.CandidateItems == coverage.SupportedItems + coverage.UnsupportedItems +
                coverage.UncertainItems &&
            coverage.AutomaticallyCheckedItems == coverage.CandidateItems &&
            coverage.OmittedItems == coverage.UnsupportedItems + coverage.UncertainItems &&
            value.Retrieval.Evidence.Count >= 1 &&
            value.Retrieval.StaleAnalysisRunCount == 0 &&
            value.Retrieval.UnknownAnalysisRunCount == 0 &&
            value.Retrieval.CurrentAnalysisRunCount.GetValueOrDefault() > 0 &&
            value.Retrieval.Evidence.All(item =>
                item.PinnedFreshnessStatus == AnalysisFreshnessStatus.Current &&
                item.CurrentFreshnessStatus == AnalysisFreshnessStatus.Current &&
                (item.PinnedFreshnessReasons?.Count ?? 0) == 0 &&
                (item.CurrentFreshnessReasons?.Count ?? 0) == 0),
            $"{mode} did not produce a verified, current-evidence positive result.");
        return requestCoverage.Status == "fulfilled" &&
            requestCoverage.Requirements.Count >= minimumRequirements && coverage.SupportedItems >= 1;
    }

    private static bool ValidateIssuesFaculty(FacultyAssistantRunResponse value)
    {
        FacultyAssistantAnalysisReport report = value.Report ??
            throw new InvalidOperationException("OwnPaperIssues completed without a report.");
        FacultyAssistantCoverage coverage = report.Coverage ??
            throw new InvalidOperationException("OwnPaperIssues omitted coverage arithmetic.");
        FacultyRequestCoverage requestCoverage = ValidateRequestCoverage(report, 2);
        bool verificationValid = coverage.CandidateItems == 0
            ? report.Verification.Status == "not_run" && report.Verification.Model.Length == 0 &&
                report.Verification.PromptVersion.Length == 0
            : report.Verification.Status == "automatically_checked" &&
                report.Verification.Model == ServiceAcceptanceBudget.RequiredModel &&
                report.Verification.PromptVersion == FacultyAssistantVerificationPrompt.Version;
        Require(value.Status == "Completed" && report.Mode == "OwnPaperIssues" &&
            report.Model == ServiceAcceptanceBudget.RequiredModel &&
            report.PromptVersion == FacultyAssistantPrompt.Version &&
            verificationValid &&
            report.Items.Count == coverage.SupportedItems &&
            coverage.CandidateItems == coverage.SupportedItems + coverage.UnsupportedItems +
                coverage.UncertainItems && coverage.AutomaticallyCheckedItems == coverage.CandidateItems &&
            coverage.OmittedItems == coverage.UnsupportedItems + coverage.UncertainItems &&
            value.Retrieval.Evidence.All(item =>
                item.PinnedFreshnessStatus == AnalysisFreshnessStatus.Current &&
                item.CurrentFreshnessStatus == AnalysisFreshnessStatus.Current),
            "The issues result was not a checked, current-evidence positive result.");
        return requestCoverage.Status == "fulfilled" && requestCoverage.Requirements.Count >= 2 &&
            coverage.SupportedItems >= 1;
    }

    private static FacultyRequestCoverage ValidateRequestCoverage(
        FacultyAssistantAnalysisReport report, int minimumRequirements)
    {
        FacultyRequestCoverage coverage = report.RequestCoverage ??
            throw new InvalidOperationException("The fresh faculty report omitted request coverage.");
        Require(coverage.Status is "fulfilled" or "partial" or "unanswered" or "unavailable" &&
            !string.IsNullOrWhiteSpace(coverage.Limitation) && coverage.Limitation.Length <= 500,
            "The faculty request coverage status or limitation is invalid.");
        if (report.Items.Count == 0)
        {
            Require(coverage.Status == "unanswered" && coverage.Requirements.Count is >= 1 and <= 8 &&
                coverage.Model.Length == 0 && coverage.PromptVersion.Length == 0 &&
                !coverage.UsesSameModelFamily && report.Outcome == "no_supported_items",
                "Deterministic empty request coverage is inconsistent.");
            for (int index = 0; index < coverage.Requirements.Count; index++)
            {
                FacultyRequestCoverageRequirement requirement = coverage.Requirements[index];
                Require(requirement.RequirementId == $"requirement-{index + 1}" &&
                    !string.IsNullOrWhiteSpace(requirement.Requirement) && requirement.Requirement.Length <= 500 &&
                    !string.IsNullOrWhiteSpace(requirement.Reason) && requirement.Reason.Length <= 500 &&
                    requirement.Status == "unanswered" && requirement.ItemIndexes.Count == 0,
                    "A deterministic empty request coverage requirement is malformed.");
            }
            return coverage;
        }
        if (coverage.Status == "unavailable")
        {
            Require(coverage.Requirements.Count == 0 && coverage.Model.Length == 0 &&
                coverage.PromptVersion.Length == 0 && !coverage.UsesSameModelFamily &&
                report.Outcome == "partial", "Unavailable request coverage is inconsistent.");
            return coverage;
        }
        Require(coverage.Model == ServiceAcceptanceBudget.RequiredModel &&
            coverage.PromptVersion == FacultyRequestCoveragePrompt.Version &&
            coverage.UsesSameModelFamily && coverage.Requirements.Count is >= 1 and <= 8,
            "The faculty request coverage model, prompt, or requirement count is invalid.");
        for (int index = 0; index < coverage.Requirements.Count; index++)
        {
            FacultyRequestCoverageRequirement requirement = coverage.Requirements[index];
            Require(requirement.RequirementId == $"requirement-{index + 1}" &&
                !string.IsNullOrWhiteSpace(requirement.Requirement) && requirement.Requirement.Length <= 500 &&
                !string.IsNullOrWhiteSpace(requirement.Reason) && requirement.Reason.Length <= 500 &&
                requirement.Status is "fulfilled" or "partial" or "unanswered" &&
                requirement.ItemIndexes.All(itemIndex => itemIndex >= 1 && itemIndex <= report.Items.Count) &&
                requirement.ItemIndexes.Distinct().Count() == requirement.ItemIndexes.Count &&
                (requirement.Status == "unanswered") == (requirement.ItemIndexes.Count == 0),
                "A faculty request coverage requirement is malformed.");
        }
        string aggregate = coverage.Requirements.All(value => value.Status == "fulfilled") ? "fulfilled" :
            coverage.Requirements.All(value => value.Status == "unanswered") ? "unanswered" : "partial";
        string expectedOutcome = report.Items.Count == 0 ? "no_supported_items" :
            report.Coverage!.OmittedItems == 0 && coverage.Status == "fulfilled" ? "completed" : "partial";
        Require(coverage.Status == aggregate && report.Outcome == expectedOutcome,
            "The faculty request coverage aggregate or report outcome is inconsistent.");
        return coverage;
    }

    private static async Task<ReadbackEvidence> RunReadbackAndReplayChecksAsync(HttpClient client,
        WorkIdentity works, CanonicalArticleReviewResponse originalReview,
        FacultyAssistantContextResponse context, HrEvidenceDossierResponse dossier,
        params FacultyRunEvidence?[] facultyRuns)
    {
        CanonicalArticleReviewResponse readReview = await PostAsync<CanonicalArticleReviewResponse>(client,
            "/Services/AcademicPerformance/V1/GetCanonicalArticleReview",
            new CanonicalArticleReviewRequest
            {
                PersonelId = ServiceAcceptanceHost.SubjectId,
                CanonicalWorkId = works.AdamCanonicalWorkId,
                Language = "tr"
            });
        CanonicalArticleReviewResponse replayedReview = await PostAsync<CanonicalArticleReviewResponse>(client,
            "/Services/AcademicPerformance/V1/ReviewCanonicalArticle",
            new CanonicalArticleReviewRequest
            {
                PersonelId = ServiceAcceptanceHost.SubjectId,
                CanonicalWorkId = works.AdamCanonicalWorkId,
                Language = "tr",
                ForceRegeneration = false
            });
        Require(readReview.ReviewRunId == originalReview.ReviewRunId &&
            replayedReview.ReviewRunId == originalReview.ReviewRunId && replayedReview.Reused &&
            Equivalent(originalReview.Report, readReview.Report) &&
            Equivalent(originalReview.Report, replayedReview.Report),
            "Article review read/cache replay did not return the identical saved report.");

        FacultyAssistantContextResponse readContext = await PostAsync<FacultyAssistantContextResponse>(client,
            "/Services/AcademicPerformance/V1/GetFacultyAssistantContext",
            new GetFacultyAssistantContextRequest
            {
                PersonelId = ServiceAcceptanceHost.SubjectId,
                Version = context.Version
            });
        HrEvidenceDossierResponse readDossier = await PostAsync<HrEvidenceDossierResponse>(client,
            "/Services/AcademicPerformance/V1/GetHrEvidenceDossier",
            new GetHrEvidenceDossierRequest
            {
                PersonelId = ServiceAcceptanceHost.SubjectId,
                DossierId = dossier.DossierId
            });
        Require(Equivalent(context, readContext) && Equivalent(dossier, readDossier),
            "Final context or dossier readback changed.");

        Guid actionRequestId = Guid.NewGuid();
        AppendHrDossierReviewActionRequest actionRequest = new()
        {
            PersonelId = ServiceAcceptanceHost.SubjectId,
            DossierId = dossier.DossierId,
            ClientRequestId = actionRequestId,
            ActionType = "Opened",
            EvidenceReference = $"review:{originalReview.ReviewRunId}",
            Note = "Final acceptance procedural review opened."
        };
        HrDossierReviewActionResponse createdAction = await PostAsync<HrDossierReviewActionResponse>(client,
            "/Services/AcademicPerformance/V1/AppendHrDossierReviewAction", actionRequest);
        HrDossierReviewActionResponse replayedAction = await PostAsync<HrDossierReviewActionResponse>(client,
            "/Services/AcademicPerformance/V1/AppendHrDossierReviewAction", actionRequest);
        HrDossierReviewActionListResponse actionList = await PostAsync<HrDossierReviewActionListResponse>(client,
            "/Services/AcademicPerformance/V1/ListHrDossierReviewActions",
            new ListHrDossierReviewActionsRequest
            {
                PersonelId = ServiceAcceptanceHost.SubjectId,
                DossierId = dossier.DossierId,
                Skip = 0,
                Take = 100
            });
        Require(!createdAction.Reused && replayedAction.Reused &&
            Equivalent(createdAction.Action, replayedAction.Action) &&
            actionList.TotalCount == 1 && actionList.Actions.Count == 1 &&
            Equivalent(createdAction.Action, actionList.Actions[0]),
            "HR procedural action append/replay/list parity failed.");
        HttpCapture actionConflict = await PostCaptureAsync(client,
            "/Services/AcademicPerformance/V1/AppendHrDossierReviewAction",
            new AppendHrDossierReviewActionRequest
            {
                PersonelId = ServiceAcceptanceHost.SubjectId,
                DossierId = dossier.DossierId,
                ClientRequestId = actionRequestId,
                ActionType = "Opened",
                EvidenceReference = actionRequest.EvidenceReference,
                Note = "Changed content must conflict."
            });
        Require(actionConflict.StatusCode == HttpStatusCode.Conflict,
            "Changed HR action payload with the same request ID did not return HTTP 409.");

        JsonArray faculty = [];
        foreach (FacultyRunEvidence run in facultyRuns.OfType<FacultyRunEvidence>())
        {
            FacultyAssistantRunResponse replayed = await PostAsync<FacultyAssistantRunResponse>(client,
                "/Services/AcademicPerformance/V1/StartFacultyAssistant", run.Request,
                HttpStatusCode.Accepted);
            FacultyAssistantRunResponse read = await PostAsync<FacultyAssistantRunResponse>(client,
                "/Services/AcademicPerformance/V1/GetFacultyAssistantRun",
                new GetFacultyAssistantRunRequest
                {
                    PersonelId = ServiceAcceptanceHost.SubjectId,
                    RunId = run.Completed.RunId
                });
            StartFacultyAssistantRequest changed = CloneFacultyRequest(run.Request);
            changed.Query += " değiştirildi";
            HttpCapture conflict = await PostCaptureAsync(client,
                "/Services/AcademicPerformance/V1/StartFacultyAssistant", changed);
            Require(replayed.Reused && replayed.RunId == run.Completed.RunId &&
                Equivalent(run.Completed.Report, replayed.Report) &&
                Equivalent(run.Completed.Report, read.Report) &&
                conflict.StatusCode == HttpStatusCode.Conflict,
                $"{run.Request.Mode} replay/read/conflict contract failed.");
            faculty.Add(Node(new
            {
                run.Request.Mode,
                replayed,
                read,
                changedPayloadStatus = (int)conflict.StatusCode,
                changedPayloadBody = ParseBody(conflict.Body)
            }));
        }
        JsonNode json = Node(new
        {
            review = new { read = readReview, replayed = replayedReview },
            context = readContext,
            dossier = readDossier,
            hrAction = new
            {
                created = createdAction,
                replayed = replayedAction,
                listed = actionList,
                changedPayloadStatus = (int)actionConflict.StatusCode,
                changedPayloadBody = ParseBody(actionConflict.Body)
            },
            faculty
        });
        return new(json);
    }

    private static StartFacultyAssistantRequest CloneFacultyRequest(StartFacultyAssistantRequest value) => new()
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

    private static async Task<JsonObject> DeepSqlAuditAsync(string database,
        ServiceAcceptanceBudgetSnapshot ledger, WorkIdentity works,
        CanonicalArticleReviewResponse review, params FacultyAssistantRunResponse?[] facultyResponses)
    {
        await using AcademicDbContext db = FinalAcceptanceDatabase.Open(database);
        var reviewRun = await db.CanonicalArticleReviewRuns.AsNoTracking()
            .Include(value => value.ArticleSourceSnapshot)!.ThenInclude(value => value!.Pages)
            .Include(value => value.ArticleSourceSnapshot)!.ThenInclude(value => value!.Spans)
            .Include(value => value.Findings).ThenInclude(value => value.Evidence)
                .ThenInclude(value => value.ArticleSourceSpan)
            .SingleAsync(value => value.Id == review.ReviewRunId);
        Require(EquivalentJson<ArticleReviewReport>(reviewRun.ReportJson, review.Report),
            "The typed review API report differs from persisted ReportJson.");
        ValidateReviewCitations(reviewRun, review.Report);

        var workItems = await db.ArticleReviewWorkItems.AsNoTracking()
            .Include(value => value.Checkpoints)
            .Where(value => value.CanonicalWorkId == works.AdamCanonicalWorkId)
            .OrderBy(value => value.Id).ToListAsync();
        Require(workItems.Count == 1 && workItems[0].Status == "Completed" &&
            workItems[0].Checkpoints.Count(value => value.Stage == ArticleReviewStageKinds.Generation &&
                value.Status == "Completed") == 4 &&
            workItems[0].Checkpoints.All(value => value.Status is "Completed" or "OutputLimit"),
            "The review checkpoint set is incomplete or contains an unsafe terminal state.");

        var contextRows = await db.FacultyAssistantContextVersions.AsNoTracking()
            .OrderBy(value => value.Id).ToListAsync();
        var dossierRows = await db.HrEvidenceDossiers.AsNoTracking()
            .OrderBy(value => value.Id).ToListAsync();
        var actionRows = await db.HrDossierReviewActions.AsNoTracking()
            .OrderBy(value => value.Id).ToListAsync();
        var facultyRows = await db.FacultyAssistantRuns.AsNoTracking()
            .OrderBy(value => value.Id).ToListAsync();
        Require(contextRows.Count == 1 && dossierRows.Count == 1 && actionRows.Count == 1 &&
            facultyRows.Count == facultyResponses.OfType<FacultyAssistantRunResponse>().Count(),
            "Persisted context/dossier/action/faculty row counts are not exact.");
        foreach (FacultyAssistantRunResponse api in facultyResponses.OfType<FacultyAssistantRunResponse>())
        {
            var row = facultyRows.Single(value => value.RunId == api.RunId);
            Require(row.ReportJson is not null &&
                EquivalentJson<FacultyAssistantAnalysisReport>(row.ReportJson, api.Report!),
                "A typed faculty API report differs from persisted ReportJson.");
            await ValidateFacultyCitationsAsync(db, row.AuthorizedInputJson, row.ReportJson, api);
        }

        UsageAudit usage = await ReadUsageAuditAsync(database);
        Require(usage.Rows.Count == ledger.Calls && usage.Unknown == 0 &&
            usage.KnownSpend == ledger.KnownActualSpendUsd &&
            ledger.CommittedSpendUsd == ledger.KnownActualSpendUsd,
            "SQL Gemini usage rows do not reconcile exactly with the aggregate ledger.");
        HashSet<Guid> usageAttempts = usage.Rows.Select(value => value.AttemptId).ToHashSet();
        Require(workItems.SelectMany(value => value.Checkpoints)
                .Where(value => value.AttemptId.HasValue)
                .All(value => usageAttempts.Contains(value.AttemptId!.Value)),
            "A review checkpoint attempt ID is absent from the SQL usage ledger.");

        return new JsonObject
        {
            ["usage"] = usage.Json,
            ["ledgerReconciled"] = true,
            ["review"] = Node(new
            {
                reviewRun.Id,
                reviewRun.CanonicalWorkId,
                reviewRun.BaseAnalysisRunId,
                reviewRun.ArticleSourceSnapshotId,
                reviewRun.SettingsFingerprint,
                reviewRun.Model,
                reviewRun.VerificationModel,
                reviewRun.ReportJson,
                source = new
                {
                    reviewRun.ArticleSourceSnapshot!.ExtractedTextHash,
                    pages = reviewRun.ArticleSourceSnapshot.Pages.OrderBy(value => value.Ordinal)
                        .Select(value => new { value.Ordinal, value.PageNumber, value.Text }),
                    spans = reviewRun.ArticleSourceSnapshot.Spans.OrderBy(value => value.Ordinal)
                        .Select(value => new { value.Id, value.Ordinal, value.SourceId, value.PageNumber,
                            value.StartOffset, value.EndOffset, value.Text })
                },
                workItems = workItems.Select(value => new
                {
                    value.Id,
                    value.WorkKey,
                    value.Status,
                    value.MaximumCalls,
                    value.MaximumSpendUsd,
                    value.ConfigurationJson,
                    value.SourceSnapshotJson,
                    checkpoints = value.Checkpoints.OrderBy(checkpoint => checkpoint.Id).Select(checkpoint => new
                    {
                        checkpoint.Id,
                        checkpoint.Stage,
                        checkpoint.Role,
                        checkpoint.BatchKey,
                        checkpoint.ParentBatchKey,
                        checkpoint.Ordinal,
                        checkpoint.Status,
                        checkpoint.AttemptId,
                        checkpoint.RequestFingerprint,
                        checkpoint.ReservedCostUsd,
                        checkpoint.ActualCostUsd,
                        checkpoint.PricingVersion,
                        checkpoint.ErrorCode,
                        checkpoint.RequestJson,
                        checkpoint.ResultJson
                    })
                })
            }),
            ["facultyContextRows"] = Node(contextRows.Select(value => new
            {
                value.Id, value.PersonelId, value.Version, value.ContextFingerprint,
                value.ContextJson, value.CreatedByActorId, value.CreatedAt
            })),
            ["dossierRows"] = Node(dossierRows.Select(value => new
            {
                value.Id, value.PersonelId, value.CreatedByActorId, value.CreatedAt,
                value.PolicyVersion, value.PublicationMetricSnapshotId, value.InputFingerprint,
                value.InputManifestJson, value.DossierJson
            })),
            ["dossierActionRows"] = Node(actionRows.Select(value => new
            {
                value.Id, value.DossierId, value.ActorAuditId, value.ClientRequestId,
                value.ActionType, value.EvidenceReference, value.Note, value.RecordedAt
            })),
            ["facultyRows"] = Node(facultyRows.Select(value => new
            {
                value.Id, value.RunId, value.ClientRequestId, value.Mode, value.Status,
                value.AttemptCount, value.AttemptToken, value.RequestJson,
                value.RetrievalManifestJson, value.AuthorizedInputJson, value.ReportJson,
                value.ErrorCode, value.ErrorMessage
            }))
        };
    }

    private static void ValidateReviewCitations(CanonicalArticleReviewRun persisted,
        ArticleReviewReport report)
    {
        ArticleSourceSnapshot source = persisted.ArticleSourceSnapshot ??
            throw new InvalidOperationException("The review source snapshot is absent.");
        ValidateSpanCatalog(source);
        Dictionary<string, ArticleSourceSpanSnapshot> spans = source.Spans
            .ToDictionary(value => value.SourceId, StringComparer.Ordinal);
        foreach (ArticleReviewEvidence evidence in report.Reviews.SelectMany(value => value.Findings)
                     .SelectMany(value => value.Evidence))
        {
            Require(spans.TryGetValue(evidence.SourceId, out ArticleSourceSpanSnapshot? span) &&
                span.PageNumber == evidence.PageNumber && span.StartOffset == evidence.StartOffset &&
                span.EndOffset == evidence.EndOffset && span.Text == evidence.Quote &&
                span.EndOffset - span.StartOffset == span.Text.Length,
                "A review citation is not an exact UTF-16 immutable source-span match.");
        }
    }

    private static void ValidateSpanCatalog(ArticleSourceSnapshot source)
    {
        foreach (ArticleSourceSpanSnapshot span in source.Spans)
        {
            ArticleSourcePageSnapshot page = source.Pages.Single(value =>
                value.PageNumber == span.PageNumber);
            Require(span.ArticleSourceSnapshotId == source.Id &&
                page.ArticleSourceSnapshotId == source.Id && span.StartOffset >= 0 &&
                span.EndOffset > span.StartOffset && span.EndOffset <= page.Text.Length &&
                span.Text == page.Text.Substring(span.StartOffset, span.EndOffset - span.StartOffset),
                "An immutable source span does not match its exact UTF-16 page slice.");
        }
    }

    private static async Task ValidateFacultyCitationsAsync(AcademicDbContext db,
        string? inputJson, string? reportJson, FacultyAssistantRunResponse api)
    {
        FacultyAssistantAnalysisRequest input = JsonSerializer.Deserialize<FacultyAssistantAnalysisRequest>(
            inputJson ?? throw new InvalidOperationException("Faculty authorized input is absent."), JsonOptions)
            ?? throw new JsonException("Faculty authorized input is invalid.");
        FacultyAssistantAnalysisReport report = JsonSerializer.Deserialize<FacultyAssistantAnalysisReport>(
            reportJson ?? throw new InvalidOperationException("Faculty report is absent."), JsonOptions)
            ?? throw new JsonException("Faculty report is invalid.");
        Dictionary<string, FacultyAssistantEvidence> evidence = input.Evidence.ToDictionary(
            value => value.EvidenceId, StringComparer.Ordinal);
        long[] spanIds = evidence.Values.Select(value => value.SourceSpanId).Distinct().ToArray();
        Dictionary<long, ArticleSourceSpanSnapshot> spans = await db.ArticleSourceSpans.AsNoTracking()
            .Where(value => spanIds.Contains(value.Id)).ToDictionaryAsync(value => value.Id);
        long[] sourceIds = spans.Values.Select(value => value.ArticleSourceSnapshotId).Distinct().ToArray();
        var pages = await db.ArticleSourcePages.AsNoTracking()
            .Where(value => sourceIds.Contains(value.ArticleSourceSnapshotId)).ToListAsync();
        Dictionary<string, FacultyAssistantRetrievedEvidence> retrieved = api.Retrieval.Evidence
            .ToDictionary(value => value.EvidenceId, StringComparer.Ordinal);
        foreach (FacultyAssistantEvidence item in evidence.Values)
        {
            Require(spans.TryGetValue(item.SourceSpanId, out ArticleSourceSpanSnapshot? span) &&
                retrieved.TryGetValue(item.EvidenceId, out FacultyAssistantRetrievedEvidence? apiItem) &&
                apiItem.ArticleSourceSpanId == item.SourceSpanId && apiItem.PageNumber == item.PageNumber &&
                apiItem.SourceId == item.SourceId && apiItem.StartOffset == item.StartOffset &&
                apiItem.EndOffset == item.EndOffset && span.PageNumber == item.PageNumber &&
                span.SourceId == item.SourceId && span.StartOffset == item.StartOffset &&
                span.EndOffset == item.EndOffset && span.Text == item.ExactText &&
                pages.Single(value => value.ArticleSourceSnapshotId == span.ArticleSourceSnapshotId &&
                    value.PageNumber == span.PageNumber).Text.Substring(span.StartOffset,
                    span.EndOffset - span.StartOffset) == item.ExactText,
                "Faculty authorized/retrieved evidence differs from its exact UTF-16 SQL page slice.");
        }
        foreach (FacultyAssistantCitation citation in report.Items.SelectMany(value => value.Citations))
        {
            Require(evidence.TryGetValue(citation.EvidenceId, out FacultyAssistantEvidence? item) &&
                spans.TryGetValue(item.SourceSpanId, out ArticleSourceSpanSnapshot? span) &&
                citation.ExactQuote == item.ExactText && item.ExactText == span.Text &&
                item.SourceId == span.SourceId && item.StartOffset == span.StartOffset &&
                item.EndOffset == span.EndOffset && item.EndOffset - item.StartOffset == item.ExactText.Length,
                "A faculty citation is not an exact UTF-16 immutable source-span match.");
        }
        Require(Equivalent(report, api.Report),
            "Faculty SQL report and typed API report differ.");
    }

    private static async Task<UsageAudit> ReadUsageAuditAsync(string database)
    {
        await using SqlConnection connection = new(database);
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT AttemptId,StartedAt,CompletedAt,RequestedModel,ReturnedModel,Outcome,HttpStatus,
                PromptTokenCount,CachedTokenCount,CandidateTokenCount,ThoughtTokenCount,TotalTokenCount,
                PricingVersion,EstimatedUsd
            FROM [analysis].[GeminiUsageAttempts]
            ORDER BY StartedAt,AttemptId
            """;
        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        List<UsageRow> rows = [];
        while (await reader.ReadAsync())
            rows.Add(new(reader.GetGuid(0), reader.GetDateTime(1),
                reader.IsDBNull(2) ? null : reader.GetDateTime(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetInt64(7),
                reader.IsDBNull(8) ? null : reader.GetInt64(8),
                reader.IsDBNull(9) ? null : reader.GetInt64(9),
                reader.IsDBNull(10) ? null : reader.GetInt64(10),
                reader.IsDBNull(11) ? null : reader.GetInt64(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetDecimal(13)));
        int unknown = rows.Count(value => value.CompletedAt is null || value.EstimatedUsd is null ||
            value.ReturnedModel != ServiceAcceptanceBudget.RequiredModel ||
            string.IsNullOrWhiteSpace(value.PricingVersion));
        decimal knownSpend = rows.Where(value => value.EstimatedUsd.HasValue)
            .Sum(value => value.EstimatedUsd!.Value);
        return new(rows, unknown, knownSpend, Node(new
        {
            calls = rows.Count,
            unknown,
            knownSpend,
            rows
        }));
    }

    private static async Task<JsonNode> TryFailureSqlAuditAsync(string database)
    {
        try
        {
            await using SqlConnection connection = new(database);
            await connection.OpenAsync();
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = """
                IF OBJECT_ID(N'[analysis].[ArticleReviewWorkItems]') IS NOT NULL
                    SELECT (SELECT Id,WorkKey,Status,MaximumCalls,MaximumSpendUsd,ConfigurationJson,
                        SourceSnapshotJson FROM [analysis].[ArticleReviewWorkItems] ORDER BY Id FOR JSON PATH) WorkItems,
                        (SELECT Id,ArticleReviewWorkItemId,Stage,Role,BatchKey,ParentBatchKey,Ordinal,Status,
                            AttemptId,RequestFingerprint,ReservedCostUsd,ActualCostUsd,PricingVersion,ErrorCode,
                            RequestJson,ResultJson FROM [analysis].[ArticleReviewStageCheckpoints]
                            ORDER BY Id FOR JSON PATH) Checkpoints,
                        (SELECT AttemptId,StartedAt,CompletedAt,RequestedModel,ReturnedModel,Outcome,HttpStatus,
                            PromptTokenCount,CachedTokenCount,CandidateTokenCount,ThoughtTokenCount,TotalTokenCount,
                            PricingVersion,EstimatedUsd FROM [analysis].[GeminiUsageAttempts]
                            ORDER BY StartedAt,AttemptId FOR JSON PATH) Usage
                ELSE SELECT N'[]',N'[]',N'[]'
                """;
            await using SqlDataReader reader = await command.ExecuteReaderAsync();
            await reader.ReadAsync();
            return new JsonObject
            {
                ["workItems"] = ParseBody(reader.IsDBNull(0) ? "[]" : reader.GetString(0)),
                ["checkpoints"] = ParseBody(reader.IsDBNull(1) ? "[]" : reader.GetString(1)),
                ["usage"] = ParseBody(reader.IsDBNull(2) ? "[]" : reader.GetString(2))
            };
        }
        catch (Exception exception)
        {
            return Node(new { unavailable = true, exception.Message });
        }
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string path, object body,
        params HttpStatusCode[] accepted)
    {
        HttpCapture capture = await PostCaptureAsync(client, path, body);
        HttpStatusCode[] expected = accepted.Length == 0 ? [HttpStatusCode.OK] : accepted;
        if (!expected.Contains(capture.StatusCode))
            throw new AcceptanceHttpException(path, capture.StatusCode, capture.Body);
        return JsonSerializer.Deserialize<T>(capture.Body, JsonOptions) ??
            throw new JsonException($"{path} returned an empty typed response.");
    }

    private static async Task<HttpCapture> PostCaptureAsync(HttpClient client, string path, object body)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(path, body, JsonOptions);
        string text = await response.Content.ReadAsStringAsync();
        return new(response.StatusCode, text);
    }

    private static bool Equivalent<T>(T left, T right) =>
        JsonNode.DeepEquals(Node(left), Node(right));

    private static bool EquivalentJson<T>(string json, T typed)
    {
        T persisted = JsonSerializer.Deserialize<T>(json, JsonOptions) ??
            throw new JsonException($"Persisted {typeof(T).Name} JSON is invalid.");
        return Equivalent(persisted, typed);
    }

    private static JsonNode Node<T>(T value) =>
        JsonSerializer.SerializeToNode(value, JsonOptions) ?? JsonValue.Create((string?)null)!;

    private static JsonNode ParseBody(string value)
    {
        try { return JsonNode.Parse(value) ?? JsonValue.Create(value)!; }
        catch (JsonException) { return JsonValue.Create(value)!; }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record WorkIdentity(int AdamCanonicalWorkId, int AdamAcademicWorkId,
        int FootballCanonicalWorkId, int FootballAcademicWorkId)
    {
        public int[] CanonicalWorkIds => [AdamCanonicalWorkId, FootballCanonicalWorkId];
    }

    private sealed record SummaryEvidence(SavedArticleSummaryResponse Adam,
        SavedArticleSummaryResponse Football, JsonNode Json);

    private sealed record FacultyRunEvidence(Guid ClientRequestId, StartFacultyAssistantRequest Request,
        FacultyAssistantRunResponse Queued, FacultyAssistantRunResponse Completed, JsonNode Json);

    private sealed record ReadbackEvidence(JsonNode Json);

    private sealed record HttpCapture(HttpStatusCode StatusCode, string Body);

    private sealed record UsageRow(Guid AttemptId, DateTime StartedAt, DateTime? CompletedAt,
        string RequestedModel, string? ReturnedModel, string Outcome, int? HttpStatus,
        long? PromptTokenCount, long? CachedTokenCount, long? CandidateTokenCount,
        long? ThoughtTokenCount, long? TotalTokenCount, string? PricingVersion,
        decimal? EstimatedUsd);

    private sealed record UsageAudit(IReadOnlyList<UsageRow> Rows, int Unknown,
        decimal KnownSpend, JsonNode Json);
}

internal sealed class AcceptanceHttpException(string path, HttpStatusCode statusCode, string responseBody)
    : InvalidOperationException($"{path} returned HTTP {(int)statusCode}: {responseBody}")
{
    public string Path { get; } = path;
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string ResponseBody { get; } = responseBody;
}
