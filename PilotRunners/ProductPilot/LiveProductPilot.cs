using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AcademicCollector.Analysis.Contracts;
using AcademicCollectorDemo.Host;
using AcademicCollectorDemo.Modules.AcademicPerformance;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleReviews;
using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.FacultyAssistant;
using AcademicCollectorDemo.Modules.AcademicPerformance.HrDossiers;
using AcademicCollectorDemo.Modules.AcademicPerformance.Knowledge;
using AcademicCollectorDemo.Modules.AcademicPerformance.Metrics;
using AcademicCollectorDemo.Modules.AcademicPerformance.ProductAccess;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Configuration;
using ResearcherAnalysisService.Data;
using ResearcherAnalysisService.Integrations.Gemini;
using Serenity;
using Serenity.Extensions.DependencyInjection;

namespace ProductPilot;

internal static class LiveProductPilot
{
    private const string RunId = "product-pilot-20260913";
    private const string RetestRunId = "product-pilot-20260913-retest3";
    private const string AnalysisUrl = "http://127.0.0.1:5098";
    private const string CollectorUrl = "http://127.0.0.1:5198";
    private const string ServiceKey = "product-pilot-synthetic-analysis-key";
    private const string Api = "/Services/AcademicPerformance/V1/";
    private const string PrivateSentinel = "PRIVATE_PRODUCT_PILOT_CONTEXT_7F2A";
    private const string AdamPdfHash = "935a5a15616961aff21529d86a754570028843407adfe858f1d18584b84293a7";
    private const string AdamSourceHash = "ef1663dd32671bce737af77d0cfd0777fd8ebd95bb86f5d0ff2b4aa457675fa3";
    private const string PriorResultHash = "a76adc07c0c7e11f4fd4e3cd66c95d6c02c39dc0d2ae0ef3dd28812ab02b67f5";
    private const string PriorPhase019Hash = "0f3ba725b72ad4584c1a0975709af85084269b0915136cb92757e469f63bd6d4";
    private const string OriginalLiveResultHash = "3a1723af64ff4f226911e75e631f88d6a3e1e5b29dfac8d28733b36ab9b63246";
    private const string OriginalLiveBudgetHash = "f36d96e5afcbb589ee30d21d5c00ae0cc677480efb15c78d9abc6e973ec25c82";
    private const string OriginalLiveFinalPhaseHash = "08f7cbaca789de3bcb9c27d29cf7f68bc6c38ad271efaa22dfba82ecc60c8e76";
    private const string Retest2ResultHash = "7984334e54a052628ed2ea0e36dc5e73d3a1b490741c1ac417705c9c77f83a19";
    private const string Retest2BudgetHash = "c89567f265babb77b3008bd6966d01b6ac0b4e6b5e80f6005358776fc4540b34";
    private const string Retest2FinalPhaseHash = "5a18cc62a55b3a8041c04e2647037d9e462c9031fd47fd8a216b2ec44f74e001";
    private const string MethodsQuery = "Adam makalesindeki same parameter initialization ve hyper-parameters dense grid deney yöntemini kendi yöntem bölümümü geliştirmek için nasıl kullanabilirim?";
    private const string TeachingQuery = "Rprop special case, zero memory ve bias correction bağlantısını optimizasyon dersinde nasıl bir türetim etkinliğine dönüştürebilirim?";
    private static readonly Guid OriginalMethodsClientRequestId = Guid.Parse("77f67669-6b3d-43fd-96ca-a083374b08d2");
    private static readonly Guid OriginalTeachingClientRequestId = Guid.Parse("3433e54d-1c2b-4bc2-950d-27e247e70413");
    private static readonly Guid PreviousRetestMethodsClientRequestId = Guid.Parse("60d08843-a8ea-4756-9df0-90ab0f55ad3d");
    private static readonly Guid PreviousRetestTeachingClientRequestId = Guid.Parse("aca472dd-94d1-4d94-bc40-5c556278f67b");
    private static readonly Guid RetestMethodsClientRequestId = Guid.Parse("7c1eaa85-ba05-4626-b77f-8a4bf2d298a7");
    private static readonly Guid RetestTeachingClientRequestId = Guid.Parse("1d614cfe-4963-4eef-a1a4-222ab083f71c");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    internal static void RunIdentitySelfTest()
    {
        Guid[] ids = [OriginalMethodsClientRequestId, OriginalTeachingClientRequestId,
            PreviousRetestMethodsClientRequestId, PreviousRetestTeachingClientRequestId,
            RetestMethodsClientRequestId, RetestTeachingClientRequestId];
        if (ids.Distinct().Count() != ids.Length)
            throw new InvalidOperationException("Original and corrected client request IDs must be distinct.");
        Guid run = Guid.Parse("b1f5dd5c-8eb8-43e1-b343-6624910cfc99");
        if (!IsFreshExecution(RetestMethodsClientRequestId, run, false,
                RetestMethodsClientRequestId, run, new HashSet<Guid> { Guid.NewGuid() }) ||
            IsFreshExecution(RetestMethodsClientRequestId, run, true,
                RetestMethodsClientRequestId, run, new HashSet<Guid>()) ||
            IsFreshExecution(RetestMethodsClientRequestId, run, false,
                OriginalMethodsClientRequestId, run, new HashSet<Guid>()))
            throw new InvalidOperationException("Fresh response identity checks failed.");
    }

    public static async Task<int> RunPreflightAsync()
    {
        string root = FindRepositoryRoot();
        string name = "AcademicProductPilotPreflight_" + Guid.NewGuid().ToString("N");
        (string master, string database) = await CreateDatabaseAsync(name, "AcademicProductPilotPreflight_");
        WebApplication? collector = null;
        JsonObject result = new() { ["checkedAtUtc"] = DateTime.UtcNow, ["networkPolicy"] =
            "Gemini/provider dispatch physically absent; faculty worker disabled." };
        try
        {
            collector = await StartCollectorAsync(root, database, "http://127.0.0.1:5199", false);
            SourceFixture fixture = await SeedAsync(root, database);
            using HttpClient client = Client("http://127.0.0.1:5199");
            JsonObject checks = await RunOfflineChecksAsync(client, collector.Services, database, fixture);
            ProductPilotBudgetState emptyBudget = new(_ => Task.CompletedTask, 16, 1m);
            JsonObject zeroReconciliation = await ReconcileAsync(database, emptyBudget.Snapshot());
            result["source"] = fixture.Preflight;
            result["checks"] = checks;
            result["zeroUsageSqlGuardReconciliation"] = zeroReconciliation;
            result["geminiApiKeyPresent"] = HasGeminiKey();
            result["plannedAiForRelease"] = JsonSerializer.SerializeToNode(new
            {
                articleProvider = "Gemini", articleModel = ProductPilotBudgetState.RequiredModel,
                articleVerifierModel = ProductPilotBudgetState.RequiredModel,
                generationThinkingLevel = "high", verifierThinkingLevel = "high",
                articleGenerationMaxOutputTokens = 8192, facultyGenerationMaxOutputTokens = 16384,
                verifierMaxOutputTokens = 8192
            });
            result["passed"] = checks["passed"]?.GetValue<bool>() == true &&
                zeroReconciliation["passed"]?.GetValue<bool>() == true && HasGeminiKey();
        }
        catch (Exception exception)
        {
            result["passed"] = false;
            result["failure"] = SafeFailure(exception);
        }
        finally
        {
            if (collector is not null) { await collector.StopAsync(); await collector.DisposeAsync(); }
            await DropDatabaseAsync(master, name, "AcademicProductPilotPreflight_");
        }
        string path = Path.Combine(root, "PilotRunners", "ProductPilot", "preflight-20260913-result.json");
        await File.WriteAllTextAsync(path, result.ToJsonString(JsonOptions) + Environment.NewLine);
        Console.WriteLine(result.ToJsonString(JsonOptions));
        return result["passed"]?.GetValue<bool>() == true ? 0 : 1;
    }

    public static async Task<int> RunRetestPreflightAsync()
    {
        string root = FindRepositoryRoot();
        JsonObject result = new()
        {
            ["checkedAtUtc"] = DateTime.UtcNow,
            ["networkPolicy"] = "Gemini dispatch denied by the innermost HTTP handler; faculty worker disabled."
        };
        WebApplication? collector = null;
        WebApplication? analysis = null;
        try
        {
            RetestBaseline baseline = await LoadRetestBaselineAsync(root);
            ProductPilotBudgetState restored = new(_ => Task.CompletedTask, 16, 1m, baseline.Budget);
            JsonObject priorReconciliation = await ReconcileAsync(baseline.Database, restored.Snapshot());
            if (priorReconciliation["passed"]?.GetValue<bool>() != true)
                throw new InvalidOperationException("The original SQL usage does not reconcile to the frozen guard ledger.");
            collector = await StartCollectorAsync(root, baseline.Database, "http://127.0.0.1:5199", false);
            using HttpClient client = Client("http://127.0.0.1:5199");
            JsonObject readOnly = await RunRetestReadOnlyChecksAsync(client, collector.Services, baseline);
            await collector.StopAsync(); await collector.DisposeAsync(); collector = null;

            FacultyProviderCapture capture = new();
            analysis = await StartAnalysisAsync(baseline.Database, restored, capture, false);
            AiOptions resolved = analysis.Services.GetRequiredService<IOptions<AiOptions>>().Value;
            JsonObject effective = EffectiveAi(resolved);
            bool exactConfiguration = ExactRetestAi(resolved);
            if (!exactConfiguration)
                throw new InvalidOperationException("The resolved retest analysis settings do not match the reviewed configuration.");
            JsonObject finalReconciliation = await ReconcileAsync(baseline.Database, restored.Snapshot());
            bool originalsFrozen = ValidateOriginalLiveArtifactHashes(root);
            result["databaseName"] = baseline.DatabaseName;
            result["originalArtifacts"] = baseline.ArtifactEvidence;
            result["restoredBudget"] = JsonSerializer.SerializeToNode(restored.Snapshot(), JsonOptions);
            result["priorSqlGuardReconciliation"] = priorReconciliation;
            result["readOnlyApiChecks"] = readOnly;
            result["analysisEffectiveConfiguration"] = effective;
            result["exactConfiguration"] = exactConfiguration;
            result["postPreflightSqlGuardReconciliation"] = finalReconciliation;
            result["originalArtifactsStillFrozen"] = originalsFrozen;
            result["freshClientRequestIds"] = JsonSerializer.SerializeToNode(new
            {
                ownPaperMethods = RetestMethodsClientRequestId, teachingHelp = RetestTeachingClientRequestId,
                absentBeforeWorker = await RetestClientRequestIdsAbsentAsync(baseline.Database)
            }, JsonOptions);
            result["passed"] = readOnly["passed"]?.GetValue<bool>() == true &&
                finalReconciliation["passed"]?.GetValue<bool>() == true && originalsFrozen &&
                await RetestClientRequestIdsAbsentAsync(baseline.Database) &&
                restored.Snapshot().Calls == 4 && restored.Snapshot().CommittedSpendUsd == 0.130186500m;
        }
        catch (Exception exception)
        {
            result["passed"] = false; result["failure"] = SafeFailure(exception);
        }
        finally
        {
            if (collector is not null) { await collector.StopAsync(); await collector.DisposeAsync(); }
            if (analysis is not null) { await analysis.StopAsync(); await analysis.DisposeAsync(); }
        }
        string path = Path.Combine(root, "PilotRunners", "ProductPilot", "retest-preflight-20260913-result.json");
        await File.WriteAllTextAsync(path, result.ToJsonString(JsonOptions) + Environment.NewLine);
        Console.WriteLine(result.ToJsonString(JsonOptions));
        return result["passed"]?.GetValue<bool>() == true ? 0 : 1;
    }

