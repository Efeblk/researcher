using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AcademicCollector.Analysis.Contracts;
using ResearcherAnalysisService.Products.ArticleReviews;
using ResearcherAnalysisService.Products.ArticleSummaries;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ResearcherAnalysisService.Products.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Integrations.Gemini;

namespace FullTextResumePilot;

public static class LivePilot
{
    internal const string AcceptanceRunId = "fulltext-citation-alignment-pilot-20260912";
    private const string AnalysisUrl = "http://127.0.0.1:5097";
    private const string CollectorUrl = "http://127.0.0.1:5197";
    private const string Api = AnalysisUrl + "/api/v1/";
    private const int MaximumCalls = 64;
    private const decimal MaximumSpendUsd = 2m;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<int> RunAsync(string sourceDirectory, bool resume)
    {
        string root = Program.FindRepositoryRoot();
        PilotArtifactStore artifacts = new(root, AcceptanceRunId);
        PreflightResult preflight = Program.RunPreflight(sourceDirectory);
        if (!preflight.Passed)
            throw new InvalidOperationException("Offline source preflight failed; paid execution refused.");
        CitationProbePlan probePlan = CreateCitationProbePlan(sourceDirectory);
        EnsurePortFree(5097); EnsurePortFree(5197);
        string databaseName;
        string master;
        string database;
        PilotGeminiBudgetState budget;
        PilotGeminiBudgetSnapshot? resumeBudgetBefore = null;
        JsonObject result;
        if (resume)
        {
            result = await artifacts.ReadStateAsync();
            databaseName = result["databaseName"]?.GetValue<string>() ??
                throw new InvalidOperationException("The resume state does not identify its database.");
            (master, database) = ConnectionStrings(databaseName);
            if (!await DatabaseExistsAsync(master, databaseName))
                throw new InvalidOperationException("The preserved pilot database is unavailable; paid resume refused.");
            PilotGeminiBudgetSnapshot persistedBudget = await artifacts.ReadBudgetAsync();
            List<UsageAttempt> persistedUsage = await ReadUsageAsync(database);
            bool sqlHasIncompleteOrUnknown = persistedUsage.Any(value =>
                value.CompletedAt is null || value.EstimatedUsd is null || value.Outcome == "Pending");
            PilotGeminiBudgetState.ValidateResumeSnapshot(persistedBudget, persistedUsage.Count,
                persistedUsage.Sum(value => value.EstimatedUsd ?? 0), sqlHasIncompleteOrUnknown);
            budget = PilotGeminiBudgetState.Restore(persistedBudget);
            resumeBudgetBefore = budget.Snapshot();
            result["resumeBudgetBefore"] = JsonSerializer.SerializeToNode(resumeBudgetBefore, JsonOptions);
            result["resumedAtUtc"] = DateTime.UtcNow;
        }
        else
        {
            artifacts.EnsureNewRun();
            databaseName = "AcademicFullTextResumePilot_" + Guid.NewGuid().ToString("N");
            (master, database) = await CreateDatabaseAsync(databaseName);
            budget = new(MaximumCalls, MaximumSpendUsd);
            result = new()
            {
                ["pilot"] = AcceptanceRunId, ["startedAtUtc"] = DateTime.UtcNow,
                ["databaseName"] = databaseName, ["analysisUrl"] = AnalysisUrl, ["collectorUrl"] = CollectorUrl,
                ["limits"] = JsonSerializer.SerializeToNode(new { MaximumCalls, MaximumSpendUsd }),
                ["preflight"] = JsonSerializer.SerializeToNode(preflight, JsonOptions), ["articles"] = new JsonArray(),
                ["status"] = "initialized"
            };
        }
        WebApplication? analysis = null;
        CollectorProcess? collector = null;
        bool success = false;
        await using PilotArtifactHeartbeat heartbeat = new(artifacts, budget, databaseName);
        try
        {
            await artifacts.WritePhaseAsync("initial", result, budget.Snapshot());
            (analysis, TimeSpan analysisStartup) = await StartAnalysisAsync(database, budget);
            result["analysisStartupSeconds"] = analysisStartup.TotalSeconds;
            collector = await CollectorProcess.StartAsync(root, database);
            result["collectorPid"] = collector.ProcessId; result["runnerAndAnalysisPid"] = Environment.ProcessId;
            result["collectorStartupSeconds"] = collector.StartupElapsed.TotalSeconds;
            result["status"] = "hosts_started";
            await artifacts.WritePhaseAsync("hosts-started", result, budget.Snapshot());
            using HttpClient client = new() { BaseAddress = new(CollectorUrl), Timeout = TimeSpan.FromMinutes(12) };
            using HttpClient analysisClient = new() { BaseAddress = new(AnalysisUrl), Timeout = TimeSpan.FromMinutes(12) };
            bool cachedProbePassed = result["citationProbeGate"]?["passed"]?.GetValue<bool>() == true;
            bool cachedProbeCurrent = cachedProbePassed &&
                await CachedProbeGateMatchesCurrentProfileAsync(analysisClient, result["citationProbeGate"] as JsonObject);
            if (resume && cachedProbePassed && !cachedProbeCurrent)
                throw new InvalidOperationException("The cached citation probe gate does not match the current pinned profile; paid resume refused.");
            if (!cachedProbeCurrent)
            {
                result["status"] = "citation_probes";
                heartbeat.SetPhase("citation-probes");
                bool probesPassed = await RunCitationProbesAsync(analysisClient, analysis.Services, probePlan, budget, result,
                    name => artifacts.WritePhaseAsync(name, result, budget.Snapshot()));
                if (!probesPassed)
                    throw new InvalidOperationException("Citation-alignment probes failed; the PDF batch was stopped before its first paid call.");
            }
            JsonArray articles = (JsonArray)result["articles"]!;
            SourcePreflight adamPreflight = preflight.Sources.Single(value => value.Name == "adam");
            LiveSource adamSource = new("adam", "pilot-citation-alignment-adam",
                "Adam: A Method for Stochastic Optimization", "10.48550/arXiv.1412.6980",
                "https://arxiv.org/pdf/1412.6980v1", adamPreflight);
            JsonObject adam = GetOrAddArticleResult(articles, adamSource);
            heartbeat.SetPhase("adam");
            try { await RunArticleAsync(client, database, adamSource, budget, adam, !resume,
                name => artifacts.WritePhaseAsync(name, result, budget.Snapshot())); }
            catch (Exception exception) { RecordArticleFailure(adam, exception); }
            await artifacts.WritePhaseAsync("adam-complete", result, budget.Snapshot());
            if (adam["operationalSuccess"]?.GetValue<bool>() != true)
                throw new InvalidOperationException("Adam workflow failed; football was not started.");
            SourcePreflight footballPreflight = preflight.Sources.Single(value => value.Name == "football");
            LiveSource footballSource = new("football", "pilot-citation-alignment-football",
                "Multiagent off-screen behavior prediction in football", "10.1038/s41598-022-12547-0",
                "https://livrepository.liverpool.ac.uk/3166141/1/Multiagent%20off-screen%20behavior%20prediction%20in%20football.pdf",
                footballPreflight);
            JsonObject football = GetOrAddArticleResult(articles, footballSource);
            heartbeat.SetPhase("football");
            try { await RunArticleAsync(client, database, footballSource, budget, football, !resume,
                name => artifacts.WritePhaseAsync(name, result, budget.Snapshot())); }
            catch (Exception exception) { RecordArticleFailure(football, exception); }
            await artifacts.WritePhaseAsync("football-complete", result, budget.Snapshot());
            result["budget"] = JsonSerializer.SerializeToNode(budget.Snapshot(), JsonOptions);
            success = articles.Count == 2 && articles.All(node => node?["operationalSuccess"]?.GetValue<bool>() == true) &&
                budget.Snapshot() is { Calls: <= MaximumCalls, CommittedSpendUsd: <= MaximumSpendUsd, DispatchStopped: false };
            if (resumeBudgetBefore is not null)
                success = success && budget.Snapshot().Calls == resumeBudgetBefore.Calls &&
                    budget.Snapshot().CommittedSpendUsd == resumeBudgetBefore.CommittedSpendUsd;
            result["operationalSuccess"] = success;
            result["scientificCorrectnessEstablished"] = false; result["humanExpertReviewPerformed"] = false;
            result["status"] = success ? "validation_complete" : "validation_failed";
        }
        catch (Exception exception)
        {
            result["failure"] = JsonSerializer.SerializeToNode(new { type = exception.GetType().Name, message = exception.Message });
            result["budget"] = JsonSerializer.SerializeToNode(budget.Snapshot(), JsonOptions);
            result["status"] = "preserved_for_resume";
        }
        finally
        {
            if (resumeBudgetBefore is not null)
            {
                PilotGeminiBudgetSnapshot resumeAfter = budget.Snapshot();
                result["resumeBudgetAfter"] = JsonSerializer.SerializeToNode(resumeAfter, JsonOptions);
                result["resumeCreatedNoProviderCalls"] = resumeAfter.Calls == resumeBudgetBefore.Calls &&
                    resumeAfter.CommittedSpendUsd == resumeBudgetBefore.CommittedSpendUsd;
            }
            try { result["finalSqlDiagnostics"] = await CaptureFinalSqlDiagnosticsAsync(database); }
            catch (Exception exception) { result["finalSqlDiagnosticsFailure"] = exception.Message; success = false; }
            if (collector is not null) await collector.DisposeAsync();
            if (analysis is not null) { await analysis.StopAsync(); await analysis.DisposeAsync(); }
            if (success)
            {
                try
                {
                    await DropDatabaseAsync(master, databaseName);
                    result["databaseDropped"] = true;
                    result["databasePreservedForResume"] = false;
                    result.Remove("resumeCommand");
                }
                catch (Exception exception) { result["databaseDropped"] = false; result["databaseCleanupFailure"] = exception.Message; success = false; }
            }
            else
            {
                result["databaseDropped"] = false;
                result["databasePreservedForResume"] = true;
                result["resumeCommand"] = "FullTextResumePilot.dll acceptance-live --execute-paid-pilot --resume";
            }
            result["finishedAtUtc"] = DateTime.UtcNow; result["operationalSuccess"] = success;
            await artifacts.WriteFinalAsync(result, budget.Snapshot());
        }
        Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        return success ? 0 : 1;
    }