    public static async Task<int> RunRetestAsync()
    {
        string root = FindRepositoryRoot();
        RetestBaseline baseline = await LoadRetestBaselineAsync(root);
        ProductPilotArtifacts artifacts = new(root, RetestRunId);
        artifacts.EnsureNew();
        ProductPilotBudgetState budget = new(artifacts.WriteBudgetAsync, 16, 1m, baseline.Budget);
        JsonObject state = new()
        {
            ["pilot"] = RetestRunId, ["startedAtUtc"] = DateTime.UtcNow,
            ["databaseName"] = baseline.DatabaseName,
            ["limits"] = JsonSerializer.SerializeToNode(new { maximumCalls = 16, maximumSpendUsd = 1m }),
            ["carriedForward"] = JsonSerializer.SerializeToNode(new
            {
                calls = baseline.Budget.Calls, spendUsd = baseline.Budget.CommittedSpendUsd,
                remainingCalls = 16 - baseline.Budget.Calls, remainingSpendUsd = 1m - baseline.Budget.CommittedSpendUsd
            }),
            ["originalArtifacts"] = baseline.ArtifactEvidence,
            ["externalApi"] = "POST https://generativelanguage.googleapis.com/v1beta/models/gemini-3.8-flash:generateContent",
            ["status"] = "initialized"
        };
        WebApplication? collector = null;
        WebApplication? analysis = null;
        bool success = false;
        await using ProductPilotHeartbeat heartbeat = new(artifacts, budget, baseline.DatabaseName);
        try
        {
            JsonObject restoredReconciliation = await ReconcileAsync(baseline.Database, budget.Snapshot());
            if (restoredReconciliation["passed"]?.GetValue<bool>() != true)
                throw new InvalidOperationException("The restored budget failed SQL reconciliation before retest.");
            await artifacts.WriteBudgetAsync(budget.Snapshot());
            state["restoredSqlGuardReconciliation"] = restoredReconciliation;
            await artifacts.WritePhaseAsync("restored-before-read-only-gates", state, budget.Snapshot());

            collector = await StartCollectorAsync(root, baseline.Database, CollectorUrl, false);
            using HttpClient client = Client(CollectorUrl);
            state["readOnlyApiChecks"] = await RunRetestReadOnlyChecksAsync(client, collector.Services, baseline);
            if (state["readOnlyApiChecks"]?["passed"]?.GetValue<bool>() != true)
                throw new InvalidOperationException("Same-database read-only API gates failed.");
            if ((await ReconcileAsync(baseline.Database, budget.Snapshot()))["passed"]?.GetValue<bool>() != true)
                throw new InvalidOperationException("Read-only gates changed Gemini usage.");
            if (!await RetestClientRequestIdsAbsentAsync(baseline.Database))
                throw new InvalidOperationException("A corrected client request ID already exists before worker enable.");
            state["freshClientRequestIds"] = JsonSerializer.SerializeToNode(new
            {
                ownPaperMethods = RetestMethodsClientRequestId, teachingHelp = RetestTeachingClientRequestId,
                absentBeforeWorker = true
            }, JsonOptions);
            await artifacts.WritePhaseAsync("read-only-gates-passed", state, budget.Snapshot());
            await collector.StopAsync(); await collector.DisposeAsync(); collector = null;

            FacultyProviderCapture capture = new();
            analysis = await StartAnalysisAsync(baseline.Database, budget, capture, true);
            AiOptions resolved = analysis.Services.GetRequiredService<IOptions<AiOptions>>().Value;
            state["analysisEffectiveConfiguration"] = EffectiveAi(resolved);
            if (!ExactRetestAi(resolved))
                throw new InvalidOperationException("Resolved analysis settings do not match the released retest configuration.");
            state["status"] = "retest-live-dispatch-enabled";
            await artifacts.WritePhaseAsync("before-first-retest-outbound", state, budget.Snapshot());
            collector = await StartCollectorAsync(root, baseline.Database, CollectorUrl, true);

            JsonArray scenarios = []; state["facultyScenarios"] = scenarios;
            foreach ((string mode, string query, Guid clientRequestId) in new[]
            {
                ("OwnPaperMethods", MethodsQuery, RetestMethodsClientRequestId),
                ("TeachingHelp", TeachingQuery, RetestTeachingClientRequestId)
            })
            {
                heartbeat.Set(mode);
                JsonObject scenario = await RunFacultyScenarioAsync(client, baseline.Database,
                    baseline.Fixture.CanonicalWorkId, baseline.Fixture.ContextVersion, mode, query,
                    clientRequestId, baseline.OriginalRunIds, budget);
                scenarios.Add(scenario);
                state["providerCapture"] = JsonSerializer.SerializeToNode(capture.Snapshot(), JsonOptions);
                state["usage"] = await ReadUsageJsonAsync(baseline.Database);
                await artifacts.WritePhaseAsync(mode.ToLowerInvariant(), state, budget.Snapshot());
                if (budget.Snapshot().DispatchStopped) break;
            }
            state["providerCapture"] = JsonSerializer.SerializeToNode(capture.Snapshot(), JsonOptions);
            JsonObject captureAudit = AuditProviderCapture(capture);
            state["providerCaptureAudit"] = captureAudit;
            state["usage"] = await ReadUsageJsonAsync(baseline.Database);
            state["sqlAudit"] = await SqlAuditAsync(baseline.Database, baseline.Fixture);
            JsonObject reconciliation = await ReconcileAsync(baseline.Database, budget.Snapshot());
            state["budgetSqlReconciliation"] = reconciliation;
            ProductPilotBudgetSnapshot finalBudget = budget.Snapshot();
            bool originalRunsStillFrozen = await OriginalFailedRunsIntactAsync(baseline.Database, baseline.OriginalRunIds);
            bool correctedReportsPersisted = await CorrectedReportsPersistedAsync(baseline.Database);
            state["originalFailedRunsStillFrozen"] = originalRunsStillFrozen;
            state["correctedReportsPersistedWithExactModels"] = correctedReportsPersisted;
            state["originalArtifactsStillFrozen"] = ValidateOriginalLiveArtifactHashes(root);
            success = scenarios.Count == 2 && scenarios.All(value =>
                    value?["passed"]?.GetValue<bool>() == true &&
                    value?["freshCorrectedExecution"]?.GetValue<bool>() == true) &&
                reconciliation["passed"]?.GetValue<bool>() == true && originalRunsStillFrozen &&
                correctedReportsPersisted && captureAudit["passed"]?.GetValue<bool>() == true &&
                state["originalArtifactsStillFrozen"]?.GetValue<bool>() == true &&
                !finalBudget.DispatchStopped && finalBudget.Calls <= 16 && finalBudget.CommittedSpendUsd <= 1m &&
                finalBudget.Items.All(value => value.Completed && value.ActualUsageReliable &&
                    value.RequestedModel == ProductPilotBudgetState.RequiredModel &&
                    value.ReturnedModel == ProductPilotBudgetState.RequiredModel);
            state["status"] = success ? "validation_complete" : "validation_failed";
        }
        catch (Exception exception)
        {
            state["failure"] = SafeFailure(exception); state["status"] = "preserved_for_audit";
        }
        finally
        {
            try { state["finalSqlAudit"] = await SqlAuditAsync(baseline.Database, null); }
            catch (Exception exception) { state["sqlAuditFailure"] = exception.Message; success = false; }
            if (collector is not null) { await collector.StopAsync(); await collector.DisposeAsync(); }
            if (analysis is not null) { await analysis.StopAsync(); await analysis.DisposeAsync(); }
            if (success)
            {
                await DropDatabaseAsync(baseline.Master, baseline.DatabaseName, "AcademicProductPilot_");
                state["databaseDropped"] = true; state["databasePreserved"] = false;
            }
            else
            {
                state["databaseDropped"] = false; state["databasePreserved"] = true;
            }
            state["operationalSuccess"] = success; state["finishedAtUtc"] = DateTime.UtcNow;
            await artifacts.WriteFinalAsync(state, budget.Snapshot());
        }
        Console.WriteLine(state.ToJsonString(JsonOptions));
        return success ? 0 : 1;
    }

    private static async Task<RetestBaseline> LoadRetestBaselineAsync(string root)
    {
        string originalDirectory = Path.Combine(root, "docs", RunId);
        string resultPath = Path.Combine(originalDirectory, "result.json");
        string originalBudgetPath = Path.Combine(originalDirectory, "budget.json");
        string finalPhasePath = Path.Combine(originalDirectory, "phases", "006-final.json");
        string retest2Directory = Path.Combine(root, "docs", "product-pilot-20260913-retest2");
        string retest2ResultPath = Path.Combine(retest2Directory, "result.json");
        string budgetPath = Path.Combine(retest2Directory, "budget.json");
        string retest2FinalPhasePath = Path.Combine(retest2Directory, "phases", "006-final.json");
        bool hashesValid = Hash(await File.ReadAllBytesAsync(resultPath)) == OriginalLiveResultHash &&
            Hash(await File.ReadAllBytesAsync(originalBudgetPath)) == OriginalLiveBudgetHash &&
            Hash(await File.ReadAllBytesAsync(finalPhasePath)) == OriginalLiveFinalPhaseHash &&
            Hash(await File.ReadAllBytesAsync(retest2ResultPath)) == Retest2ResultHash &&
            Hash(await File.ReadAllBytesAsync(budgetPath)) == Retest2BudgetHash &&
            Hash(await File.ReadAllBytesAsync(retest2FinalPhasePath)) == Retest2FinalPhaseHash;
        if (!hashesValid)
            throw new InvalidOperationException("The frozen original live artifacts changed.");
        JsonObject original = JsonNode.Parse(await File.ReadAllTextAsync(resultPath))?.AsObject() ??
            throw new InvalidOperationException("The original live result is invalid.");
        string databaseName = original["databaseName"]?.GetValue<string>() ?? string.Empty;
        if (original["status"]?.GetValue<string>() != "validation_failed" ||
            original["operationalSuccess"]?.GetValue<bool>() != false ||
            original["databasePreserved"]?.GetValue<bool>() != true)
            throw new InvalidOperationException("The original failed run state is not the frozen expected state.");
        ValidateDatabaseName(databaseName, "AcademicProductPilot_");
        (string master, string database) = ConnectionStrings(databaseName);
        ProductPilotBudgetSnapshot ledger = JsonSerializer.Deserialize<ProductPilotBudgetSnapshot>(
            await File.ReadAllTextAsync(budgetPath), JsonOptions) ??
            throw new InvalidOperationException("The original budget ledger is invalid.");
        _ = new ProductPilotBudgetState(_ => Task.CompletedTask, 16, 1m, ledger);
        if (ledger.Calls != 4 || ledger.CommittedSpendUsd != 0.130186500m ||
            ledger.Items.Take(2).Any(value => value.MaximumOutputTokens != 8192) ||
            ledger.Items.Skip(2).Any(value => value.MaximumOutputTokens != 16384))
            throw new InvalidOperationException("The four-call cumulative budget does not match the reviewed failures.");

        JsonArray usage = (await ReadUsageJsonAsync(database)).AsArray();
        decimal usageSpend = usage.OfType<JsonObject>().Sum(value => value["estimatedUsd"]!.GetValue<decimal>());
        if (usage.Count != 4 || usageSpend != 0.130186500m || usage.OfType<JsonObject>().Any(value =>
                value["requestedModel"]?.GetValue<string>() != ProductPilotBudgetState.RequiredModel ||
                value["returnedModel"]?.GetValue<string>() != ProductPilotBudgetState.RequiredModel ||
                value["httpStatus"]?.GetValue<int>() != 200) ||
            usage[0]?["outcome"]?.GetValue<string>() != "OutputLimit" ||
            usage[1]?["outcome"]?.GetValue<string>() != "OutputLimit" ||
            usage[2]?["outcome"]?.GetValue<string>() != "Success" ||
            usage[3]?["outcome"]?.GetValue<string>() != "Success")
            throw new InvalidOperationException("The cumulative SQL usage rows do not match the reviewed evidence.");

        await using AcademicDbContext db = Database(database);
        ArticleSourceSnapshot source = await db.ArticleSourceSnapshots.AsNoTracking()
            .Include(value => value.Pages).Include(value => value.Spans).SingleAsync();
        FacultyAssistantContextVersion context = await db.FacultyAssistantContextVersions.AsNoTracking()
            .SingleAsync(value => value.PersonelId == PilotAccessService.SubjectId);
        HrEvidenceDossier dossier = await db.HrEvidenceDossiers.AsNoTracking()
            .SingleAsync(value => value.PersonelId == PilotAccessService.SubjectId);
        List<FacultyAssistantRun> originalRuns = await db.FacultyAssistantRuns.AsNoTracking()
            .OrderBy(value => value.CreatedAt).ToListAsync();
        string? originalMethodsQuery = originalRuns.Count > 0
            ? JsonSerializer.Deserialize<StartFacultyAssistantRequest>(originalRuns[0].RequestJson, JsonOptions)?.Query : null;
        string? originalTeachingQuery = originalRuns.Count > 1
            ? JsonSerializer.Deserialize<StartFacultyAssistantRequest>(originalRuns[1].RequestJson, JsonOptions)?.Query : null;
        bool exactOldRuns = originalRuns.Count == 4 &&
            originalRuns[0].Mode == "OwnPaperMethods" && originalMethodsQuery == MethodsQuery &&
            originalRuns[0].ClientRequestId == OriginalMethodsClientRequestId &&
            originalRuns[1].Mode == "TeachingHelp" && originalTeachingQuery == TeachingQuery &&
            originalRuns[1].ClientRequestId == OriginalTeachingClientRequestId &&
            originalRuns[2].Mode == "OwnPaperMethods" &&
            JsonSerializer.Deserialize<StartFacultyAssistantRequest>(originalRuns[2].RequestJson, JsonOptions)?.Query == MethodsQuery &&
            originalRuns[2].ClientRequestId == PreviousRetestMethodsClientRequestId &&
            originalRuns[3].Mode == "TeachingHelp" &&
            JsonSerializer.Deserialize<StartFacultyAssistantRequest>(originalRuns[3].RequestJson, JsonOptions)?.Query == TeachingQuery &&
            originalRuns[3].ClientRequestId == PreviousRetestTeachingClientRequestId &&
            originalRuns.All(value => value.Status == "Failed" && value.AttemptCount == 1 && value.ReportJson is null &&
                value.ErrorCode == "ProviderFailure" && value.ContextVersionId == context.Id);
        if (!exactOldRuns)
            throw new InvalidOperationException("The original failed faculty runs or exact questions changed.");
        if (source.ExtractedTextHash != AdamSourceHash || source.Pages.Count != 9 || source.Spans.Count != 64)
            throw new InvalidOperationException("The preserved source snapshot changed.");
        JsonObject artifactEvidence = new()
        {
            ["originalResultSha256"] = OriginalLiveResultHash, ["originalBudgetSha256"] = OriginalLiveBudgetHash,
            ["originalFinalPhaseSha256"] = OriginalLiveFinalPhaseHash,
            ["retest2ResultSha256"] = Retest2ResultHash, ["retest2BudgetSha256"] = Retest2BudgetHash,
            ["retest2FinalPhaseSha256"] = Retest2FinalPhaseHash, ["hashesMatch"] = true,
            ["originalCalls"] = ledger.Calls, ["originalSpendUsd"] = ledger.CommittedSpendUsd,
            ["originalOutcomes"] = new JsonArray("OutputLimit", "OutputLimit", "Success", "Success")
        };
        SourceFixture fixture = new(source.CanonicalWorkId, context.Version, new JsonObject
        {
            ["sourceHash"] = source.ExtractedTextHash, ["pages"] = source.Pages.Count,
            ["spans"] = source.Spans.Count, ["reusedPreservedSql"] = true
        }, dossier.Id, context.ContextFingerprint, dossier.InputFingerprint);
        return new(databaseName, master, database, ledger, fixture,
            originalRuns.Select(value => value.RunId).ToHashSet(), artifactEvidence);
    }

    private static async Task<JsonObject> RunRetestReadOnlyChecksAsync(HttpClient client, IServiceProvider services,
        RetestBaseline baseline)
    {
        RetestCounts before = await ReadRetestCountsAsync(baseline.Database);
        HttpCapture getContext = await PostAsync(client, Api + "GetFacultyAssistantContext",
            new { PersonelID = PilotAccessService.SubjectId, Version = baseline.Fixture.ContextVersion });
        HttpCapture getHr = await PostAsync(client, Api + "GetHrEvidenceDossier",
            new { PersonelID = PilotAccessService.SubjectId, DossierId = baseline.Fixture.DossierId });
        HttpCapture list = await PostAsync(client, Api + "ListHrDossierReviewActions",
            new { PersonelID = PilotAccessService.SubjectId, DossierId = baseline.Fixture.DossierId, Skip = 0, Take = 100 });
        FacultyAssistantContextResponse context = Deserialize<FacultyAssistantContextResponse>(getContext);
        HrEvidenceDossierResponse dossier = Deserialize<HrEvidenceDossierResponse>(getHr);
        HrDossierReviewActionListResponse actions = Deserialize<HrDossierReviewActionListResponse>(list);
        using IServiceScope scope = services.CreateScope();
        IAcademicEvidenceSearchService search = scope.ServiceProvider.GetRequiredService<IAcademicEvidenceSearchService>();
        AcademicEvidenceSearchResponse methods = await search.SearchAsync(PilotAccessService.SubjectId, new()
        {
            Query = MethodsQuery, CanonicalWorkIds = [baseline.Fixture.CanonicalWorkId], Take = 10
        }, default);
        AcademicEvidenceSearchResponse teaching = await search.SearchAsync(PilotAccessService.SubjectId, new()
        {
            Query = TeachingQuery, CanonicalWorkIds = [baseline.Fixture.CanonicalWorkId], Take = 10
        }, default);
        RetestCounts after = await ReadRetestCountsAsync(baseline.Database);
        string hr = getHr.Body;
        bool oldRunsIntact = await OriginalFailedRunsIntactAsync(baseline.Database, baseline.OriginalRunIds);
        bool passed = getContext.StatusCode == 200 && getHr.StatusCode == 200 && list.StatusCode == 200 &&
            context.Version == baseline.Fixture.ContextVersion && context.Fingerprint == baseline.Fixture.ContextFingerprint &&
            context.Context.Preferences?.Contains(PrivateSentinel, StringComparison.Ordinal) == true &&
            dossier.DossierId == baseline.Fixture.DossierId && dossier.InputFingerprint == baseline.Fixture.DossierFingerprint &&
            dossier.Dossier.PublicationMetrics?.Data.CanonicalWorkCount == 1 && dossier.Dossier.Works.Count == 1 &&
            dossier.Dossier.Works.SelectMany(value => value.Reviews).SelectMany(value => value.Evidence).Any() &&
            actions.TotalCount == 1 && !hr.Contains(PrivateSentinel, StringComparison.Ordinal) &&
            !hr.Contains("RAW_PROVIDER_SENTINEL", StringComparison.Ordinal) && !hr.Contains("99999999999", StringComparison.Ordinal) &&
            methods.Hits.Count > 0 && teaching.Hits.Count > 0 && before == after && before.GeminiUsageAttempts == 4 &&
            oldRunsIntact;
        return new()
        {
            ["passed"] = passed,
            ["endpoints"] = JsonSerializer.SerializeToNode(new
            {
                getContext = getContext.StatusOnly(), getHr = getHr.StatusOnly(), listActions = list.StatusOnly()
            }, JsonOptions),
            ["context"] = JsonSerializer.SerializeToNode(new
            {
                version = context.Version, fingerprint = context.Fingerprint,
                privateSentinelPresentInContext = context.Context.Preferences?.Contains(PrivateSentinel, StringComparison.Ordinal) == true
            }, JsonOptions),
            ["hr"] = JsonSerializer.SerializeToNode(new
            {
                dossierId = dossier.DossierId, dossier.InputFingerprint,
                canonicalWorkCount = dossier.Dossier.PublicationMetrics?.Data.CanonicalWorkCount,
                reviewEvidenceCount = dossier.Dossier.Works.SelectMany(value => value.Reviews).SelectMany(value => value.Evidence).Count(),
                reviewActionCount = actions.TotalCount, privateContextExcluded = !hr.Contains(PrivateSentinel, StringComparison.Ordinal)
            }, JsonOptions),
            ["retrieval"] = JsonSerializer.SerializeToNode(new
            {
                methods = new { query = MethodsQuery, count = methods.Hits.Count },
                teaching = new { query = TeachingQuery, count = teaching.Hits.Count }
            }, JsonOptions),
            ["rowCountsUnchanged"] = before == after,
            ["rowCounts"] = JsonSerializer.SerializeToNode(after, JsonOptions),
            ["originalFailedRunsIntact"] = oldRunsIntact,
            ["geminiCallsUnchangedAtFour"] = after.GeminiUsageAttempts == 4
        };
    }

    private static async Task<RetestCounts> ReadRetestCountsAsync(string database)
    {
        await using AcademicDbContext db = Database(database);
        return new(await db.Researchers.CountAsync(), await db.CanonicalWorks.CountAsync(),
            await db.ArticleSourceSnapshots.CountAsync(), await db.ArticleSourceSpans.CountAsync(),
            await db.HrEvidenceDossiers.CountAsync(), await db.HrDossierReviewActions.CountAsync(),
            await db.FacultyAssistantContextVersions.CountAsync(), await db.FacultyAssistantRuns.CountAsync(),
            await CountUsageAsync(database));
    }

    private static async Task<bool> OriginalFailedRunsIntactAsync(string database, HashSet<Guid> originalRunIds)
    {
        await using AcademicDbContext db = Database(database);
        List<FacultyAssistantRun> runs = await db.FacultyAssistantRuns.AsNoTracking()
            .Where(value => originalRunIds.Contains(value.RunId)).ToListAsync();
        return runs.Count == originalRunIds.Count && runs.All(value => value.Status == "Failed" && value.AttemptCount == 1 &&
            value.ReportJson == null && value.ErrorCode == "ProviderFailure");
    }

    private static async Task<bool> RetestClientRequestIdsAbsentAsync(string database)
    {
        await using AcademicDbContext db = Database(database);
        return !await db.FacultyAssistantRuns.AsNoTracking().AnyAsync(value =>
            value.ClientRequestId == RetestMethodsClientRequestId ||
            value.ClientRequestId == RetestTeachingClientRequestId);
    }

    private static async Task<bool> CorrectedReportsPersistedAsync(string database)
    {
        await using AcademicDbContext db = Database(database);
        List<FacultyAssistantRun> runs = await db.FacultyAssistantRuns.AsNoTracking().Where(value =>
            value.ClientRequestId == RetestMethodsClientRequestId ||
            value.ClientRequestId == RetestTeachingClientRequestId).ToListAsync();
        return runs.Count == 2 && runs.All(value => value.Status == "Completed" && value.AttemptCount == 1 &&
            value.ReportJson is not null && ReadPersistedReport(value.ReportJson) is { } report &&
            report.Items.Count > 0 && report.Model == ProductPilotBudgetState.RequiredModel &&
            report.Verification.Model == ProductPilotBudgetState.RequiredModel);
    }

    private static FacultyAssistantAnalysisReport? ReadPersistedReport(string value)
    {
        try { return JsonSerializer.Deserialize<FacultyAssistantAnalysisReport>(value, JsonOptions); }
        catch (JsonException) { return null; }
    }