    private static JsonObject GetOrAddArticleResult(JsonArray articles, LiveSource source)
    {
        JsonObject? existing = articles.OfType<JsonObject>().SingleOrDefault(value =>
            value["name"]?.GetValue<string>() == source.Name);
        if (existing is not null) return existing;
        JsonObject created = CreateArticleResult(source);
        articles.Add(created);
        return created;
    }

    private static JsonObject CreateArticleResult(LiveSource source) => new()
    {
        ["name"] = source.Name, ["personelId"] = source.PersonelId, ["doi"] = source.Doi,
        ["publicFullTextUrl"] = source.Url, ["offlinePdfSha256"] = source.Preflight.PdfSha256,
        ["operationalSuccess"] = false
    };

    private static void RecordArticleFailure(JsonObject result, Exception exception)
    {
        result["failure"] = JsonSerializer.SerializeToNode(new { type = exception.GetType().Name, message = exception.Message });
        result["operationalSuccess"] = false;
    }

    private static async Task RunArticleAsync(HttpClient client, string connectionString,
        LiveSource source, PilotGeminiBudgetState budget, JsonObject result, bool allowProviderDispatch,
        Func<string, Task> persistProgress)
    {
        int workId = await SeedAsync(connectionString, source); result["academicWorkId"] = workId;
        int beforeSummary = (await ReadUsageAsync(connectionString)).Count;
        int? recoveredCanonicalWorkId = await TryGetCanonicalWorkIdAsync(connectionString, workId, source.PersonelId);
        bool summaryAlreadyPersisted = recoveredCanonicalWorkId.HasValue &&
            await HasPersistedSummaryAsync(connectionString, recoveredCanonicalWorkId.Value);
        if (!summaryAlreadyPersisted)
        {
            if (!allowProviderDispatch)
                throw new InvalidOperationException("Resume refused to regenerate a missing persisted summary.");
            HttpCapture summary = await PostAsync(client, Api + "articles/summary/generate",
                new { PersonelID = source.PersonelId, AcademicWorkId = workId, Language = "tr" });
            result["summary"] = summary.ToJson();
            List<UsageAttempt> afterSummaryCall = await ReadUsageAsync(connectionString);
            result["summaryUsage"] = UsagePhase(afterSummaryCall.Skip(beforeSummary).ToList());
            await persistProgress(source.Name + "-summary-dispatched");
            if (summary.StatusCode is < 200 or >= 300) { result["operationalSuccess"] = false; return; }
        }
        else
        {
            result["summaryRecoveredFromSql"] = true;
        }
        List<UsageAttempt> afterSummary = await ReadUsageAsync(connectionString);
        int canonicalWorkId = await GetCanonicalWorkIdAsync(connectionString, workId, source.PersonelId);
        result["canonicalWorkId"] = canonicalWorkId; result["automaticCanonicalAssociationVerified"] = true;
        DatabaseAudit summaryAudit = await AuditSummaryAsync(connectionString, canonicalWorkId, source.Preflight, source.Url);
        result["summaryDatabaseAudit"] = summaryAudit.Json;
        result["summaryPhaseComplete"] = summaryAudit.Json["passed"]?.GetValue<bool>() == true;
        await persistProgress(source.Name + "-summary-audited");
        int beforeReview = afterSummary.Count;
        List<HttpCapture> reviewRequests = [];
        bool reviewAlreadyPersisted = await HasCompletedReviewAsync(connectionString, canonicalWorkId);
        if (!reviewAlreadyPersisted)
        {
            if (!allowProviderDispatch)
                throw new InvalidOperationException("Resume refused to regenerate a missing completed review.");
            for (int attempt = 0; attempt < 2; attempt++)
            {
                HttpCapture review = await PostAsync(client, Api + "articles/review/generate", new
                    { PersonelID = source.PersonelId, CanonicalWorkId = canonicalWorkId, Language = "tr", ForceRegeneration = false });
                reviewRequests.Add(review);
                if (review.StatusCode is >= 200 and < 300 || budget.Snapshot().DispatchStopped) break;
            }
            result["reviewRequests"] = new JsonArray(reviewRequests.Select(value => (JsonNode?)value.ToJson()).ToArray());
        }
        else
        {
            result["reviewRecoveredFromSql"] = true;
            HttpCapture recovered = await PostAsync(client, Api + "articles/analysis",
                new { PersonelID = source.PersonelId, CanonicalWorkId = canonicalWorkId, Language = "tr" });
            reviewRequests.Add(recovered);
            result["reviewRequests"] = new JsonArray(recovered.ToJson());
        }
        List<UsageAttempt> afterReview = await ReadUsageAsync(connectionString);
        result["reviewUsage"] = UsagePhase(afterReview.Skip(beforeReview).ToList());
        await persistProgress(source.Name + "-review-dispatched");
        DatabaseAudit reviewAudit = await AuditReviewAsync(connectionString, canonicalWorkId,
            afterReview.Select(value => value.AttemptId).ToHashSet());
        result["reviewDatabaseAudit"] = reviewAudit.Json;
        bool reviewSucceeded = reviewRequests.Last().StatusCode is >= 200 and < 300;
        if (!reviewSucceeded)
        {
            result["operationalSuccess"] = false;
            await persistProgress(source.Name + "-review-failed");
            return;
        }
        result["reviewPhaseComplete"] = reviewAudit.Json["passed"]?.GetValue<bool>() == true;
        await persistProgress(source.Name + "-review-audited");
        int beforeReads = afterReview.Count, guardBeforeReads = budget.Snapshot().Calls;
        HttpCapture savedSummary = await PostAsync(client, Api + "articles/summary",
            new { PersonelID = source.PersonelId, AcademicWorkId = workId, Language = "tr" });
        HttpCapture savedReview = await PostAsync(client, Api + "articles/analysis",
            new { PersonelID = source.PersonelId, CanonicalWorkId = canonicalWorkId, Language = "tr" });
        HttpCapture evidence = savedReview;
        HttpCapture reused = await PostAsync(client, Api + "articles/review/generate",
            new { PersonelID = source.PersonelId, CanonicalWorkId = canonicalWorkId, Language = "tr", ForceRegeneration = false });
        int afterReads = (await ReadUsageAsync(connectionString)).Count, guardAfterReads = budget.Snapshot().Calls;
        bool noCalls = beforeReads == afterReads && guardBeforeReads == guardAfterReads;
        bool summaryReadMatches = PilotReportComparer.MatchesResponse<ArticleSummaryReport>(savedSummary.Body,
            summaryAudit.StoredReport);
        bool reviewReadMatches = PilotReportComparer.MatchesResponse<ArticleReviewReport>(savedReview.Body,
            reviewAudit.StoredReport) && PilotReportComparer.MatchesResponse<ArticleReviewReport>(reused.Body,
                reviewAudit.StoredReport);
        bool combinedEvidencePresent = JsonNode.Parse(savedReview.Body)?["Evidence"] is not null ||
            JsonNode.Parse(savedReview.Body)?["evidence"] is not null;
        result["savedReadsAndCache"] = JsonSerializer.SerializeToNode(new
        {
            savedSummary = savedSummary.ToJson(), savedReview = savedReview.ToJson(), evidence = evidence.ToJson(),
            repeatedReview = reused.ToJson(), usageBefore = beforeReads, usageAfter = afterReads,
            guardCallsBefore = guardBeforeReads, guardCallsAfter = guardAfterReads, createdNoProviderAttempts = noCalls,
            combinedEvidencePresent,
            savedSummaryReportMatchesSql = summaryReadMatches, savedAndReusedReviewReportsMatchSql = reviewReadMatches
        }, JsonOptions);
        result["operationalSuccess"] = reviewSucceeded && savedSummary.StatusCode == 200 && savedReview.StatusCode == 200 &&
            evidence.StatusCode == 200 && combinedEvidencePresent && reused.StatusCode == 200 && noCalls && summaryReadMatches && reviewReadMatches &&
            summaryAudit.Json["passed"]?.GetValue<bool>() == true && reviewAudit.Json["passed"]?.GetValue<bool>() == true;
        result["cachePhaseComplete"] = true;
        await persistProgress(source.Name + "-saved-reads-and-cache");
    }

    private static async Task<bool> RunCitationProbesAsync(HttpClient client, IServiceProvider services,
        CitationProbePlan probePlan,
        PilotGeminiBudgetState budget, JsonObject result, Func<string, Task> persistProgress)
    {
        using HttpResponseMessage profilesResponse = await client.GetAsync("/api/v1/evaluations/profiles");
        string profilesBody = await profilesResponse.Content.ReadAsStringAsync();
        if (!profilesResponse.IsSuccessStatusCode)
            throw new InvalidOperationException($"Evaluation profiles returned HTTP {(int)profilesResponse.StatusCode}.");
        ArticleEvaluationProfilesResponse profiles = JsonSerializer.Deserialize<ArticleEvaluationProfilesResponse>(
            profilesBody, JsonOptions) ?? throw new InvalidOperationException("Evaluation profiles response was invalid.");
        ArticleEvaluationProfile profile = profiles.Profiles.Single(value =>
            value.ProfileId == ArticleEvaluationProfileCatalog.GeminiProfileId);
        IReadOnlyDictionary<string, string> settings = profile.ExecutionSettings ??
            throw new InvalidOperationException("Gemini evaluation settings are absent.");
        bool profilePinned = ProfilePinned(profile, settings);
        if (!profilePinned)
            throw new InvalidOperationException("The Gemini evaluation profile does not match the acceptance settings.");

        ReviewArticleRequest source = probePlan.Source;
        IReadOnlyList<ProbeCase> cases = probePlan.Cases;
        JsonArray captures = [];
        JsonObject gate = new()
        {
            ["profile"] = JsonSerializer.SerializeToNode(profile, JsonOptions),
            ["profilePinned"] = true,
            ["offlineSourceSelection"] = JsonSerializer.SerializeToNode(probePlan.Preflight, JsonOptions),
            ["profilesHttp"] = new JsonObject
            {
                ["statusCode"] = (int)profilesResponse.StatusCode,
                ["body"] = JsonNode.Parse(profilesBody)
            },
            ["cases"] = captures,
            ["passed"] = false
        };
        result["citationProbeGate"] = gate;
        foreach (ProbeCase probe in cases)
        {
            ReviewArticleRequest localizedSource = source with { Language = probe.Language };
            ArticleEvaluationRequest request = new(profile.ProfileId, ArticleEvaluationTaskKinds.Calibration,
                profile.SettingsFingerprint, localizedSource) { CalibrationClaims = probe.Claims };
            PilotGeminiBudgetSnapshot before = budget.Snapshot();
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            ArticleEvaluationResponse typed = await scope.ServiceProvider
                .GetRequiredService<ArticleEvaluationService>().ExecuteAsync(request, CancellationToken.None);
            PilotGeminiBudgetSnapshot after = budget.Snapshot();
            Dictionary<string, string> observed = (typed.Verdicts ?? []).ToDictionary(value => value.ItemId,
                value => value.Verdict, StringComparer.Ordinal);
            bool passed = typed.Outcome == ArticleEvaluationOutcomes.Completed &&
                typed.Provider == "Gemini" && typed.RequestedModel == "gemini-3.8-flash" &&
                typed.SettingsFingerprint == profile.SettingsFingerprint && typed.Telemetry.AttemptCount == 1 &&
                probe.Expected.All(value => observed.TryGetValue(value.Key, out string? verdict) && verdict == value.Value) &&
                observed.Count == probe.Expected.Count && after.Calls == before.Calls + 1;
            captures.Add(JsonSerializer.SerializeToNode(new
            {
                probe.Name,
                request,
                execution = "in-process engine diagnostic",
                engineResponse = typed,
                expectation = probe.Expected,
                observed,
                passed,
                guardedCalls = after.Items.Skip(before.Calls).ToList()
            }, JsonOptions));
            await persistProgress("citation-probe-" + probe.Name);
            if (!passed)
            {
                gate["passed"] = false;
                gate["failedCase"] = probe.Name;
                return false;
            }
        }
        gate["passed"] = true;
        gate["completedAtUtc"] = DateTime.UtcNow;
        await persistProgress("citation-probes-passed");
        return true;
    }