    private static JsonObject AuditProviderCapture(FacultyProviderCapture capture)
    {
        JsonArray entries = JsonSerializer.SerializeToNode(capture.Snapshot(), JsonOptions)?.AsArray() ?? [];
        JsonObject[] generations = entries.OfType<JsonObject>()
            .Where(value => value["stage"]?.GetValue<string>() == "generation").ToArray();
        JsonObject[] verifications = entries.OfType<JsonObject>()
            .Where(value => value["stage"]?.GetValue<string>() == "verification").ToArray();
        int[] generatedCounts = generations.Select(value => value["items"]?.AsArray().Count ?? -1).ToArray();
        int[] verdictCounts = verifications.Select(value => value["verdicts"]?.AsArray().Count ?? -1).ToArray();
        bool passed = generations.Length == 2 && verifications.Length == 2 &&
            generatedCounts.All(value => value > 0) && generatedCounts.SequenceEqual(verdictCounts) &&
            entries.OfType<JsonObject>().All(value =>
                value["model"]?.GetValue<string>() == ProductPilotBudgetState.RequiredModel);
        return new()
        {
            ["passed"] = passed, ["generationCount"] = generations.Length,
            ["verificationCount"] = verifications.Length,
            ["generatedItemCounts"] = JsonSerializer.SerializeToNode(generatedCounts, JsonOptions),
            ["verdictCounts"] = JsonSerializer.SerializeToNode(verdictCounts, JsonOptions)
        };
    }

    private static JsonObject EffectiveAi(AiOptions value) => new()
    {
        ["provider"] = value.ArticleProvider, ["requestedGenerationModel"] = value.ArticleModel,
        ["requestedVerifierModel"] = value.ArticleVerifierModel,
        ["generationThinking"] = value.ArticleGenerationThinkingLevel,
        ["verifierThinking"] = value.ArticleVerifierThinkingLevel,
        ["articleGenerationMaxOutputTokens"] = value.ArticleMaxOutputTokens,
        ["facultyGenerationMaxOutputTokens"] = value.FacultyAssistantMaxOutputTokens,
        ["verifierMaxOutputTokens"] = value.ArticleVerifierMaxOutputTokens,
        ["facultyPromptVersion"] = FacultyAssistantPrompt.Version,
        ["apiKeyLoadedInMemory"] = true
    };

    private static bool ExactRetestAi(AiOptions value) => value.ArticleProvider == "Gemini" &&
        value.ArticleModel == ProductPilotBudgetState.RequiredModel &&
        value.ArticleVerifierModel == ProductPilotBudgetState.RequiredModel &&
        value.ArticleGenerationThinkingLevel == "high" && value.ArticleVerifierThinkingLevel == "high" &&
        value.ArticleMaxOutputTokens == 8192 && value.FacultyAssistantMaxOutputTokens == 16384 &&
        value.ArticleVerifierMaxOutputTokens == 8192 && FacultyAssistantPrompt.Version == "faculty-evidence-assistant-v2";

    private static int ReadFacultyMaximumOutputTokens(AiOptions value) => value.FacultyAssistantMaxOutputTokens;

    private static bool ValidateOriginalLiveArtifactHashes(string root)
    {
        string directory = Path.Combine(root, "docs", RunId);
        string retest2 = Path.Combine(root, "docs", "product-pilot-20260913-retest2");
        return Hash(File.ReadAllBytes(Path.Combine(directory, "result.json"))) == OriginalLiveResultHash &&
            Hash(File.ReadAllBytes(Path.Combine(directory, "budget.json"))) == OriginalLiveBudgetHash &&
            Hash(File.ReadAllBytes(Path.Combine(directory, "phases", "006-final.json"))) == OriginalLiveFinalPhaseHash &&
            Hash(File.ReadAllBytes(Path.Combine(retest2, "result.json"))) == Retest2ResultHash &&
            Hash(File.ReadAllBytes(Path.Combine(retest2, "budget.json"))) == Retest2BudgetHash &&
            Hash(File.ReadAllBytes(Path.Combine(retest2, "phases", "006-final.json"))) == Retest2FinalPhaseHash;
    }

    public static async Task<int> RunLiveAsync()
    {
        string root = FindRepositoryRoot();
        ProductPilotArtifacts artifacts = new(root, RunId);
        artifacts.EnsureNew();
        string name = "AcademicProductPilot_" + Guid.NewGuid().ToString("N");
        (string master, string database) = await CreateDatabaseAsync(name, "AcademicProductPilot_");
        ProductPilotBudgetState budget = new(artifacts.WriteBudgetAsync, 16, 1m);
        JsonObject state = new()
        {
            ["pilot"] = RunId, ["startedAtUtc"] = DateTime.UtcNow, ["databaseName"] = name,
            ["limits"] = JsonSerializer.SerializeToNode(new { maximumCalls = 16, maximumSpendUsd = 1m }),
            ["externalApi"] = "POST https://generativelanguage.googleapis.com/v1beta/models/gemini-3.8-flash:generateContent",
            ["status"] = "initialized"
        };
        WebApplication? collector = null;
        WebApplication? analysis = null;
        bool success = false;
        await using ProductPilotHeartbeat heartbeat = new(artifacts, budget, name);
        try
        {
            await artifacts.WriteBudgetAsync(budget.Snapshot());
            await artifacts.WritePhaseAsync("initial", state, budget.Snapshot());
            collector = await StartCollectorAsync(root, database, CollectorUrl, false);
            SourceFixture fixture = await SeedAsync(root, database);
            state["source"] = fixture.Preflight;
            using HttpClient client = Client(CollectorUrl);
            state["offlineChecks"] = await RunOfflineChecksAsync(client, collector.Services, database, fixture);
            if (state["offlineChecks"]?["passed"]?.GetValue<bool>() != true)
                throw new InvalidOperationException("Offline endpoint and retrieval checks failed.");
            if (budget.Snapshot().Calls != 0 || await CountUsageAsync(database) != 0)
                throw new InvalidOperationException("Offline HR/context checks unexpectedly created Gemini usage.");
            state["offlineCreatedZeroGeminiCalls"] = true;
            await artifacts.WritePhaseAsync("offline-gates-passed", state, budget.Snapshot());

            await collector.StopAsync(); await collector.DisposeAsync(); collector = null;

            FacultyProviderCapture capture = new();
            analysis = await StartAnalysisAsync(database, budget, capture, true);
            AiOptions resolvedAi = analysis.Services.GetRequiredService<IOptions<AiOptions>>().Value;
            state["analysisEffectiveConfiguration"] = JsonSerializer.SerializeToNode(new
            {
                provider = resolvedAi.ArticleProvider, requestedGenerationModel = resolvedAi.ArticleModel,
                requestedVerifierModel = resolvedAi.ArticleVerifierModel,
                generationThinking = resolvedAi.ArticleGenerationThinkingLevel,
                verifierThinking = resolvedAi.ArticleVerifierThinkingLevel,
                articleGenerationMaxOutputTokens = resolvedAi.ArticleMaxOutputTokens,
                facultyGenerationMaxOutputTokens = ReadFacultyMaximumOutputTokens(resolvedAi),
                verifierMaxOutputTokens = resolvedAi.ArticleVerifierMaxOutputTokens,
                apiKeyLoadedInMemory = true
            });
            if (resolvedAi.ArticleProvider != "Gemini" || resolvedAi.ArticleModel != ProductPilotBudgetState.RequiredModel ||
                resolvedAi.ArticleVerifierModel != ProductPilotBudgetState.RequiredModel ||
                resolvedAi.ArticleGenerationThinkingLevel != "high" || resolvedAi.ArticleVerifierThinkingLevel != "high" ||
                resolvedAi.ArticleMaxOutputTokens != 8192 || ReadFacultyMaximumOutputTokens(resolvedAi) != 16384 ||
                resolvedAi.ArticleVerifierMaxOutputTokens != 8192)
                throw new InvalidOperationException("Resolved analysis settings do not match the released model configuration.");
            state["status"] = "live-dispatch-enabled";
            await artifacts.WritePhaseAsync("before-first-outbound", state, budget.Snapshot());
            collector = await StartCollectorAsync(root, database, CollectorUrl, true);

            JsonArray scenarios = [];
            state["facultyScenarios"] = scenarios;
            foreach ((string mode, string query) in new[]
            {
                ("OwnPaperMethods", MethodsQuery), ("TeachingHelp", TeachingQuery)
            })
            {
                heartbeat.Set(mode);
                Guid clientRequestId = mode == "OwnPaperMethods" ? OriginalMethodsClientRequestId : OriginalTeachingClientRequestId;
                JsonObject scenario = await RunFacultyScenarioAsync(client, database, fixture.CanonicalWorkId,
                    fixture.ContextVersion, mode, query, clientRequestId, new HashSet<Guid>(), budget);
                scenarios.Add(scenario);
                state["providerCapture"] = JsonSerializer.SerializeToNode(capture.Snapshot(), JsonOptions);
                state["usage"] = await ReadUsageJsonAsync(database);
                await artifacts.WritePhaseAsync(mode.ToLowerInvariant(), state, budget.Snapshot());
                if (budget.Snapshot().DispatchStopped) break;
            }
            state["providerCapture"] = JsonSerializer.SerializeToNode(capture.Snapshot(), JsonOptions);
            state["usage"] = await ReadUsageJsonAsync(database);
            state["sqlAudit"] = await SqlAuditAsync(database, fixture);
            JsonObject reconciliation = await ReconcileAsync(database, budget.Snapshot());
            state["budgetSqlReconciliation"] = reconciliation;
            ProductPilotBudgetSnapshot finalBudget = budget.Snapshot();
            success = scenarios.Count == 2 && scenarios.All(value =>
                    value?["passed"]?.GetValue<bool>() == true) &&
                reconciliation["passed"]?.GetValue<bool>() == true &&
                !finalBudget.DispatchStopped && finalBudget.Calls <= 16 && finalBudget.CommittedSpendUsd <= 1m &&
                finalBudget.Items.All(value => value.Completed && value.ActualUsageReliable &&
                    value.RequestedModel == ProductPilotBudgetState.RequiredModel &&
                    value.ReturnedModel == ProductPilotBudgetState.RequiredModel);
            state["status"] = success ? "validation_complete" : "validation_failed";
        }
        catch (Exception exception)
        {
            state["failure"] = SafeFailure(exception); state["status"] = "preserved_for_audit";
        }
        finally
        {
            try { state["finalSqlAudit"] = await SqlAuditAsync(database, null); }
            catch (Exception exception) { state["sqlAuditFailure"] = exception.Message; success = false; }
            if (collector is not null) { await collector.StopAsync(); await collector.DisposeAsync(); }
            if (analysis is not null) { await analysis.StopAsync(); await analysis.DisposeAsync(); }
            if (success)
            {
                await DropDatabaseAsync(master, name, "AcademicProductPilot_");
                state["databaseDropped"] = true; state["databasePreserved"] = false;
            }
            else
            {
                state["databaseDropped"] = false; state["databasePreserved"] = true;
            }
            state["operationalSuccess"] = success; state["finishedAtUtc"] = DateTime.UtcNow;
            await artifacts.WriteFinalAsync(state, budget.Snapshot());
        }
        Console.WriteLine(state.ToJsonString(JsonOptions));
        return success ? 0 : 1;
    }