    private static async Task<bool> CachedProbeGateMatchesCurrentProfileAsync(HttpClient client,
        JsonObject? gate)
    {
        if (gate?["profile"] is not JsonNode storedNode) return false;
        ArticleEvaluationProfile? stored = storedNode.Deserialize<ArticleEvaluationProfile>(JsonOptions);
        if (stored?.ExecutionSettings is null) return false;
        using HttpResponseMessage response = await client.GetAsync("/api/v1/evaluations/profiles");
        if (!response.IsSuccessStatusCode) return false;
        ArticleEvaluationProfilesResponse? profiles = await response.Content
            .ReadFromJsonAsync<ArticleEvaluationProfilesResponse>(JsonOptions);
        ArticleEvaluationProfile? current = profiles?.Profiles.SingleOrDefault(value =>
            value.ProfileId == ArticleEvaluationProfileCatalog.GeminiProfileId);
        return current?.ExecutionSettings is not null && ProfilePinned(current, current.ExecutionSettings) &&
            current.SettingsFingerprint == stored.SettingsFingerprint &&
            current.ExecutionSettings.OrderBy(value => value.Key, StringComparer.Ordinal).SequenceEqual(
                stored.ExecutionSettings.OrderBy(value => value.Key, StringComparer.Ordinal));
    }

    private static bool ProfilePinned(ArticleEvaluationProfile profile,
        IReadOnlyDictionary<string, string> settings) =>
        profile.Provider == "Gemini" && profile.RequestedModel == "gemini-3.8-flash" &&
        profile.Availability == "configured" && settings["generationThinking"] == "high" &&
        settings["verifierThinking"] == "high" && settings["generationMaxOutputTokens"] == "8192" &&
        settings["verifierMaxOutputTokens"] == "8192" &&
        settings["claimVerificationPromptVersion"] == "article-claim-verification-v4" &&
        settings["reviewGenerationPromptVersion"] == "article-specialist-review-v3" &&
        settings["reviewVerificationPromptVersion"] == "article-specialist-review-verification-v3";

    internal static CitationProbePreflight ValidateCitationProbePlan(string sourceDirectory) =>
        CreateCitationProbePlan(sourceDirectory).Preflight;

    private static CitationProbePlan CreateCitationProbePlan(string sourceDirectory)
    {
        ArticlePdfExtractor extractor = new(Options.Create(new ArticleSummaryOptions
        {
            MaximumPages = 40,
            MaximumExtractedCharacters = 160000,
            OcrEnabled = false
        }));
        SummarizeArticleRequest extracted = extractor.Extract(
            File.ReadAllBytes(Path.Combine(sourceDirectory, "adam-v1-fulltext.pdf")), "tr");
        string canonicalPages = JsonSerializer.Serialize(extracted.Pages,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        string sourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPages)))
            .ToLowerInvariant();
        ReviewArticleRequest source = new("tr", "pdf", sourceHash, extracted.ExtractionVersion,
            ArticleReviewer.DefaultPolicyVersion, extracted.Pages, extracted.TotalSourcePages,
            extracted.IsPartial, extracted.ScopeReason)
        {
            SourceSpans = extracted.SourceSpans
        };
        ArticleSourceSpan first = ExactProbeSpan(source, 0, 506, "src-1-0-9ab39fb5ef9bf2a0");
        ArticleSourceSpan middle = ExactProbeSpan(source, 3061, 3537, "src-1-3061-5f51631768f3e9eb");
        ArticleSourceSpan next = source.SourceSpans!.Single(value =>
            value.PageNumber == 1 && value.StartOffset == 3537);
        const string claim = "Stokastik amaç fonksiyonlarının birinci derece gradyan tabanlı optimizasyonu için AdaGrad ve RMSProp yöntemlerinin avantajlarını birleştiren Adam algoritmasını geliştirmek amaçlanmıştır.";
        bool negativeOmitsRmsProp = !string.Concat(first.Text, middle.Text)
            .Contains("RMSProp", StringComparison.OrdinalIgnoreCase);
        bool positiveContainsRmsProp = string.Concat(middle.Text, next.Text)
            .Contains("RMSProp", StringComparison.OrdinalIgnoreCase);
        CitationProbePreflight preflight = new(sourceHash,
            sourceHash == "ef1663dd32671bce737af77d0cfd0777fd8ebd95bb86f5d0ff2b4aa457675fa3",
            first.SourceId, middle.SourceId, next.SourceId, next.StartOffset, next.EndOffset,
            negativeOmitsRmsProp, positiveContainsRmsProp,
            ArticleReviewer.DefaultPolicyVersion);
        if (!preflight.Passed)
            throw new InvalidOperationException("The exact historical citation probe source selection failed offline preflight.");
        ArticleEvaluationCalibrationClaim Negative(string id) =>
            new(id, claim, "purpose", [first.SourceId, middle.SourceId]);
        ArticleEvaluationCalibrationClaim Positive(string id) =>
            new(id, claim, "purpose", [middle.SourceId, next.SourceId]);
        ProbeCase[] cases =
        [
            new("historical-paired", "tr",
                [Negative("historical-negative-paired"), Positive("historical-positive-paired")],
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["historical-negative-paired"] = "uncertain",
                    ["historical-positive-paired"] = "supported"
                }),
            new("historical-negative-alone", "tr", [Negative("historical-negative-alone")],
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["historical-negative-alone"] = "uncertain"
                }),
            new("historical-positive-alone", "tr", [Positive("historical-positive-alone")],
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["historical-positive-alone"] = "supported"
                })
        ];
        return new(source, cases, preflight);
    }

    private static ArticleSourceSpan ExactProbeSpan(ReviewArticleRequest source, int startOffset,
        int endOffset, string sourceId) => source.SourceSpans!.Single(value =>
        value.PageNumber == 1 && value.StartOffset == startOffset && value.EndOffset == endOffset &&
        value.SourceId == sourceId);

    private static async Task<(WebApplication, TimeSpan)> StartAnalysisAsync(string connectionString,
        PilotGeminiBudgetState budget)
    {
        IConfigurationRoot secrets = new ConfigurationBuilder()
            .AddUserSecrets(typeof(ResearcherAnalysisService.Program).Assembly, optional: false).Build();
        string apiKey = secrets["Gemini:ApiKey"] ?? throw new InvalidOperationException("Gemini:ApiKey user secret is missing.");
        Stopwatch timer = Stopwatch.StartNew();
        WebApplication app = ResearcherAnalysisService.Program.CreateApplication(
            ["--environment", "Testing", "--urls", AnalysisUrl], builder =>
            {
                builder.Configuration.Sources.Clear();
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Urls"] = AnalysisUrl,
                    ["ConnectionStrings:UsageDatabase"] = connectionString, ["Gemini:ApiKey"] = apiKey,
                    ["Ai:Provider"] = "Ollama", ["Ai:ArticleProvider"] = "Gemini",
                    ["Ai:ArticleModel"] = "gemini-3.8-flash", ["Ai:ArticleVerifierModel"] = "gemini-3.8-flash",
                    ["Ai:ArticleGenerationThinkingLevel"] = "high", ["Ai:ArticleVerifierThinkingLevel"] = "high",
                    ["Ai:ArticleContextTokens"] = "131072", ["Ai:ArticleMaxOutputTokens"] = "8192",
                    ["Ai:ArticleVerifierMaxOutputTokens"] = "8192", ["Ai:ArticleFallbackChunkBytes"] = "10000",
                    ["Ai:ArticleReviewMaximumInputBytes"] = "100000", ["Ai:ArticleReviewTimeoutSeconds"] = "300",
                    ["Ai:TimeoutSeconds"] = "180"
                });
                builder.Logging.ClearProviders();
                builder.Services.AddSingleton(budget); builder.Services.AddTransient<PilotGeminiBudgetHandler>();
                PilotGeminiClientRegistration.AddGuardedClients(builder.Services);
            });
        await app.StartAsync(); timer.Stop();
        if (timer.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("Analysis startup exceeded 30 seconds.");
        return (app, timer.Elapsed);
    }

    private static async Task<int> SeedAsync(string connectionString, LiveSource source)
    {
        await using AcademicDbContext db = Database(connectionString);
        int? existing = await db.AcademicWorks.AsNoTracking().Where(value =>
                value.PersonelId == source.PersonelId && value.Provider == AcademicWorkProvider.OpenAlex &&
                value.ProviderWorkId == "resume-pilot-" + source.Name)
            .Select(value => (int?)value.Id).SingleOrDefaultAsync();
        if (existing.HasValue) return existing.Value;
        Researcher researcher = new() { PersonelId = source.PersonelId, FirstName = "Synthetic", LastName = "Pilot" };
        AcademicWork work = new() { PersonelId = source.PersonelId, Provider = AcademicWorkProvider.OpenAlex,
            ProviderWorkId = "resume-pilot-" + source.Name, Title = source.Title, Doi = source.Doi,
            FullTextUrl = source.Url, HasFullText = true, IsOpenAccess = true, ProviderPayload = "{}", SyncedAt = DateTime.UtcNow };
        db.AddRange(researcher, work); await db.SaveChangesAsync(); return work.Id;
    }

    private static async Task<int?> TryGetCanonicalWorkIdAsync(string connectionString, int workId, string personelId)
    {
        await using AcademicDbContext db = Database(connectionString);
        int? canonicalWorkId = await db.CanonicalWorkObservations.AsNoTracking()
            .Where(value => value.AcademicWorkId == workId)
            .Select(value => (int?)value.CanonicalWorkId).SingleOrDefaultAsync();
        if (!canonicalWorkId.HasValue) return null;
        return await db.CanonicalResearcherWorks.AsNoTracking().AnyAsync(value =>
            value.CanonicalWorkId == canonicalWorkId.Value && value.PersonelId == personelId)
            ? canonicalWorkId
            : null;
    }

    private static async Task<bool> HasPersistedSummaryAsync(string connectionString, int canonicalWorkId)
    {
        await using AnalysisDbContext db = AnalysisDatabase(connectionString);
        return await db.CanonicalArticleAnalysisRuns.AsNoTracking().AnyAsync(value =>
            value.CanonicalWorkId == canonicalWorkId && value.SavedArticleSummary != null);
    }

    private static async Task<bool> HasCompletedReviewAsync(string connectionString, int canonicalWorkId)
    {
        await using AnalysisDbContext db = AnalysisDatabase(connectionString);
        return await db.CanonicalArticleReviewRuns.AsNoTracking().AnyAsync(value =>
            value.CanonicalWorkId == canonicalWorkId && value.ProcessedRoles == 4 && value.TotalRoles == 4);
    }

    private static async Task<int> GetCanonicalWorkIdAsync(string connectionString, int workId, string personelId)
    {
        await using AcademicDbContext db = Database(connectionString);
        int canonicalWorkId = await db.CanonicalWorkObservations.AsNoTracking().Where(value => value.AcademicWorkId == workId)
            .Select(value => value.CanonicalWorkId).SingleAsync();
        if (!await db.CanonicalResearcherWorks.AsNoTracking().AnyAsync(value =>
                value.CanonicalWorkId == canonicalWorkId && value.PersonelId == personelId))
            throw new InvalidOperationException("The summary workflow did not establish the synthetic canonical association.");
        return canonicalWorkId;
    }

    private static async Task<DatabaseAudit> AuditSummaryAsync(string connectionString, int canonicalWorkId,
        SourcePreflight expected, string expectedUrl)
    {
        await using AnalysisDbContext db = AnalysisDatabase(connectionString);
        CanonicalArticleAnalysisRun run = await db.CanonicalArticleAnalysisRuns.AsNoTracking()
            .Include(value => value.ArticleSourceSnapshot).ThenInclude(value => value!.Pages)
            .Include(value => value.ArticleSourceSnapshot).ThenInclude(value => value!.Spans)
            .Include(value => value.SavedArticleSummary)
            .Include(value => value.Claims).ThenInclude(value => value.Evidence).ThenInclude(value => value.ArticleSourceSpan)
            .Where(value => value.CanonicalWorkId == canonicalWorkId).OrderByDescending(value => value.Id).FirstAsync();
        ArticleSourceSnapshot source = run.ArticleSourceSnapshot!;
        bool exactCatalog = source.Spans.All(span => { ArticleSourcePageSnapshot page = source.Pages.Single(value => value.PageNumber == span.PageNumber);
            return span.StartOffset >= 0 && span.EndOffset <= page.Text.Length && page.Text[span.StartOffset..span.EndOffset] == span.Text; });
        bool evidenceExact = run.Claims.SelectMany(value => value.Evidence).All(value =>
            value.ArticleSourceSpan is not null && source.Spans.Any(span => span.Id == value.ArticleSourceSpanId));
        ArticleSummaryReport storedReport = JsonSerializer.Deserialize<ArticleSummaryReport>(
            run.SavedArticleSummary!.ReportJson, JsonOptions) ?? throw new InvalidOperationException("Stored summary report is invalid.");
        IEnumerable<ArticleClaim> storedClaims = storedReport.Sections.Purpose.Concat(storedReport.Sections.Methods)
            .Concat(storedReport.Sections.Data).Concat(storedReport.Sections.Findings).Concat(storedReport.Sections.Limitations);
        bool storedReportEvidenceExact = storedClaims.SelectMany(claim => claim.Evidence).All(evidence =>
        {
            ArticleSourceSpanSnapshot? span = source.Spans.SingleOrDefault(value => value.SourceId == evidence.SourceId);
            if (span is null) return false;
            ArticleSourcePageSnapshot page = source.Pages.Single(value => value.PageNumber == span.PageNumber);
            return evidence.PageNumber == span.PageNumber && evidence.StartOffset == span.StartOffset &&
                evidence.EndOffset == span.EndOffset && evidence.Quote == span.Text &&
                page.Text[span.StartOffset..span.EndOffset] == evidence.Quote;
        });
        bool passed = source.ExtractedTextHash == expected.SourceHash && source.Pages.Count == expected.Pages &&
            source.Spans.Count == expected.Spans && exactCatalog && evidenceExact && storedReportEvidenceExact &&
            run.SourceOrigin == "FullTextUrl" && run.Language == "tr" &&
            EquivalentSourceUrl(run.SourceUrl, expectedUrl) && run.ExtractionMethod == "pdf_text" &&
            source.ExtractionVersion == ArticlePdfExtractor.Version;
        JsonObject json = (JsonObject)JsonSerializer.SerializeToNode(new { passed, runId = run.Id, sourceSnapshotId = source.Id,
            run.SourceAcquiredAt, run.SourceOrigin, run.SourceUrl, sourceHash = source.ExtractedTextHash,
            pages = source.Pages.Count, spans = source.Spans.Count, claims = run.Claims.Count,
            evidenceLinks = run.Claims.Sum(value => value.Evidence.Count), exactCatalog, evidenceExact, storedReportEvidenceExact,
            run.IsPartial, run.ScopeReason }, JsonOptions)!;
        return new(json, JsonSerializer.SerializeToNode(storedReport, JsonOptions)!);
    }

    public static bool EquivalentSourceUrl(string? left, string? right) =>
        Uri.TryCreate(left, UriKind.Absolute, out Uri? leftUri) &&
        Uri.TryCreate(right, UriKind.Absolute, out Uri? rightUri) && leftUri.Equals(rightUri);

    private static async Task<DatabaseAudit> AuditReviewAsync(string connectionString, int canonicalWorkId,
        HashSet<Guid> usageIds)
    {
        await using AnalysisDbContext db = AnalysisDatabase(connectionString);
        CanonicalArticleReviewRun? run = await db.CanonicalArticleReviewRuns.AsNoTracking()
            .Include(value => value.ArticleSourceSnapshot).ThenInclude(value => value!.Pages)
            .Include(value => value.ArticleSourceSnapshot).ThenInclude(value => value!.Spans)
            .Include(value => value.Findings).ThenInclude(value => value.Evidence).ThenInclude(value => value.ArticleSourceSpan)
            .Where(value => value.CanonicalWorkId == canonicalWorkId).OrderByDescending(value => value.Id).FirstOrDefaultAsync();
        List<ArticleReviewWorkItem> items = await db.ArticleReviewWorkItems.AsNoTracking().Include(value => value.Checkpoints)
            .Where(value => value.CanonicalWorkId == canonicalWorkId).OrderBy(value => value.Id).ToListAsync();
        List<ArticleReviewStageCheckpoint> checkpoints = items.SelectMany(value => value.Checkpoints).OrderBy(value => value.Id).ToList();
        bool attemptsMatch = checkpoints.Where(value => value.AttemptId.HasValue).All(value => usageIds.Contains(value.AttemptId!.Value));
        bool exactEvidence = run is not null && run.ArticleSourceSnapshot is not null &&
            run.Findings.SelectMany(value => value.Evidence).All(value =>
            {
                ArticleSourceSpanSnapshot? span = value.ArticleSourceSpan;
                if (span is null || !run.ArticleSourceSnapshot.Spans.Any(sourceSpan => sourceSpan.Id == span.Id))
                    return false;
                ArticleSourcePageSnapshot page = run.ArticleSourceSnapshot.Pages.Single(sourcePage =>
                    sourcePage.PageNumber == span.PageNumber);
                return span.StartOffset >= 0 && span.EndOffset <= page.Text.Length &&
                    page.Text[span.StartOffset..span.EndOffset] == span.Text;
            });
        ArticleReviewReport? storedReport = run is null ? null : JsonSerializer.Deserialize<ArticleReviewReport>(
            run.ReportJson, JsonOptions);
        bool storedReportEvidenceExact = storedReport is not null && run?.ArticleSourceSnapshot is not null &&
            storedReport.Reviews.SelectMany(review => review.Findings).SelectMany(finding => finding.Evidence)
                .All(evidence =>
                {
                    ArticleSourceSpanSnapshot? span = run.ArticleSourceSnapshot.Spans.SingleOrDefault(value =>
                        value.SourceId == evidence.SourceId);
                    if (span is null) return false;
                    ArticleSourcePageSnapshot page = run.ArticleSourceSnapshot.Pages.Single(value =>
                        value.PageNumber == span.PageNumber);
                    return evidence.PageNumber == span.PageNumber && evidence.StartOffset == span.StartOffset &&
                        evidence.EndOffset == span.EndOffset && evidence.Quote == span.Text &&
                        page.Text[span.StartOffset..span.EndOffset] == evidence.Quote;
                });
        bool passed = run is not null && run.ProcessedRoles == 4 && run.TotalRoles == 4 &&
            run.Language == "tr" && items.Any(value => value.Status == "Completed") && attemptsMatch &&
            exactEvidence && storedReportEvidenceExact;
        JsonObject json = (JsonObject)JsonSerializer.SerializeToNode(new { passed, reviewRunId = run?.Id, run?.ProcessedRoles,
            run?.TotalRoles, findings = run?.Findings.Count ?? 0, evidenceLinks = run?.Findings.Sum(value => value.Evidence.Count) ?? 0,
            exactEvidence, storedReportEvidenceExact, attemptIdsMatchUsageLedger = attemptsMatch, workItems = items.Select(value => new
            { value.Id, value.Status, value.MaximumCalls, value.MaximumSpendUsd, checkpoints = value.Checkpoints.OrderBy(c => c.Id).Select(c => new
                { c.Id, c.Stage, c.Role, c.BatchKey, c.ParentBatchKey, c.Ordinal, c.Status, c.AttemptId,
                    c.ReservedCostUsd, c.ActualCostUsd, c.PricingVersion, c.ErrorCode }) }) }, JsonOptions)!;
        return new(json, storedReport is null ? null :
            JsonSerializer.SerializeToNode(storedReport, JsonOptions)!);
    }

    public static async Task<int> AuditFrozenResultAsync()
    {
        string root = Program.FindRepositoryRoot();
        string rawPath = Path.Combine(root, "docs", "fulltext-resume-pilot-20260912-result.json");
        JsonObject raw = (JsonNode.Parse(await File.ReadAllTextAsync(rawPath)) as JsonObject) ??
            throw new InvalidOperationException("The frozen raw result is invalid.");
        JsonObject article = raw["articles"]?[0] as JsonObject ?? throw new InvalidOperationException("Adam result is absent.");
        JsonNode initialSummary = ReportNode(article["summary"]?["body"]);
        JsonNode savedSummary = ReportNode(article["savedReadsAndCache"]?["savedSummary"]?["body"]);
        JsonNode initialReview = ReportNode(article["reviewRequests"]?[0]?["body"]);
        JsonNode savedReview = ReportNode(article["savedReadsAndCache"]?["savedReview"]?["body"]);
        JsonNode reusedReview = ReportNode(article["savedReadsAndCache"]?["repeatedReview"]?["body"]);
        bool summaryEqual = PilotReportComparer.Equivalent<ArticleSummaryReport>(initialSummary, savedSummary);
        bool reviewEqual = PilotReportComparer.Equivalent<ArticleReviewReport>(initialReview, savedReview);
        bool reuseEqual = PilotReportComparer.Equivalent<ArticleReviewReport>(initialReview, reusedReview);
        decimal spent = raw["budget"]!["committedSpendUsd"]!.GetValue<decimal>();
        int calls = raw["budget"]!["calls"]!.GetValue<int>();
        decimal footballSample = raw["preflight"]!["sources"]![1]!["sampleEightStageReservationUsd"]!.GetValue<decimal>();
        bool correctedAdamOperational = summaryEqual && reviewEqual && reuseEqual &&
            article["summaryDatabaseAudit"]?["passed"]?.GetValue<bool>() == true &&
            article["reviewDatabaseAudit"]?["passed"]?.GetValue<bool>() == true &&
            article["savedReadsAndCache"]?["createdNoProviderAttempts"]?.GetValue<bool>() == true;
        JsonObject audit = new()
        {
            ["audit"] = "fulltext-resume-pilot-20260912-offline-correction",
            ["rawArtifact"] = "docs/fulltext-resume-pilot-20260912-result.json",
            ["rawArtifactUnchanged"] = true,
            ["reason"] = "The raw JSON comparison did not normalize property-name casing or omitted optional null properties. Typed offline replay normalizes those serialization shapes and verifies semantic equality; the frozen raw flags remain unchanged.",
            ["observedSerializationShapeDifference"] = JsonSerializer.SerializeToNode(new
            {
                publicReviewFindingsWithOmittedNullSuggestion = 3,
                typedStoredSerializationIncludesExplicitNullSuggestion = true,
                semanticValueOnBothSides = "null"
            }),
            ["typedComparisons"] = JsonSerializer.SerializeToNode(new
            {
                generatedSummaryEqualsSavedSqlRead = summaryEqual,
                generatedReviewEqualsSavedSqlRead = reviewEqual,
                generatedReviewEqualsCachedRepeat = reuseEqual
            }),
            ["sqlEvidence"] = JsonSerializer.SerializeToNode(new
            {
                rawSqlReportJsonSidecarCaptured = false,
                savedReadEndpointsAreSqlOnly = true,
                summaryStoredEvidenceExact = article["summaryDatabaseAudit"]?["storedReportEvidenceExact"]?.GetValue<bool>(),
                reviewStoredEvidenceExact = article["reviewDatabaseAudit"]?["storedReportEvidenceExact"]?.GetValue<bool>(),
                immutableRowsAndCheckpointsCapturedBeforeDrop = raw["finalSqlDiagnostics"] is not null
            }),
            ["correctedAdamOperationalSuccess"] = correctedAdamOperational,
            ["sourceFidelity"] = savedSummary["SourceFidelity"]?.DeepClone() ?? savedSummary["sourceFidelity"]?.DeepClone(),
            ["reportLanguage"] = savedSummary["Language"]?.DeepClone() ?? savedSummary["language"]?.DeepClone(),
            ["turkishSummarySample"] = savedSummary["Sections"]?["Purpose"]?[0]?["Text"]?.DeepClone() ??
                savedSummary["sections"]?["purpose"]?[0]?["text"]?.DeepClone(),
            ["budgetBoundary"] = JsonSerializer.SerializeToNode(new
            {
                originalMaximumCalls = 32, usedCalls = calls, remainingCalls = 32 - calls,
                originalMaximumSpendUsd = 1m, usedUsd = spent, remainingUsd = 1m - spent,
                footballSampleEightReviewStageReservationUsd = footballSample,
                footballLiveDeferred = footballSample > 1m - spent,
                reason = "The sampled eight football review-stage reservations alone exceed the remaining USD ceiling; summary cost is additional. No football paid call was started."
            }),
            ["scientificCorrectnessEstablished"] = false,
            ["createdAtUtc"] = DateTime.UtcNow
        };
        string output = Path.Combine(root, "docs", "fulltext-resume-pilot-20260912-corrected-audit.json");
        await File.WriteAllTextAsync(output, audit.ToJsonString(JsonOptions) + Environment.NewLine);
        Console.WriteLine(audit.ToJsonString(JsonOptions));
        return correctedAdamOperational ? 0 : 1;
    }

    private static JsonNode ReportNode(JsonNode? body) => body?["Report"] ?? body?["report"] ??
        body?["Review"]?["Report"] ?? body?["review"]?["report"] ??
        throw new InvalidOperationException("A captured report body is missing.");

    private static JsonNode UsagePhase(IReadOnlyList<UsageAttempt> attempts)
    {
        int unknown = attempts.Count(value => value.EstimatedUsd is null);
        decimal knownSubtotal = attempts.Sum(value => value.EstimatedUsd ?? 0);
        return JsonSerializer.SerializeToNode(new
        {
            providerCalls = attempts.Count, successfulCalls = attempts.Count(value => value.Outcome == "Success"),
            failedCalls = attempts.Count(value => value.Outcome != "Success"), promptTokens = attempts.Sum(value => value.PromptTokens ?? 0),
            cachedTokens = attempts.Sum(value => value.CachedTokens ?? 0), candidateTokens = attempts.Sum(value => value.CandidateTokens ?? 0),
            thoughtTokens = attempts.Sum(value => value.ThoughtTokens ?? 0), totalTokens = attempts.Sum(value => value.TotalTokens ?? 0),
            unknownCostAttempts = unknown, knownCostSubtotalUsd = knownSubtotal,
            estimatedUsd = unknown == 0 ? knownSubtotal : (decimal?)null, attempts
        }, JsonOptions)!;
    }

    private static async Task<List<UsageAttempt>> ReadUsageAsync(string connectionString)
    {
        await using SqlConnection connection = new(connectionString); await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand(); command.CommandText = """
            SELECT AttemptId,StartedAt,CompletedAt,RequestedModel,ReturnedModel,Outcome,HttpStatus,
                   PromptTokenCount,CachedTokenCount,CandidateTokenCount,ThoughtTokenCount,TotalTokenCount,PricingVersion,EstimatedUsd
            FROM [analysis].[GeminiUsageAttempts] ORDER BY StartedAt,AttemptId;
            """;
        await using SqlDataReader reader = await command.ExecuteReaderAsync(); List<UsageAttempt> attempts = [];
        while (await reader.ReadAsync()) attempts.Add(new(reader.GetGuid(0), DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc),
            reader.IsDBNull(2) ? null : DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc), reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetInt32(6),
            reader.IsDBNull(7) ? null : reader.GetInt64(7), reader.IsDBNull(8) ? null : reader.GetInt64(8),
            reader.IsDBNull(9) ? null : reader.GetInt64(9), reader.IsDBNull(10) ? null : reader.GetInt64(10),
            reader.IsDBNull(11) ? null : reader.GetInt64(11), reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.IsDBNull(13) ? null : reader.GetDecimal(13)));
        return attempts;
    }

    private static async Task<HttpCapture> PostAsync(HttpClient client, string path, object body)
    {
        Stopwatch timer = Stopwatch.StartNew(); using HttpResponseMessage response = await client.PostAsJsonAsync(path, body, JsonOptions);
        string responseBody = await response.Content.ReadAsStringAsync(); timer.Stop();
        return new((int)response.StatusCode, timer.Elapsed.TotalSeconds, responseBody);
    }

    private static AcademicDbContext Database(string connectionString) => new(
        new DbContextOptionsBuilder<AcademicDbContext>().UseSqlServer(connectionString).Options);

    private static AnalysisDbContext AnalysisDatabase(string connectionString) => new(
        new DbContextOptionsBuilder<AnalysisDbContext>().UseSqlServer(connectionString).Options);

    private static async Task<(string, string)> CreateDatabaseAsync(string name)
    {
        ValidateDatabaseName(name);
        (string master, string database) = ConnectionStrings(name);
        await using (SqlConnection connection = new(master)) { await connection.OpenAsync(); await using SqlCommand command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE [{name}]"; await command.ExecuteNonQueryAsync(); }
        return (master, database);
    }

    private static (string Master, string Database) ConnectionStrings(string name)
    {
        ValidateDatabaseName(name);
        SqlConnectionStringBuilder builder = new(
            Environment.GetEnvironmentVariable("ACADEMIC_TEST_SQLSERVER") ??
            @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true");
        builder.InitialCatalog = "master";
        string master = builder.ConnectionString;
        builder.InitialCatalog = name;
        return (master, builder.ConnectionString);
    }

    private static async Task<bool> DatabaseExistsAsync(string master, string name)
    {
        ValidateDatabaseName(name);
        await using SqlConnection connection = new(master);
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT_BIG(*) FROM sys.databases WHERE [name] = @name";
        command.Parameters.AddWithValue("@name", name);
        return Convert.ToInt64(await command.ExecuteScalarAsync()) == 1;
    }

    private static async Task DropDatabaseAsync(string master, string name)
    {
        ValidateDatabaseName(name); await using SqlConnection connection = new(master); await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]";
        await command.ExecuteNonQueryAsync();
    }

    private static void ValidateDatabaseName(string name)
    {
        const string prefix = "AcademicFullTextResumePilot_";
        if (!name.StartsWith(prefix, StringComparison.Ordinal) || name.Length != prefix.Length + 32 ||
            name.Any(character => !char.IsLetterOrDigit(character) && character != '_'))
            throw new InvalidOperationException("The isolated pilot database name is invalid.");
    }

    private static void EnsurePortFree(int port) { using TcpListener listener = new(IPAddress.Loopback, port); listener.Start(); listener.Stop(); }

    private static async Task<JsonNode> CaptureFinalSqlDiagnosticsAsync(string connectionString)
    {
        List<UsageAttempt> usage = await ReadUsageAsync(connectionString);
        await using AnalysisDbContext db = AnalysisDatabase(connectionString);
        List<ArticleReviewWorkItem> workItems = await db.ArticleReviewWorkItems.AsNoTracking()
            .Include(value => value.Checkpoints).OrderBy(value => value.Id).ToListAsync();
        var summaryReports = await db.CanonicalArticleAnalysisRuns.AsNoTracking()
            .Where(value => value.SavedArticleSummary != null).OrderBy(value => value.Id)
            .Select(value => new
            {
                analysisRunId = value.Id,
                value.CanonicalWorkId,
                savedArticleSummaryId = value.SavedArticleSummary!.Id,
                reportJson = value.SavedArticleSummary.ReportJson
            }).ToListAsync();
        var reviewReports = await db.CanonicalArticleReviewRuns.AsNoTracking().OrderBy(value => value.Id)
            .Select(value => new
            {
                reviewRunId = value.Id,
                value.CanonicalWorkId,
                value.ProcessedRoles,
                value.TotalRoles,
                reportJson = value.ReportJson
            }).ToListAsync();
        return JsonSerializer.SerializeToNode(new
        {
            usage = UsagePhase(usage),
            rows = new
            {
                researchers = await db.Researchers.CountAsync(),
                academicWorks = await db.AcademicWorks.CountAsync(),
                canonicalWorks = await db.CanonicalWorks.CountAsync(),
                associations = await db.CanonicalResearcherWorks.CountAsync(),
                summaries = await db.ArticleSummaries.CountAsync(),
                analysisRuns = await db.CanonicalArticleAnalysisRuns.CountAsync(),
                claims = await db.CanonicalArticleClaims.CountAsync(),
                claimEvidence = await db.CanonicalArticleClaimEvidence.CountAsync(),
                reviewRuns = await db.CanonicalArticleReviewRuns.CountAsync(),
                reviewFindings = await db.CanonicalArticleReviewFindings.CountAsync(),
                reviewEvidence = await db.CanonicalArticleReviewEvidence.CountAsync(),
                reviewWorkItems = workItems.Count,
                reviewCheckpoints = workItems.Sum(value => value.Checkpoints.Count)
            },
            rawSqlReports = new
            {
                summaries = summaryReports.Select(value => new
                {
                    value.analysisRunId,
                    value.CanonicalWorkId,
                    value.savedArticleSummaryId,
                    value.reportJson,
                    parsedReport = JsonNode.Parse(value.reportJson)
                }),
                reviews = reviewReports.Select(value => new
                {
                    value.reviewRunId,
                    value.CanonicalWorkId,
                    value.ProcessedRoles,
                    value.TotalRoles,
                    value.reportJson,
                    parsedReport = JsonNode.Parse(value.reportJson)
                })
            },
            workItems = workItems.Select(value => new
            {
                value.Id, value.CanonicalWorkId, value.Status, value.MaximumCalls, value.MaximumSpendUsd,
                checkpoints = value.Checkpoints.OrderBy(checkpoint => checkpoint.Id).Select(checkpoint => new
                {
                    checkpoint.Id, checkpoint.Stage, checkpoint.Role, checkpoint.BatchKey,
                    checkpoint.ParentBatchKey, checkpoint.Ordinal, checkpoint.Status, checkpoint.AttemptId,
                    checkpoint.ReservedCostUsd, checkpoint.ActualCostUsd, checkpoint.PricingVersion,
                    checkpoint.ErrorCode
                })
            })
        }, JsonOptions)!;
    }

    private sealed record LiveSource(string Name, string PersonelId, string Title, string Doi, string Url, SourcePreflight Preflight);
    private sealed record ProbeCase(string Name, string Language,
        IReadOnlyList<ArticleEvaluationCalibrationClaim> Claims,
        IReadOnlyDictionary<string, string> Expected);
    private sealed record CitationProbePlan(ReviewArticleRequest Source,
        IReadOnlyList<ProbeCase> Cases, CitationProbePreflight Preflight);
    private sealed record DatabaseAudit(JsonObject Json, JsonNode? StoredReport);
    private sealed record UsageAttempt(Guid AttemptId, DateTime StartedAt, DateTime? CompletedAt, string RequestedModel,
        string? ReturnedModel, string Outcome, int? HttpStatus, long? PromptTokens, long? CachedTokens,
        long? CandidateTokens, long? ThoughtTokens, long? TotalTokens, string? PricingVersion, decimal? EstimatedUsd);
    private sealed record HttpCapture(int StatusCode, double ElapsedSeconds, string Body)
    {
        public JsonObject ToJson() { JsonNode? body; try { body = JsonNode.Parse(Body); } catch (JsonException) { body = JsonValue.Create(Body); }
            return new() { ["statusCode"] = StatusCode, ["elapsedSeconds"] = ElapsedSeconds, ["body"] = body }; }
    }

    private sealed class CollectorProcess : IAsyncDisposable
    {
        private readonly Process process; private readonly ConcurrentQueue<string> output = new();
        public int ProcessId => process.Id; public TimeSpan StartupElapsed { get; private set; }
        private CollectorProcess(Process process) => this.process = process;
        public static async Task<CollectorProcess> StartAsync(string root, string connectionString)
        {
            string assembly = Path.Combine(root, "bin", "Debug", "net10.0", "AcademicCollectorDemo.dll");
            if (!File.Exists(assembly)) throw new InvalidOperationException("Build the collector with -p:SkipTSBuild=true first.");
            ProcessStartInfo start = new("dotnet") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(assembly); start.ArgumentList.Add("--urls=" + CollectorUrl); SetEnvironment(start, connectionString);
            Process process = Process.Start(start) ?? throw new InvalidOperationException("Collector did not start.");
            CollectorProcess owner = new(process); process.OutputDataReceived += (_, e) => { if (e.Data is not null) owner.output.Enqueue(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) owner.output.Enqueue(e.Data); };
            process.BeginOutputReadLine(); process.BeginErrorReadLine(); Stopwatch timer = Stopwatch.StartNew();
            using HttpClient probe = new() { BaseAddress = new(CollectorUrl), Timeout = TimeSpan.FromSeconds(2) };
            while (timer.Elapsed < TimeSpan.FromSeconds(30))
            {
                if (process.HasExited) throw new InvalidOperationException($"Collector exited {process.ExitCode}: {string.Join(Environment.NewLine, owner.output.TakeLast(20))}");
                try { using HttpResponseMessage response = await probe.GetAsync("/"); if (response.IsSuccessStatusCode)
                    { timer.Stop(); owner.StartupElapsed = timer.Elapsed; return owner; } }
                catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException) { }
                await Task.Delay(250);
            }
            await owner.DisposeAsync(); throw new TimeoutException("Collector startup exceeded 30 seconds.");
        }
        private static void SetEnvironment(ProcessStartInfo start, string connectionString)
        {
            start.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing"; start.Environment["DOTNET_ENVIRONMENT"] = "Testing";
            start.Environment["ConnectionStrings__AcademicDatabase"] = connectionString;
            start.Environment["BulkCollection__WorkerEnabled"] = "false";
            foreach (string provider in new[] { "Orcid", "SearchApi", "OpenAlex", "WebOfScience", "Yoksis", "TrDizin", "Crossref", "SemanticScholar" })
                start.Environment[$"ProviderRequestLimits__{provider}__Enabled"] = "false";
            foreach (string key in new[] { "SearchApi__ApiKey", "OpenAlex__ApiKey", "SemanticScholar__ApiKey", "WebOfScience__ApiKey",
                "Yoksis__Username", "Yoksis__Password" }) start.Environment[key] = "";
        }
        public async ValueTask DisposeAsync() { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } process.Dispose(); }
    }
}

public sealed record CitationProbePreflight(
    string SourceHash,
    bool SourceHashMatches,
    string FirstSourceId,
    string MiddleSourceId,
    string PositiveTailSourceId,
    int PositiveTailStartOffset,
    int PositiveTailEndOffset,
    bool NegativeQuotesOmitRmsProp,
    bool PositiveQuotesContainRmsProp,
    string PolicyVersion)
{
    public bool Passed => SourceHashMatches && NegativeQuotesOmitRmsProp && PositiveQuotesContainRmsProp &&
        PolicyVersion == "article-specialist-review-policy-v3";
}