    private static async Task<JsonObject> RunOfflineChecksAsync(HttpClient client, IServiceProvider services,
        string database, SourceFixture fixture)
    {
        (long metricSnapshotId, ResearcherPublicationMetricsResponse metricData) =
            await CreateMetricSnapshotAsync(services, database);
        HttpCapture saveContext = await PostAsync(client, Api + "SaveFacultyAssistantContext", new
        {
            PersonelID = PilotAccessService.SubjectId, ExpectedVersion = 0,
            Context = new { Language = "tr", ResearchGoals = new[] { "Adam optimizasyon yöntemini geliştirmek" },
                Courses = new[] { "Makine Öğrenmesi" }, TeachingAudience = "Lisansüstü öğrenciler",
                Preferences = "Kaynağa bağlı kısa yanıt. " + PrivateSentinel }
        });
        FacultyAssistantContextResponse saved = Deserialize<FacultyAssistantContextResponse>(saveContext);
        fixture.ContextVersion = saved.Version;
        HttpCapture getContext = await PostAsync(client, Api + "GetFacultyAssistantContext",
            new { PersonelID = PilotAccessService.SubjectId, Version = saved.Version });
        HttpCapture createHr = await PostAsync(client, Api + "CreateHrEvidenceDossier", new
            { PersonelID = PilotAccessService.SubjectId, PublicationMetricSnapshotId = metricSnapshotId,
                CanonicalWorkIds = new[] { fixture.CanonicalWorkId }, Language = "tr" });
        HrEvidenceDossierResponse dossier = Deserialize<HrEvidenceDossierResponse>(createHr);
        HttpCapture getHr = await PostAsync(client, Api + "GetHrEvidenceDossier",
            new { PersonelID = PilotAccessService.SubjectId, dossier.DossierId });
        Guid actionId = Guid.Parse("8ee6b75f-0dc8-4ed8-991e-2d95c3e958cb");
        object actionBody = new { PersonelID = PilotAccessService.SubjectId, dossier.DossierId,
            ClientRequestId = actionId, ActionType = "NoteAdded", EvidenceReference = "adam:method",
            Note = "Kaynak bağlantısı pilot sırasında incelendi." };
        HttpCapture append = await PostAsync(client, Api + "AppendHrDossierReviewAction", actionBody);
        HttpCapture appendRepeat = await PostAsync(client, Api + "AppendHrDossierReviewAction", actionBody);
        HttpCapture list = await PostAsync(client, Api + "ListHrDossierReviewActions",
            new { PersonelID = PilotAccessService.SubjectId, dossier.DossierId, Skip = 0, Take = 100 });
        HrEvidenceDossierResponse readDossier = Deserialize<HrEvidenceDossierResponse>(getHr);
        HrDossierReviewActionResponse createdAction = Deserialize<HrDossierReviewActionResponse>(append);
        HrDossierReviewActionResponse reusedAction = Deserialize<HrDossierReviewActionResponse>(appendRepeat);
        HrDossierReviewActionListResponse actions = Deserialize<HrDossierReviewActionListResponse>(list);
        using IServiceScope scope = services.CreateScope();
        IAcademicEvidenceSearchService search = scope.ServiceProvider.GetRequiredService<IAcademicEvidenceSearchService>();
        AcademicEvidenceSearchResponse positive = await search.SearchAsync(PilotAccessService.SubjectId, new()
        {
            Query = "same parameter initialization hyper-parameters dense grid", CanonicalWorkIds = [fixture.CanonicalWorkId], Take = 10
        }, default);
        AcademicEvidenceSearchResponse turkishOnly = await search.SearchAsync(PilotAccessService.SubjectId, new()
        {
            Query = "yöntemsel yaklaşımı dersimde açıklamak", CanonicalWorkIds = [fixture.CanonicalWorkId], Take = 10
        }, default);
        string hrExport = createHr.Body + getHr.Body;
        int usage = await CountUsageAsync(database);
        bool passed = saveContext.StatusCode == 200 && getContext.StatusCode == 200 &&
            createHr.StatusCode == 200 && getHr.StatusCode == 200 && append.StatusCode == 200 &&
            appendRepeat.StatusCode == 200 && list.StatusCode == 200 && positive.Hits.Count > 0 &&
            dossier.DossierId == readDossier.DossierId && dossier.InputFingerprint == readDossier.InputFingerprint &&
            JsonSerializer.Serialize(dossier.Dossier, JsonOptions) == JsonSerializer.Serialize(readDossier.Dossier, JsonOptions) &&
            dossier.Dossier.PublicationMetrics is not null &&
            dossier.Dossier.PublicationMetrics.Data.CanonicalWorkCount == 1 &&
            dossier.Dossier.PublicationMetrics.Data.Definitions.Count > 0 &&
            dossier.Dossier.Works.SelectMany(value => value.Reviews).SelectMany(value => value.Evidence).Any() &&
            !createdAction.Reused && reusedAction.Reused && createdAction.Action.Id == reusedAction.Action.Id &&
            actions.TotalCount == 1 && actions.Actions.Count == 1 && actions.Actions[0].Id == createdAction.Action.Id &&
            !hrExport.Contains(PrivateSentinel, StringComparison.Ordinal) &&
            !hrExport.Contains("RAW_PROVIDER_SENTINEL", StringComparison.Ordinal) &&
            !hrExport.Contains("99999999999", StringComparison.Ordinal) && usage == 0;
        return new()
        {
            ["passed"] = passed,
            ["endpoints"] = JsonSerializer.SerializeToNode(new { saveContext = saveContext.StatusOnly(),
                getContext = getContext.StatusOnly(), createHr = createHr.StatusOnly(),
                getHr = getHr.StatusOnly(), append = append.StatusOnly(),
                appendRepeat = appendRepeat.StatusOnly(), list = list.StatusOnly() }, JsonOptions),
            ["hrDossier"] = JsonSerializer.SerializeToNode(new { dossier.DossierId, dossier.InputFingerprint,
                workCount = dossier.Dossier.Works.Count,
                reviewCount = dossier.Dossier.Works.Sum(value => value.Reviews.Count),
                reviewEvidenceCount = dossier.Dossier.Works.SelectMany(value => value.Reviews)
                    .Sum(value => value.Evidence.Count) }, JsonOptions),
            ["contextVersion"] = saved.Version, ["contextFingerprint"] = saved.Fingerprint,
            ["hrPrivateContextExcluded"] = !hrExport.Contains(PrivateSentinel, StringComparison.Ordinal),
            ["hrRawProviderAndTcExcluded"] = !hrExport.Contains("RAW_PROVIDER_SENTINEL", StringComparison.Ordinal) &&
                !hrExport.Contains("99999999999", StringComparison.Ordinal),
            ["hrGetMatchesCreatedImmutableSnapshot"] = dossier.DossierId == readDossier.DossierId &&
                dossier.InputFingerprint == readDossier.InputFingerprint &&
                JsonSerializer.Serialize(dossier.Dossier, JsonOptions) == JsonSerializer.Serialize(readDossier.Dossier, JsonOptions),
            ["hrContainsPriorExactReviewEvidence"] = dossier.Dossier.Works.SelectMany(value => value.Reviews)
                .SelectMany(value => value.Evidence).Any(),
            ["hrPublicationMetrics"] = JsonSerializer.SerializeToNode(new
            {
                source = "IPublicationMetricsComputer.ComputeAsync over isolated saved SQL",
                metricSnapshotId, metricData.CanonicalWorkCount, metricData.ProviderObservationCount,
                definitionCount = metricData.Definitions.Count, metricData.Coverage,
                dossierIsStale = dossier.Dossier.PublicationMetrics?.IsStale,
                dossierStaleReasons = dossier.Dossier.PublicationMetrics?.StaleReasons
            }, JsonOptions),
            ["actionIdempotency"] = JsonSerializer.SerializeToNode(new { firstReused = createdAction.Reused,
                secondReused = reusedAction.Reused, sameActionId = createdAction.Action.Id == reusedAction.Action.Id,
                actions.TotalCount }, JsonOptions),
            ["hrGeminiCalls"] = usage,
            ["positiveLexicalRetrieval"] = JsonSerializer.SerializeToNode(new { query =
                "same parameter initialization hyper-parameters dense grid", positive.Hits.Count,
                hitSourceIds = positive.Hits.Select(value => value.SourceId), positive.AnalysisLanguageCoverage }, JsonOptions),
            ["turkishOnlyRetrievalLimitation"] = JsonSerializer.SerializeToNode(new { query =
                "yöntemsel yaklaşımı dersimde açıklamak", positiveInterpretation = false,
                hitCount = turkishOnly.Hits.Count, scope = "This records lexical behavior only; it is not a cross-language quality conclusion." }, JsonOptions)
        };
    }

    private static async Task<(long SnapshotId, ResearcherPublicationMetricsResponse Data)> CreateMetricSnapshotAsync(
        IServiceProvider services, string database)
    {
        using IServiceScope scope = services.CreateScope();
        IPublicationMetricsComputer computer = scope.ServiceProvider.GetRequiredService<IPublicationMetricsComputer>();
        DateTime now = DateTime.UtcNow;
        PublicationMetricComputation computation = await computer.ComputeAsync(PilotAccessService.SubjectId,
            PublicationMetricCatalog.Version, now, default);
        await using AcademicDbContext db = Database(database);
        PublicationMetricSnapshot snapshot = new()
        {
            PersonelId = PilotAccessService.SubjectId, CatalogVersion = PublicationMetricCatalog.Version,
            SourceRevision = 1, ComputationYear = now.Year, ComputedAt = now,
            ResultJson = computation.ResultJson, CanonicalWorkCount = computation.Data.CanonicalWorkCount,
            ProviderObservationCount = computation.Data.ProviderObservationCount,
            UnmappedAcademicWorkCount = computation.Data.UnmappedAcademicWorkCount
        };
        db.PublicationMetricSnapshots.Add(snapshot); await db.SaveChangesAsync();
        db.PublicationMetricsRefreshStates.Add(new PublicationMetricsRefreshState
        {
            PersonelId = PilotAccessService.SubjectId, RequestedRevision = 1, ComputedRevision = 1,
            RequestedCatalogVersion = PublicationMetricCatalog.Version, RequestedComputationYear = now.Year,
            LastSuccessfulSnapshotId = snapshot.Id, LastSuccessAt = now, NextAttemptAt = now,
            Attempts = 0, UpdatedAt = now, LastOutcomeCode = "Success", LastOutcomeMessage = "Product pilot offline computation."
        });
        await db.SaveChangesAsync();
        return (snapshot.Id, computation.Data);
    }

    private static async Task<JsonObject> RunFacultyScenarioAsync(HttpClient client, string database,
        int canonicalWorkId, int contextVersion, string mode, string query, Guid clientRequestId,
        IReadOnlySet<Guid> priorRunIds, ProductPilotBudgetState budget)
    {
        object request = new { PersonelID = PilotAccessService.SubjectId, ClientRequestId = clientRequestId,
            Mode = mode, Language = "tr", Query = query, CanonicalWorkIds = new[] { canonicalWorkId },
            Take = 10, ContextVersion = contextVersion };
        ProductPilotBudgetSnapshot before = budget.Snapshot();
        int sqlBefore = await CountUsageAsync(database);
        HttpCapture start = await PostAsync(client, Api + "StartFacultyAssistant", request);
        FacultyAssistantRunResponse queued = Deserialize<FacultyAssistantRunResponse>(start);
        await using (AcademicDbContext db = Database(database))
        {
            FacultyAssistantRun persisted = await db.FacultyAssistantRuns.AsNoTracking()
                .SingleAsync(value => value.ClientRequestId == clientRequestId);
            if (!IsFreshExecution(clientRequestId, queued.RunId, queued.Reused,
                    persisted.ClientRequestId, persisted.RunId, priorRunIds))
                throw new InvalidOperationException("The faculty scenario did not create the required fresh execution.");
        }
        FacultyAssistantRunResponse current = queued;
        List<HttpCapture> polls = [];
        Stopwatch timer = Stopwatch.StartNew();
        while (current.Status is "Pending" or "Running" && timer.Elapsed < TimeSpan.FromMinutes(6))
        {
            await Task.Delay(500);
            HttpCapture poll = await PostAsync(client, Api + "GetFacultyAssistantRun",
                new { PersonelID = PilotAccessService.SubjectId, queued.RunId });
            polls.Add(poll);
            current = Deserialize<FacultyAssistantRunResponse>(poll);
        }
        ProductPilotBudgetSnapshot afterCompletion = budget.Snapshot();
        int sqlAfterCompletion = await CountUsageAsync(database);
        HttpCapture duplicateStart = await PostAsync(client, Api + "StartFacultyAssistant", request);
        FacultyAssistantRunResponse duplicate = Deserialize<FacultyAssistantRunResponse>(duplicateStart);
        HttpCapture repeatedRead = await PostAsync(client, Api + "GetFacultyAssistantRun",
            new { PersonelID = PilotAccessService.SubjectId, queued.RunId });
        await Task.Delay(1500);
        ProductPilotBudgetSnapshot afterReads = budget.Snapshot();
        int sqlAfterReads = await CountUsageAsync(database);
        bool citationsExact = await CitationsExactAsync(database, current);
        bool exactModels = current.Report?.Model == ProductPilotBudgetState.RequiredModel &&
            current.Report.Verification.Model == ProductPilotBudgetState.RequiredModel;
        bool noDoubleDispatch = afterReads.Calls == afterCompletion.Calls &&
            sqlAfterReads == sqlAfterCompletion && duplicate.Reused && duplicate.RunId == queued.RunId &&
            current.AttemptCount == 1;
        bool passed = start.StatusCode == 202 && current.Status == "Completed" && current.Report is not null &&
            current.Report.Language == "tr" && current.Report.Mode == mode && current.Report.Items.Count > 0 &&
            current.Context?.Version == contextVersion && citationsExact && exactModels && noDoubleDispatch &&
            afterCompletion.Calls == before.Calls + 2 && sqlAfterCompletion == sqlBefore + 2;
        return new()
        {
            ["mode"] = mode, ["language"] = "tr", ["query"] = query, ["passed"] = passed,
            ["clientRequestId"] = clientRequestId, ["freshCorrectedExecution"] = true,
            ["start"] = start.ToJson(), ["polls"] = new JsonArray(polls.Select(value => (JsonNode?)value.ToJson()).ToArray()),
            ["completedRun"] = JsonSerializer.SerializeToNode(current, JsonOptions),
            ["duplicateStart"] = duplicateStart.ToJson(), ["repeatedRead"] = repeatedRead.ToJson(),
            ["callsBefore"] = before.Calls, ["callsAfterCompletion"] = afterCompletion.Calls,
            ["callsAfterReads"] = afterReads.Calls, ["sqlCallsBefore"] = sqlBefore,
            ["sqlCallsAfterCompletion"] = sqlAfterCompletion, ["sqlCallsAfterReads"] = sqlAfterReads,
            ["exactSourceLinkedCitations"] = citationsExact, ["exactRequestedAndReportedModels"] = exactModels,
            ["noDoubleDispatchOnDuplicateStartOrReads"] = noDoubleDispatch
        };
    }

    private static bool IsFreshExecution(Guid requestedClientRequestId, Guid responseRunId, bool responseReused,
        Guid persistedClientRequestId, Guid persistedRunId, IReadOnlySet<Guid> priorRunIds) =>
        !responseReused && requestedClientRequestId == persistedClientRequestId &&
        responseRunId == persistedRunId && !priorRunIds.Contains(responseRunId);

    private static async Task<WebApplication> StartAnalysisAsync(string database,
        ProductPilotBudgetState budget, FacultyProviderCapture capture, bool allowOutbound)
    {
        IConfigurationRoot secrets = new ConfigurationBuilder()
            .AddUserSecrets(typeof(ResearcherAnalysisService.Program).Assembly, optional: false).Build();
        string apiKey = secrets["Gemini:ApiKey"] ??
            throw new InvalidOperationException("Gemini:ApiKey user secret is missing.");
        WebApplication app = ResearcherAnalysisService.Program.CreateApplication(
            ["--environment", "Testing", "--urls", AnalysisUrl], builder =>
            {
                builder.Configuration.Sources.Clear();
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Urls"] = AnalysisUrl, ["Service:ApiKey"] = ServiceKey,
                    ["ConnectionStrings:UsageDatabase"] = database, ["Gemini:ApiKey"] = apiKey,
                    ["Ai:Provider"] = "Ollama", ["Ai:ArticleProvider"] = "Gemini",
                    ["Ai:ArticleModel"] = ProductPilotBudgetState.RequiredModel,
                    ["Ai:ArticleVerifierModel"] = ProductPilotBudgetState.RequiredModel,
                    ["Ai:ArticleGenerationThinkingLevel"] = "high", ["Ai:ArticleVerifierThinkingLevel"] = "high",
                    ["Ai:ArticleContextTokens"] = "131072", ["Ai:ArticleMaxOutputTokens"] = "8192",
                    ["Ai:FacultyAssistantMaxOutputTokens"] = "16384",
                    ["Ai:ArticleVerifierMaxOutputTokens"] = "8192", ["Ai:ArticleFallbackChunkBytes"] = "10000",
                    ["Ai:ArticleReviewMaximumInputBytes"] = "100000", ["Ai:ArticleReviewTimeoutSeconds"] = "300",
                    ["Ai:TimeoutSeconds"] = "180"
                });
                builder.Logging.ClearProviders(); builder.Logging.AddConsole();
                builder.Services.AddSingleton(budget);
                builder.Services.AddSingleton(capture);
                builder.Services.AddTransient<ProductPilotBudgetHandler>();
                builder.Services.AddHttpClient<GeminiArticleClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => allowOutbound
                        ? new HttpClientHandler { AllowAutoRedirect = false }
                        : new DenyOutboundHandler())
                    .AddHttpMessageHandler<ProductPilotBudgetHandler>();
                builder.Services.RemoveAll<IFacultyAssistantGenerator>();
                builder.Services.RemoveAll<IArticleReviewVerifier>();
                builder.Services.AddScoped<IFacultyAssistantGenerator, CapturingFacultyGenerator>();
                builder.Services.AddScoped<IArticleReviewVerifier, CapturingFacultyVerifier>();
            });
        await app.StartAsync();
        return app;
    }

    private static async Task<WebApplication> StartCollectorAsync(string root, string database,
        string url, bool workerEnabled)
    {
        EnsureAnalysisDatabase(database);
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing", ContentRootPath = root,
            ApplicationName = typeof(AcademicPerformanceModule).Assembly.FullName
        });
        builder.WebHost.UseUrls(url);
        builder.Configuration.AddJsonFile("appsettings.bundles.json", optional: false)
            .AddJsonFile("academicsettings.json", optional: false)
            .AddInMemoryCollection(CollectorConfiguration(database, workerEnabled));
        builder.Logging.ClearProviders(); builder.Logging.AddConsole();
        builder.Services.AddAcademicPerformanceModule(builder.Configuration);
        builder.Services.RemoveAll<IAcademicProductAccessService>();
        builder.Services.AddSingleton<IAcademicProductAccessService, PilotAccessService>();
        builder.Services.AddAuthentication("ProductPilot")
            .AddScheme<AuthenticationSchemeOptions, PilotAuthenticationHandler>("ProductPilot", _ => { });
        builder.Services.AddApplicationPartsTypeSource();
        builder.Services.ConfigureSections(builder.Configuration);
        builder.Services.AddCaching(); builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<Serenity.Data.IRowFieldsProvider, Serenity.Data.DefaultRowFieldsProvider>();
        builder.Services.AddSingleton<Serenity.Abstractions.IPermissionService, DevelopmentPermissionService>();
        builder.Services.AddControllersWithViews(); builder.Services.AddServiceEndpointConventions();
        builder.Services.Configure<JsonOptions>(options => JSON.Defaults.Populate(options.JsonSerializerOptions));
        WebApplication app = builder.Build();
        Serenity.Data.RowFieldsProvider.SetDefaultFrom(app.Services);
        app.Services.MigrateAcademicDatabase();
        app.UseRouting(); app.UseAuthentication(); app.MapControllers();
        app.MapGet("/", () => Results.Ok(new { Status = "ProductPilot" }));
        await app.StartAsync();
        return app;
    }

    private static void EnsureAnalysisDatabase(string database)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:UsageDatabase"] = database
            }).Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddAnalysisDatabaseMigrations(configuration);
        using ServiceProvider provider = services.BuildServiceProvider();
        provider.MigrateAnalysisDatabase();
    }

    private static Dictionary<string, string?> CollectorConfiguration(string database, bool workerEnabled)
    {
        Dictionary<string, string?> values = new()
        {
            ["ConnectionStrings:AcademicDatabase"] = database,
            ["AnalysisService:BaseUrl"] = AnalysisUrl, ["AnalysisService:ApiKey"] = ServiceKey,
            ["ArticleSummaryAutomation:Enabled"] = "false", ["ArticleSummaryAutomation:WorkerEnabled"] = "false",
            ["PublicationMetrics:WorkerEnabled"] = "false", ["ArticleEvaluation:WorkerEnabled"] = "false",
            ["FacultyAssistant:WorkerEnabled"] = workerEnabled.ToString(), ["FacultyAssistant:PollSeconds"] = "1",
            ["FacultyAssistant:RequestTimeoutSeconds"] = "330", ["BulkCollection:WorkerEnabled"] = "false"
        };
        foreach (string provider in new[] { "Orcid", "SearchApi", "OpenAlex", "WebOfScience", "Yoksis",
                     "TrDizin", "Crossref", "Unpaywall", "SemanticScholar" })
            values[$"ProviderRequestLimits:{provider}:Enabled"] = "false";
        foreach (string key in new[] { "SearchApi:ApiKey", "OpenAlex:ApiKey", "SemanticScholar:ApiKey",
                     "WebOfScience:ApiKey", "Yoksis:Username", "Yoksis:Password", "Unpaywall:Email" })
            values[key] = "";
        return values;
    }

    private static async Task<SourceFixture> SeedAsync(string root, string database)
    {
        string sourcePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Temp", "academic-fulltext-source-t9aqbsak", "adam-v1-fulltext.pdf");
        string resultPath = Path.Combine(root, "docs", "fulltext-citation-alignment-pilot-20260912", "result.json");
        string phasePath = Path.Combine(root, "docs", "fulltext-citation-alignment-pilot-20260912", "phases", "019-final.json");
        byte[] pdf = await File.ReadAllBytesAsync(sourcePath);
        string pdfHash = Hash(pdf);
        string resultHash = Hash(await File.ReadAllBytesAsync(resultPath));
        string phaseHash = Hash(await File.ReadAllBytesAsync(phasePath));
        ArticlePdfExtractor extractor = new(Options.Create(new ArticleSummaryOptions
        { MaximumPages = 40, MaximumExtractedCharacters = 160000, OcrEnabled = false }));
        SummarizeArticleRequest rawExtracted = extractor.Extract(pdf, "tr");
        string sourceHash = Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(rawExtracted.Pages,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        SummarizeArticleRequest extracted = rawExtracted with { SourceHash = sourceHash };
        if (pdfHash != AdamPdfHash || sourceHash != AdamSourceHash ||
            resultHash != PriorResultHash || phaseHash != PriorPhase019Hash ||
            extracted.Pages.Count != 9 || extracted.SourceSpans?.Count != 64 ||
            !ArticleSourceCatalog.IsValid(extracted.Pages, extracted.SourceSpans, "pdf"))
            throw new InvalidOperationException("The immutable Adam source preflight failed.");

        JsonObject prior = (JsonNode.Parse(await File.ReadAllTextAsync(resultPath)) as JsonObject)!;
        JsonObject adam = prior["articles"]!.AsArray().OfType<JsonObject>().Single(value =>
            value["name"]!.GetValue<string>() == "adam");
        JsonNode summaryNode = adam["savedReadsAndCache"]!["savedSummary"]!["body"]!;
        JsonNode reviewNode = adam["savedReadsAndCache"]!["savedReview"]!["body"]!;
        SavedArticleSummaryResponse priorSummary = summaryNode.Deserialize<SavedArticleSummaryResponse>(JsonOptions)!;
        CanonicalArticleReviewResponse priorReview = reviewNode.Deserialize<CanonicalArticleReviewResponse>(JsonOptions)!;

        await using AcademicDbContext db = Database(database);
        Researcher researcher = new()
        {
            PersonelId = PilotAccessService.SubjectId, FirstName = "Sentetik", LastName = "Akademisyen",
            AcademicTitle = "Dr. Öğr. Üyesi", Department = "Bilgisayar Mühendisliği",
            TcKimlikNo = "99999999999", LastUpdatedAt = DateTime.UtcNow
        };
        AcademicWork work = new()
        {
            PersonelId = PilotAccessService.SubjectId, Provider = AcademicWorkProvider.OpenAlex,
            ProviderWorkId = "product-pilot-adam", Title = "Adam: A Method for Stochastic Optimization",
            Doi = "10.48550/arXiv.1412.6980", Category = AcademicWorkCategory.Article,
            CategorySource = AcademicWorkCategorySource.OpenAlex, PublicationYear = 2014,
            ProviderPayload = "RAW_PROVIDER_SENTINEL", FullTextUrl = "https://arxiv.org/pdf/1412.6980v1",
            HasFullText = true, IsOpenAccess = true, SyncedAt = DateTime.UtcNow
        };
        CanonicalWork canonical = new()
        {
            NormalizedDoi = "10.48550/arxiv.1412.6980", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        db.AddRange(researcher, work, canonical); await db.SaveChangesAsync();
        db.AddRange(new CanonicalResearcherWork
        {
            CanonicalWorkId = canonical.Id, PersonelId = researcher.PersonelId, LastObservedAt = DateTime.UtcNow
        }, new CanonicalWorkObservation
        {
            CanonicalWorkId = canonical.Id, AcademicWorkId = work.Id, PersonelId = researcher.PersonelId,
            Provider = AcademicWorkProvider.OpenAlex, ProviderWorkId = work.ProviderWorkId,
            TitleObserved = work.Title, DoiObserved = work.Doi, PublicationYearObserved = 2014,
            CategoryObserved = AcademicWorkCategory.Article, FullTextUrl = work.FullTextUrl,
            IsRetracted = false, ObservedAt = DateTime.UtcNow
        });
        ArticleSourceSnapshot source = new()
        {
            CanonicalWorkId = canonical.Id, ExtractedTextHash = extracted.SourceHash,
            SourceKind = extracted.SourceKind, ExtractionVersion = extracted.ExtractionVersion,
            CreatedAt = priorSummary.SavedAt
        };
        source.Pages.AddRange(extracted.Pages.Select((page, ordinal) => new ArticleSourcePageSnapshot
        { Ordinal = ordinal, PageNumber = page.PageNumber, Text = page.Text }));
        source.Spans.AddRange(extracted.SourceSpans!.Select((span, ordinal) => new ArticleSourceSpanSnapshot
        {
            SourceId = span.SourceId, Ordinal = ordinal, PageNumber = span.PageNumber,
            StartOffset = span.StartOffset, EndOffset = span.EndOffset, Text = span.Text
        }));
        db.ArticleSourceSnapshots.Add(source); await db.SaveChangesAsync();
        SavedArticleSummary saved = new()
        {
            AcademicWorkId = work.Id, OriginalAcademicWorkId = work.Id, PersonelId = researcher.PersonelId,
            SavedAt = priorSummary.SavedAt, SourceUrl = priorSummary.SourceUrl,
            SourceHash = extracted.SourceHash, SourceKind = extracted.SourceKind,
            ExtractionVersion = extracted.ExtractionVersion,
            SnapshotJson = JsonSerializer.Serialize(extracted, JsonOptions),
            ReportJson = JsonSerializer.Serialize(priorSummary.Report, JsonOptions)
        };
        db.ArticleSummaries.Add(saved); await db.SaveChangesAsync();
        ArticleSummaryReport summaryReport = priorSummary.Report;
        ArticleVerificationMetadata summaryVerification = summaryReport.Verification!;
        CanonicalArticleAnalysisRun analysis = new()
        {
            CanonicalWorkId = canonical.Id, ArticleSourceSnapshotId = source.Id,
            SavedArticleSummaryId = saved.Id, AnalyzedAt = saved.SavedAt, SourceAcquiredAt = saved.SavedAt,
            SourceUrl = saved.SourceUrl, SourceOrigin = "ReusedPublicPilotFixture", Language = summaryReport.Language,
            PolicyVersion = new ArticleSummaryAutomationOptions().PolicyVersion, Model = summaryReport.Model,
            PromptVersion = summaryReport.PromptVersion, ExtractionMethod = summaryReport.ExtractionMethod ?? "pdf_text",
            ProcessedChunks = summaryReport.Coverage.ProcessedChunks, TotalChunks = summaryReport.Coverage.TotalChunks,
            ProcessedPages = summaryReport.Coverage.ProcessedPages, TextBearingPages = summaryReport.Coverage.TextBearingPages,
            TotalPages = summaryReport.Coverage.TotalPages, SelectedClaimsOmitted = summaryReport.Coverage.SelectedClaimsOmitted,
            IsPartial = summaryReport.Coverage.IsPartial, ScopeReason = summaryReport.Coverage.ScopeReason,
            CandidateClaims = summaryReport.Coverage.CandidateClaims,
            AutomaticallyCheckedClaims = summaryReport.Coverage.AutomaticallyCheckedClaims,
            SupportedClaims = summaryReport.Coverage.SupportedClaims,
            UnsupportedClaims = summaryReport.Coverage.UnsupportedClaims,
            UncertainClaims = summaryReport.Coverage.UncertainClaims,
            DuplicateOrCappedClaims = summaryReport.Coverage.DuplicateOrCappedClaims,
            BudgetUnverifiedClaims = summaryReport.Coverage.BudgetUnverifiedClaims,
            OmissionReasonsJson = JsonSerializer.Serialize(summaryReport.Coverage.OmissionReasons ?? [], JsonOptions),
            VerificationStatus = summaryVerification.Status, VerificationModel = summaryVerification.Model,
            VerificationPromptVersion = summaryVerification.PromptVersion,
            UsesSameModelFamily = summaryVerification.UsesSameModelFamily,
            VerificationLimitation = summaryVerification.Limitation
        };
        Dictionary<string, ArticleSourceSpanSnapshot> spans = source.Spans.ToDictionary(value => value.SourceId);
        (string Name, IReadOnlyList<ArticleClaim> Claims)[] sections =
        [
            ("Purpose", summaryReport.Sections.Purpose), ("Methods", summaryReport.Sections.Methods),
            ("Data", summaryReport.Sections.Data), ("Findings", summaryReport.Sections.Findings),
            ("Limitations", summaryReport.Sections.Limitations)
        ];
        for (int sectionOrder = 0; sectionOrder < sections.Length; sectionOrder++)
            for (int ordinal = 0; ordinal < sections[sectionOrder].Claims.Count; ordinal++)
            {
                ArticleClaim claim = sections[sectionOrder].Claims[ordinal];
                CanonicalArticleClaim row = new()
                {
                    Section = sections[sectionOrder].Name, SectionOrder = sectionOrder, Ordinal = ordinal,
                    ExternalClaimId = claim.ClaimId, Text = claim.Text
                };
                row.Evidence.AddRange(claim.Evidence.Select((evidence, evidenceOrdinal) =>
                    new CanonicalArticleClaimEvidence
                    { ArticleSourceSpanId = spans[evidence.SourceId!].Id, Ordinal = evidenceOrdinal }));
                analysis.Claims.Add(row);
            }
        db.CanonicalArticleAnalysisRuns.Add(analysis); await db.SaveChangesAsync();

        ArticleReviewReport reviewReport = priorReview.Report;
        CanonicalArticleReviewRun review = new()
        {
            CanonicalWorkId = canonical.Id, BaseAnalysisRunId = analysis.Id, ArticleSourceSnapshotId = source.Id,
            ReviewedAt = priorReview.ReviewedAt, Language = reviewReport.Language,
            PolicyVersion = reviewReport.PolicyVersion, SettingsFingerprint = "reused-prior-pilot-fixture",
            Model = reviewReport.Model, PromptVersion = reviewReport.PromptVersion, Outcome = reviewReport.Outcome,
            VerificationStatus = reviewReport.Verification.Status, VerificationModel = reviewReport.Verification.Model,
            VerificationPromptVersion = reviewReport.Verification.PromptVersion,
            UsesSameModelFamily = reviewReport.Verification.UsesSameModelFamily,
            VerificationLimitation = reviewReport.Verification.Limitation,
            ProcessedPages = reviewReport.SourceCoverage.ProcessedPages,
            TextBearingPages = reviewReport.SourceCoverage.TextBearingPages,
            TotalPages = reviewReport.SourceCoverage.TotalPages, IsPartial = reviewReport.SourceCoverage.IsPartial,
            ScopeReason = reviewReport.SourceCoverage.ScopeReason,
            ProcessedRoles = reviewReport.Coverage.ProcessedRoles, TotalRoles = reviewReport.Coverage.TotalRoles,
            CandidateFindings = reviewReport.Coverage.CandidateFindings,
            AutomaticallyCheckedFindings = reviewReport.Coverage.AutomaticallyCheckedFindings,
            SupportedFindings = reviewReport.Coverage.SupportedFindings,
            UnsupportedFindings = reviewReport.Coverage.UnsupportedFindings,
            UncertainFindings = reviewReport.Coverage.UncertainFindings,
            OmittedFindings = reviewReport.Coverage.OmittedFindings,
            OmissionReasonsJson = JsonSerializer.Serialize(reviewReport.Coverage.OmissionReasons, JsonOptions),
            ReportJson = JsonSerializer.Serialize(reviewReport, JsonOptions)
        };
        int findingOrdinal = 0;
        foreach (ArticleReviewFinding finding in reviewReport.Reviews.SelectMany(value => value.Findings))
        {
            CanonicalArticleReviewFinding row = new()
            {
                Ordinal = findingOrdinal++, ExternalFindingId = finding.FindingId, Role = finding.Role,
                Kind = finding.Kind, Basis = finding.Basis, Suggestion = finding.Suggestion
            };
            row.Evidence.AddRange(finding.Evidence.Select((evidence, ordinal) =>
                new CanonicalArticleReviewEvidence
                { ArticleSourceSpanId = spans[evidence.SourceId].Id, Ordinal = ordinal }));
            review.Findings.Add(row);
        }
        db.CanonicalArticleReviewRuns.Add(review); await db.SaveChangesAsync();
        return new SourceFixture(canonical.Id, 0, new JsonObject
        {
            ["pdfPath"] = sourcePath, ["pdfSha256"] = pdfHash, ["pdfBytes"] = pdf.Length,
            ["pages"] = extracted.Pages.Count, ["spans"] = extracted.SourceSpans.Count,
            ["sourceHash"] = extracted.SourceHash, ["exactCatalog"] = true,
            ["priorResultSha256"] = resultHash, ["priorPhase019Sha256"] = phaseHash,
            ["priorResultHashMatchesExpected"] = resultHash == PriorResultHash,
            ["priorPhase019HashMatchesExpected"] = phaseHash == PriorPhase019Hash,
            ["priorArtifactsReadOnly"] = true,
            ["seededFromPriorSavedSummaryAndReview"] = true
        });
    }

    private static async Task<bool> CitationsExactAsync(string database, FacultyAssistantRunResponse run)
    {
        if (run.Report is null || run.Report.Items.Count == 0) return false;
        await using AcademicDbContext db = Database(database);
        long[] spanIds = run.Retrieval.Evidence.Select(value => value.ArticleSourceSpanId).Distinct().ToArray();
        List<ArticleSourceSpanSnapshot> spans = await db.ArticleSourceSpans.AsNoTracking()
            .Where(value => spanIds.Contains(value.Id)).ToListAsync();
        long[] snapshotIds = spans.Select(value => value.ArticleSourceSnapshotId).Distinct().ToArray();
        List<ArticleSourcePageSnapshot> pages = await db.ArticleSourcePages.AsNoTracking()
            .Where(value => snapshotIds.Contains(value.ArticleSourceSnapshotId)).ToListAsync();
        Dictionary<long, ArticleSourceSpanSnapshot> byId = spans.ToDictionary(value => value.Id);
        foreach (FacultyAssistantCitation citation in run.Report.Items.SelectMany(value => value.Citations))
        {
            FacultyAssistantRetrievedEvidence? retrieved = run.Retrieval.Evidence.SingleOrDefault(value =>
                value.EvidenceId == citation.EvidenceId);
            if (retrieved is null || !byId.TryGetValue(retrieved.ArticleSourceSpanId, out ArticleSourceSpanSnapshot? span) ||
                span.Text != citation.ExactQuote || span.SourceId != retrieved.SourceId ||
                span.PageNumber != retrieved.PageNumber || span.StartOffset != retrieved.StartOffset ||
                span.EndOffset != retrieved.EndOffset || retrieved.SourceKind != "pdf" ||
                retrieved.SourceCoverage.TotalPages != 9)
                return false;
            ArticleSourcePageSnapshot? page = pages.SingleOrDefault(value =>
                value.ArticleSourceSnapshotId == span.ArticleSourceSnapshotId && value.PageNumber == span.PageNumber);
            if (page is null || span.StartOffset < 0 || span.EndOffset > page.Text.Length ||
                page.Text[span.StartOffset..span.EndOffset] != span.Text)
                return false;
        }
        return true;
    }

    private static async Task<JsonObject> ReconcileAsync(string database, ProductPilotBudgetSnapshot budget)
    {
        await using SqlConnection connection = new(database); await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT_BIG(*),
              COALESCE(SUM(CAST(CASE WHEN CompletedAt IS NULL OR EstimatedUsd IS NULL THEN 1 ELSE 0 END AS bigint)),CAST(0 AS bigint)),
              COALESCE(SUM(CAST(CASE WHEN RequestedModel <> 'gemini-3.8-flash' THEN 1 ELSE 0 END AS bigint)),CAST(0 AS bigint)),
              COALESCE(SUM(CAST(CASE WHEN ReturnedModel IS NULL OR ReturnedModel <> 'gemini-3.8-flash' THEN 1 ELSE 0 END AS bigint)),CAST(0 AS bigint)),
              CASE WHEN COALESCE(SUM(CASE WHEN EstimatedUsd IS NULL THEN 1 ELSE 0 END),0)=0
                   THEN COALESCE(SUM(EstimatedUsd),CAST(0 AS decimal(19,9))) ELSE NULL END
            FROM [analysis].[GeminiUsageAttempts]
            """;
        await using SqlDataReader reader = await command.ExecuteReaderAsync(); await reader.ReadAsync();
        long calls = reader.GetInt64(0), unknown = reader.GetInt64(1), wrongRequested = reader.GetInt64(2),
            wrongReturned = reader.GetInt64(3);
        decimal? spend = reader.IsDBNull(4) ? null : reader.GetDecimal(4);
        bool passed = calls == budget.Calls && unknown == 0 && wrongRequested == 0 && wrongReturned == 0 &&
            spend == budget.CommittedSpendUsd && budget.Items.All(value => value.Completed && value.ActualUsageReliable);
        return new()
        {
            ["passed"] = passed, ["sqlCalls"] = calls, ["guardCalls"] = budget.Calls,
            ["sqlUnknownCalls"] = unknown, ["sqlWrongRequestedModels"] = wrongRequested,
            ["sqlWrongReturnedModels"] = wrongReturned, ["sqlSpendUsd"] = spend,
            ["guardCommittedSpendUsd"] = budget.CommittedSpendUsd
        };
    }

    private static async Task<JsonNode> SqlAuditAsync(string database, SourceFixture? fixture)
    {
        await using AcademicDbContext db = Database(database);
        var runs = await db.FacultyAssistantRuns.AsNoTracking().OrderBy(value => value.Id)
            .Select(value => new { value.RunId, value.Mode, value.Language, value.Status,
                value.AttemptCount, value.ContextVersionId, value.InputFingerprint,
                value.ReportJson, value.ErrorCode, value.ErrorMessage }).ToListAsync();
        var dossiers = await db.HrEvidenceDossiers.AsNoTracking().OrderBy(value => value.Id)
            .Select(value => new { value.Id, value.PersonelId, value.InputFingerprint,
                value.InputManifestJson, value.DossierJson }).ToListAsync();
        bool hrPrivate = dossiers.All(value => !value.InputManifestJson.Contains(PrivateSentinel, StringComparison.Ordinal) &&
            !value.DossierJson.Contains(PrivateSentinel, StringComparison.Ordinal));
        return JsonSerializer.SerializeToNode(new
        {
            rows = new
            {
                researchers = await db.Researchers.CountAsync(), canonicalWorks = await db.CanonicalWorks.CountAsync(),
                sourceSnapshots = await db.ArticleSourceSnapshots.CountAsync(), sourceSpans = await db.ArticleSourceSpans.CountAsync(),
                analysisRuns = await db.CanonicalArticleAnalysisRuns.CountAsync(), reviewRuns = await db.CanonicalArticleReviewRuns.CountAsync(),
                reviewFindings = await db.CanonicalArticleReviewFindings.CountAsync(), dossiers = dossiers.Count,
                dossierActions = await db.HrDossierReviewActions.CountAsync(), contexts = await db.FacultyAssistantContextVersions.CountAsync(),
                facultyRuns = runs.Count, geminiUsageAttempts = await CountUsageAsync(database)
            },
            facultyRuns = runs.Select(value => new { value.RunId, value.Mode, value.Language, value.Status,
                value.AttemptCount, value.ContextVersionId, value.InputFingerprint,
                report = value.ReportJson is null ? null : JsonNode.Parse(value.ReportJson), value.ErrorCode, value.ErrorMessage }),
            dossiers = dossiers.Select(value => new { value.Id, value.PersonelId, value.InputFingerprint,
                manifest = JsonNode.Parse(value.InputManifestJson), dossier = JsonNode.Parse(value.DossierJson) }),
            hrPrivateContextExcluded = hrPrivate,
            expectedCanonicalWorkId = fixture?.CanonicalWorkId
        }, JsonOptions)!;
    }

    private static async Task<JsonNode> ReadUsageJsonAsync(string database)
    {
        await using SqlConnection connection = new(database); await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT AttemptId,StartedAt,CompletedAt,RequestedModel,ReturnedModel,Outcome,HttpStatus,
                   PromptTokenCount,CachedTokenCount,CandidateTokenCount,ThoughtTokenCount,TotalTokenCount,
                   PricingVersion,EstimatedUsd
            FROM [analysis].[GeminiUsageAttempts] ORDER BY StartedAt,AttemptId
            """;
        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        JsonArray values = [];
        while (await reader.ReadAsync())
            values.Add(JsonSerializer.SerializeToNode(new
            {
                attemptId = reader.GetGuid(0), startedAt = reader.GetDateTime(1),
                completedAt = reader.IsDBNull(2) ? (DateTime?)null : reader.GetDateTime(2),
                requestedModel = reader.GetString(3), returnedModel = reader.IsDBNull(4) ? null : reader.GetString(4),
                outcome = reader.GetString(5), httpStatus = reader.IsDBNull(6) ? (int?)null : reader.GetInt32(6),
                promptTokens = reader.IsDBNull(7) ? (long?)null : reader.GetInt64(7),
                cachedTokens = reader.IsDBNull(8) ? (long?)null : reader.GetInt64(8),
                candidateTokens = reader.IsDBNull(9) ? (long?)null : reader.GetInt64(9),
                thoughtTokens = reader.IsDBNull(10) ? (long?)null : reader.GetInt64(10),
                totalTokens = reader.IsDBNull(11) ? (long?)null : reader.GetInt64(11),
                pricingVersion = reader.IsDBNull(12) ? null : reader.GetString(12),
                estimatedUsd = reader.IsDBNull(13) ? (decimal?)null : reader.GetDecimal(13)
            }, JsonOptions));
        return values;
    }

    private static async Task<int> CountUsageAsync(string database)
    {
        await using SqlConnection connection = new(database); await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM [analysis].[GeminiUsageAttempts]";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static HttpClient Client(string baseAddress)
    {
        HttpClient client = new() { BaseAddress = new Uri(baseAddress), Timeout = TimeSpan.FromMinutes(7) };
        client.DefaultRequestHeaders.Add("X-Product-Pilot-Auth", "synthetic-faculty");
        return client;
    }

    private static async Task<HttpCapture> PostAsync(HttpClient client, string path, object body)
    {
        Stopwatch timer = Stopwatch.StartNew();
        using HttpResponseMessage response = await client.PostAsJsonAsync(path, body, JsonOptions);
        string content = await response.Content.ReadAsStringAsync(); timer.Stop();
        return new((int)response.StatusCode, timer.Elapsed.TotalSeconds, content);
    }

    private static T Deserialize<T>(HttpCapture capture)
    {
        if (capture.StatusCode is < 200 or >= 300)
            throw new InvalidOperationException($"HTTP {capture.StatusCode}: {capture.Body}");
        return JsonSerializer.Deserialize<T>(capture.Body, JsonOptions) ??
            throw new InvalidOperationException("The endpoint response could not be deserialized.");
    }

    private static bool HasGeminiKey()
    {
        IConfigurationRoot secrets = new ConfigurationBuilder()
            .AddUserSecrets(typeof(ResearcherAnalysisService.Program).Assembly, optional: true).Build();
        return !string.IsNullOrWhiteSpace(secrets["Gemini:ApiKey"]);
    }

    private static AcademicDbContext Database(string database) => new(
        new DbContextOptionsBuilder<AcademicDbContext>().UseSqlServer(database).Options);

    private static async Task<(string Master, string Database)> CreateDatabaseAsync(string name, string prefix)
    {
        ValidateDatabaseName(name, prefix);
        (string master, string database) = ConnectionStrings(name);
        await using SqlConnection connection = new(master); await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{name}]"; await command.ExecuteNonQueryAsync();
        return (master, database);
    }

    private static (string Master, string Database) ConnectionStrings(string name)
    {
        SqlConnectionStringBuilder builder = new(Environment.GetEnvironmentVariable("ACADEMIC_TEST_SQLSERVER") ??
            @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true");
        builder.InitialCatalog = "master"; string master = builder.ConnectionString;
        builder.InitialCatalog = name; return (master, builder.ConnectionString);
    }

    private static async Task DropDatabaseAsync(string master, string name, string prefix)
    {
        ValidateDatabaseName(name, prefix);
        await using SqlConnection connection = new(master); await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]";
        await command.ExecuteNonQueryAsync();
    }

    private static void ValidateDatabaseName(string name, string prefix)
    {
        if (!name.StartsWith(prefix, StringComparison.Ordinal) || name.Length != prefix.Length + 32 ||
            name.Any(value => !char.IsLetterOrDigit(value) && value != '_'))
            throw new InvalidOperationException("The isolated product pilot database name is invalid.");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AcademicCollectorDemo.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    private static JsonNode SafeFailure(Exception exception) => JsonSerializer.SerializeToNode(new
    {
        type = exception.GetType().Name, message = exception.Message,
        innerType = exception.InnerException?.GetType().Name,
        innerMessage = exception.InnerException?.Message
    }, JsonOptions)!;

    private sealed class SourceFixture(int canonicalWorkId, int contextVersion, JsonObject preflight,
        long dossierId = 0, string? contextFingerprint = null, string? dossierFingerprint = null)
    {
        public int CanonicalWorkId { get; } = canonicalWorkId;
        public int ContextVersion { get; set; } = contextVersion;
        public JsonObject Preflight { get; } = preflight;
        public long DossierId { get; } = dossierId;
        public string? ContextFingerprint { get; } = contextFingerprint;
        public string? DossierFingerprint { get; } = dossierFingerprint;
    }

    private sealed record RetestBaseline(string DatabaseName, string Master, string Database,
        ProductPilotBudgetSnapshot Budget, SourceFixture Fixture, HashSet<Guid> OriginalRunIds,
        JsonObject ArtifactEvidence);

    private sealed record RetestCounts(int Researchers, int CanonicalWorks, int SourceSnapshots,
        int SourceSpans, int Dossiers, int DossierActions, int Contexts, int FacultyRuns,
        int GeminiUsageAttempts);

    private sealed class DenyOutboundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => throw new ProductPilotBudgetException(
                "Network dispatch is physically disabled during retest preflight.");
    }

    private sealed record HttpCapture(int StatusCode, double ElapsedSeconds, string Body)
    {
        public object StatusOnly() => new { StatusCode, ElapsedSeconds };
        public JsonObject ToJson()
        {
            JsonNode? body; try { body = JsonNode.Parse(Body); } catch (JsonException) { body = JsonValue.Create(Body); }
            return new() { ["statusCode"] = StatusCode, ["elapsedSeconds"] = ElapsedSeconds, ["body"] = body };
        }
    }
}
