using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using AcademicCollector.Analysis.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using ResearcherAnalysisService.Products.Api.Contracts;
using ResearcherAnalysisService.Products.ArticleSummaries;
using AcademicCollectorDemo.Modules.AcademicPerformance.Bulk.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ResearcherAnalysisService.Products.Data;

namespace ServiceAcceptancePilot;

internal static class ServiceAcceptanceRehearsal
{
    private const string Url = "http://127.0.0.1:5209";
    private const string AnalysisUrl = "http://127.0.0.1:5109";
    private const string AdamQuery = "Bu makalenin yöntem ve sınırlılıklarını kaynaklarıyla açıkla; kendi çalışmamda hangi koşulları kontrol etmeliyim?";
    private const string FootballQuery = "Bu makalenin yöntem ve bulgular bölümünden dersimde kullanabileceğim bir örnek ve bir tartışma sorusu hazırla.";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<int> RunAsync()
    {
        string root = ServiceAcceptancePreflight.FindRoot();
        string name = "AcademicServiceAcceptance_" + Guid.NewGuid().ToString("N");
        (string master, string database) = Connections(name);
        JsonObject result = new() { ["runId"] = Program.RunId, ["databaseName"] = name,
            ["externalNetwork"] = "denied by replay-only provider handler" };
        ReplayAudit replay = new();
        SyntheticAnalysisCapture analysisCapture = new();
        try
        {
            await using (SqlConnection connection = new(master))
            {
                await connection.OpenAsync();
                using SqlCommand command = connection.CreateCommand();
                command.CommandText = $"CREATE DATABASE [{name}]";
                await command.ExecuteNonQueryAsync();
            }
            await using WebApplication analysis = await StartSyntheticAnalysisAsync(analysisCapture);
            Guid batchId = Guid.NewGuid();
            await using (WebApplication idle = await ServiceAcceptanceHost.StartCollectorAsync(
                root, database, Url, AnalysisUrl, "idle", replay))
            using (HttpClient client = Client())
            {
                JsonObject submitted = await PostAsync(client,
                    "/Services/AcademicPerformance/V1/Bulk/Submit", new
                    {
                        BatchId = batchId,
                        Researchers = new[] { new { PersonelID = ServiceAcceptanceHost.SubjectId,
                            ORCID = ServiceAcceptanceHost.Orcid } }
                    });
                result["submit"] = submitted;
                if (submitted["Counts"]?[BulkJobStatus.Pending]?.GetValue<int>() != 1 &&
                    submitted["counts"]?[BulkJobStatus.Pending]?.GetValue<int>() != 1)
                    throw new InvalidOperationException("Bulk submit did not persist a pending job.");
            }

            await using (WebApplication bulk = await ServiceAcceptanceHost.StartCollectorAsync(
                root, database, Url, AnalysisUrl, "bulk", replay))
            using (HttpClient client = Client())
            {
                JsonObject status = await PollAsync(client, batchId);
                result["bulkAfterRestart"] = status;
                string? terminal = status["Jobs"]?[0]?["Status"]?.GetValue<string>() ??
                    status["jobs"]?[0]?["status"]?.GetValue<string>();
                if (terminal != BulkJobStatus.Partial)
                    throw new InvalidOperationException("The ORCID-only replay did not produce the expected terminal Partial status.");
                result["collectionCoverage"] = "Terminal Partial is expected: ORCID replay saved two works; OpenAlex was intentionally disabled/rejected.";
                result["singleRead"] = await PostAsync(client,
                    "/Services/AcademicPerformance/V1/GetResearcher",
                    new { PersonelID = ServiceAcceptanceHost.SubjectId });
            }

            Dictionary<string, int> workIds;
            await using (AnalysisDbContext identityDb = Database(database))
            {
                workIds = await identityDb.CanonicalWorks.AsNoTracking()
                    .Where(value => value.Researchers.Any(item => item.PersonelId == ServiceAcceptanceHost.SubjectId))
                    .ToDictionaryAsync(value => value.NormalizedDoi!, value => value.Id);
            }
            int adamId = workIds["10.48550/arxiv.1412.6980"];
            int footballId = workIds["10.1038/s41598-022-12547-0"];
            int[] canonicalIds = [adamId, footballId];

            await using (WebApplication summary = await ServiceAcceptanceHost.StartCollectorAsync(
                root, database, Url, AnalysisUrl, "summary", replay))
                await WaitForSummariesAsync(database, canonicalIds);
            result["summariesAfterRestart"] = await ReadSummaryAuditAsync(database, canonicalIds);

            ResearcherPublicationMetricsStatusResponse metrics;
            await using (WebApplication metricHost = await ServiceAcceptanceHost.StartCollectorAsync(
                root, database, Url, AnalysisUrl, "metrics", replay))
            using (HttpClient client = Client())
            {
                _ = await PostTypedAsync<ResearcherPublicationMetricsStatusResponse>(client,
                    AnalysisUrl + "/api/v1/researchers/metrics/refresh",
                    new { PersonelID = ServiceAcceptanceHost.SubjectId });
                metrics = await PollMetricsAsync(client);
            }
            result["metricsCurrent"] = JsonSerializer.SerializeToNode(metrics, JsonOptions);

            Guid adamRequestId = Guid.NewGuid();
            Guid footballRequestId = Guid.NewGuid();
            FacultyAssistantContextResponse context;
            HrEvidenceDossierResponse createdDossier;
            FacultyAssistantRunResponse adamPending;
            FacultyAssistantRunResponse footballPending;
            await using (WebApplication idle = await ServiceAcceptanceHost.StartCollectorAsync(
                root, database, Url, AnalysisUrl, "idle", replay))
            using (HttpClient client = Client(authenticated: true))
            {
                context = await PostTypedAsync<FacultyAssistantContextResponse>(client,
                    AnalysisUrl + "/api/v1/faculty/context/save", new
                    {
                        PersonelID = ServiceAcceptanceHost.SubjectId, ExpectedVersion = 0,
                        Context = new { Language = "tr", ResearchGoals = new[] { "Yöntem incelemesi" },
                            Courses = new[] { "Makine öğrenmesi" }, TeachingAudience = "Lisansüstü" }
                    });
                createdDossier = await PostTypedAsync<HrEvidenceDossierResponse>(client,
                    AnalysisUrl + "/api/v1/hr/dossiers/create", new
                    { PersonelID = ServiceAcceptanceHost.SubjectId, PublicationMetricSnapshotId = metrics.SnapshotId,
                        CanonicalWorkIds = canonicalIds, Language = "tr" });
                HrEvidenceDossierResponse readDossier = await PostTypedAsync<HrEvidenceDossierResponse>(client,
                    AnalysisUrl + "/api/v1/hr/dossiers",
                    new { PersonelID = ServiceAcceptanceHost.SubjectId, createdDossier.DossierId });
                if (readDossier.InputFingerprint != createdDossier.InputFingerprint ||
                    readDossier.Dossier.PublicationMetrics is null ||
                    readDossier.Dossier.Works.Count != 2 || readDossier.Dossier.Works.Any(value => value.Reviews.Count != 0))
                    throw new InvalidOperationException("HR dossier create/read did not preserve metrics and honest empty reviews.");
                if (JsonSerializer.Serialize(createdDossier, JsonOptions) != JsonSerializer.Serialize(readDossier, JsonOptions))
                    throw new InvalidOperationException("HR dossier create/read typed payloads differ.");
                result["context"] = JsonSerializer.SerializeToNode(context, JsonOptions);
                result["hrDossierCreated"] = JsonSerializer.SerializeToNode(createdDossier, JsonOptions);
                result["hrDossierRead"] = JsonSerializer.SerializeToNode(readDossier, JsonOptions);
                adamPending = await StartFacultyAsync(client, adamRequestId, "OwnPaperMethods", AdamQuery,
                    adamId, context.Version);
                footballPending = await StartFacultyAsync(client, footballRequestId, "TeachingHelp", FootballQuery,
                    footballId, context.Version);
                if (adamPending.Status != "Pending" || footballPending.Status != "Pending")
                    throw new InvalidOperationException("Faculty requests were not durably queued before restart.");
            }

            FacultyAssistantRunResponse adam;
            FacultyAssistantRunResponse football;
            FacultyAssistantRunResponse replayedAdam;
            FacultyAssistantRunResponse replayedFootball;
            await using (WebApplication facultyHost = await ServiceAcceptanceHost.StartCollectorAsync(
                root, database, Url, AnalysisUrl, "faculty", replay))
            using (HttpClient client = Client(authenticated: true))
            {
                adam = await PollFacultyAsync(client, adamPending.RunId);
                football = await PollFacultyAsync(client, footballPending.RunId);
                replayedAdam = await StartFacultyAsync(client, adamRequestId,
                    "OwnPaperMethods", AdamQuery, adamId, context.Version);
                replayedFootball = await StartFacultyAsync(client, footballRequestId,
                    "TeachingHelp", FootballQuery, footballId, context.Version);
                if (!replayedAdam.Reused || !replayedFootball.Reused || analysisCapture.FacultyCalls != 2)
                    throw new InvalidOperationException("Faculty read/replay did not remain zero-dispatch.");
            }
            result["faculty"] = JsonSerializer.SerializeToNode(new
            {
                adamQuery = AdamQuery, adam, replayedAdam,
                footballQuery = FootballQuery, football, replayedFootball,
                analysisCapture.FacultyCalls
            }, JsonOptions);

            await using (AnalysisDbContext db = Database(database))
            {
                int works = await db.AcademicWorks.CountAsync(value => value.PersonelId == ServiceAcceptanceHost.SubjectId);
                int canonical = await db.CanonicalResearcherWorks.CountAsync(value => value.PersonelId == ServiceAcceptanceHost.SubjectId);
                int completedSummaries = await db.ArticleSummaryAutomationJobs.CountAsync(value =>
                    value.Status == ArticleSummaryAutomationJobStatus.Succeeded);
                result["sql"] = new JsonObject { ["academicWorks"] = works,
                    ["canonicalAssociations"] = canonical, ["completedSummaryJobs"] = completedSummaries,
                    ["metricSnapshots"] = await db.PublicationMetricSnapshots.CountAsync(),
                    ["hrDossiers"] = await db.HrEvidenceDossiers.CountAsync(),
                    ["facultyRuns"] = await db.FacultyAssistantRuns.CountAsync(),
                    ["migrationsApplied"] = true };
                result["replayAudit"] = replay.ToJson();
                result["analysisCapture"] = JsonSerializer.SerializeToNode(analysisCapture, JsonOptions);
                result["ready"] = works == 2 && canonical == 2 && completedSummaries == 2 &&
                    replay.ProviderServed == 2 && replay.SourceServed == 2 &&
                    analysisCapture.SummaryCalls == 2 && analysisCapture.FacultyCalls == 2 &&
                    metrics.Status == "Current" && metrics.SnapshotId.HasValue &&
                    adam.Status == "Completed" && football.Status == "Completed";
            }
            await WriteAsync(root, result);
            await DropAsync(master, name);
            result["cleanup"] = "exact owned database dropped";
            await WriteAsync(root, result);
            Console.WriteLine(result["ready"]!.GetValue<bool>() ? "REHEARSAL_READY" : "REHEARSAL_BLOCKED");
            return result["ready"]!.GetValue<bool>() ? 0 : 1;
        }
        catch (Exception exception)
        {
            result["ready"] = false;
            result["failure"] = new JsonObject { ["type"] = exception.GetType().Name,
                ["message"] = exception.Message };
            result["cleanup"] = "preserved for diagnosis";
            await WriteAsync(root, result);
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static HttpClient Client()
    {
        HttpClient client = new() { BaseAddress = new(Url), Timeout = TimeSpan.FromSeconds(30) };
        return client;
    }

    private static async Task<JsonObject> PollAsync(HttpClient client, Guid batchId)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            JsonObject status = await PostAsync(client,
                "/Services/AcademicPerformance/V1/Bulk/Status", new { BatchId = batchId });
            if ((status["IsComplete"] ?? status["isComplete"])?.GetValue<bool>() == true) return status;
            await Task.Delay(250);
        }
        throw new TimeoutException("Bulk replay did not complete.");
    }

    private static async Task WaitForSummariesAsync(string connection, int[] ids)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(40);
        while (DateTime.UtcNow < deadline)
        {
            await using AnalysisDbContext db = Database(connection);
            string[] statuses = await db.ArticleSummaryAutomationJobs.AsNoTracking()
                .Where(value => ids.Contains(value.CanonicalWorkId)).Select(value => value.Status).ToArrayAsync();
            if (statuses.Length == 2 && statuses.All(value => value == ArticleSummaryAutomationJobStatus.Succeeded))
                return;
            if (statuses.Any(value => value == ArticleSummaryAutomationJobStatus.Failed))
                throw new InvalidOperationException("A synthetic automatic summary failed terminally.");
            await Task.Delay(250);
        }
        throw new TimeoutException("Synthetic summary processing did not complete.");
    }

    private static async Task<JsonNode> ReadSummaryAuditAsync(string connection, int[] ids)
    {
        await using AnalysisDbContext db = Database(connection);
        var rows = await db.CanonicalArticleAnalysisRuns.AsNoTracking()
            .Include(value => value.ArticleSourceSnapshot).ThenInclude(value => value!.Pages)
            .Where(value => ids.Contains(value.CanonicalWorkId)).OrderBy(value => value.CanonicalWorkId).ToListAsync();
        if (rows.Count != 2 || rows.Any(value => value.ArticleSourceSnapshot?.SourceKind != "pdf"))
            throw new InvalidOperationException("Synthetic summary route did not persist two PDF-backed runs.");
        return JsonSerializer.SerializeToNode(rows.Select(value => new
        {
            value.CanonicalWorkId, value.Model, value.VerificationModel,
            sourceHash = value.ArticleSourceSnapshot!.ExtractedTextHash,
            pages = value.ArticleSourceSnapshot.Pages.Count, value.ProcessedPages, value.TotalPages
        }), JsonOptions)!;
    }

    private static async Task<ResearcherPublicationMetricsStatusResponse> PollMetricsAsync(HttpClient client)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            ResearcherPublicationMetricsStatusResponse value = await PostTypedAsync<ResearcherPublicationMetricsStatusResponse>(client,
                AnalysisUrl + "/api/v1/researchers/metrics",
                new { PersonelID = ServiceAcceptanceHost.SubjectId });
            if (value.Status == "Current" && value.SnapshotId.HasValue && value.Data is not null &&
                value.RequestedRevision == value.ComputedRevision && !value.IsStale) return value;
            if (value.Status == "Failed") throw new InvalidOperationException("Synthetic metrics refresh failed.");
            await Task.Delay(250);
        }
        throw new TimeoutException("Synthetic metrics refresh did not become current.");
    }

    private static Task<FacultyAssistantRunResponse> StartFacultyAsync(HttpClient client, Guid requestId,
        string mode, string query, int canonicalWorkId, int contextVersion) =>
        PostTypedAsync<FacultyAssistantRunResponse>(client,
            AnalysisUrl + "/api/v1/faculty/assistant/start", new
            {
                PersonelID = ServiceAcceptanceHost.SubjectId, ClientRequestId = requestId,
                Mode = mode, Language = "tr", Query = query, CanonicalWorkIds = new[] { canonicalWorkId },
                Take = 10, ContextVersion = contextVersion
            });

    private static async Task<FacultyAssistantRunResponse> PollFacultyAsync(HttpClient client, Guid runId)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            FacultyAssistantRunResponse value = await PostTypedAsync<FacultyAssistantRunResponse>(client,
                AnalysisUrl + "/api/v1/faculty/assistant/run",
                new { PersonelID = ServiceAcceptanceHost.SubjectId, RunId = runId });
            if (value.Status == "Completed") return value;
            if (value.Status is "Failed" or "Interrupted")
                throw new InvalidOperationException($"Synthetic faculty run ended {value.Status}: {value.ErrorCode}");
            await Task.Delay(250);
        }
        throw new TimeoutException("Synthetic faculty processing did not complete.");
    }

    private static async Task<T> PostTypedAsync<T>(HttpClient client, string path, object body)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(path, body);
        string text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{path} returned {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<T>(text, JsonOptions) ?? throw new JsonException();
    }

    private static HttpClient Client(bool authenticated)
    {
        HttpClient client = Client();
        if (authenticated)
            client.DefaultRequestHeaders.Add(ServiceAcceptanceHost.Header, ServiceAcceptanceHost.HeaderValue);
        return client;
    }

    private static async Task<JsonObject> PostAsync(HttpClient client, string path, object body)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(path, body);
        string text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{path} returned {(int)response.StatusCode}: {text}");
        return JsonNode.Parse(text)?.AsObject() ?? throw new JsonException();
    }

    private static async Task<WebApplication> StartSyntheticAnalysisAsync(SyntheticAnalysisCapture capture)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls(AnalysisUrl);
        WebApplication application = builder.Build();
        application.MapPost("/api/v1/articles/summary/generate", (SummarizeArticleRequest request) =>
        {
            capture.AddSummary();
            ArticleSourceSpan span = request.SourceSpans!.First(value => !string.IsNullOrWhiteSpace(value.Text));
            ArticleEvidence evidence = new(span.Text, span.PageNumber)
            { SourceId = span.SourceId, StartOffset = span.StartOffset, EndOffset = span.EndOffset };
            ArticleClaim claim = new(
                "Makalenin yöntem, bulgular ve sınırlılık bölümleri kaynak metinle ilişkilendirilmiştir.",
                [evidence]) { ClaimId = "synthetic-methods-findings-limitations" };
            ArticleCoverage coverage = new(1, 1, request.Pages.Count, request.Pages.Count,
                request.TotalSourcePages, 0, request.IsPartial, request.ScopeReason)
            {
                CandidateClaims = 1, AutomaticallyCheckedClaims = 1, SupportedClaims = 1,
                UnsupportedClaims = 0, UncertainClaims = 0, DuplicateOrCappedClaims = 0,
                BudgetUnverifiedClaims = 0, OmissionReasons = []
            };
            return Results.Json(new ArticleSummaryReport(request.Language, request.SourceKind,
                request.SourceHash, request.ExtractionVersion, coverage,
                new([], [claim], [], [], []), "synthetic-rehearsal-model", "synthetic-summary-v1")
            {
                Verification = new("automatically_checked", "synthetic-rehearsal-verifier",
                    "synthetic-summary-verification-v1", false, "Offline route rehearsal only."),
                SourceFidelity = ArticleSourceFidelity.Create(request.SourceKind, request.ExtractionVersion)
            });
        });
        application.MapPost("/api/v1/faculty-assistant", (FacultyAssistantAnalysisRequest request) =>
        {
            capture.AddFaculty();
            FacultyAssistantEvidence evidence = request.Evidence[0];
            string kind = request.Mode == "TeachingHelp" ? "teaching_adaptation" : "source_observation";
            FacultyAssistantAnswerItem item = new(kind,
                "Kaynak metindeki yöntemsel gözlem kullanıldı.",
                kind == "source_observation" ? null : "Bu örneği derste tartışma sorusuyla birlikte kullanın.",
                [new(evidence.EvidenceId, evidence.ExactText)]);
            return Results.Json(new FacultyAssistantAnalysisReport(request.Mode, request.Language,
                [item], "synthetic-rehearsal-model", "synthetic-faculty-v1",
                new("automatically_checked", "synthetic-rehearsal-verifier",
                    "synthetic-faculty-verification-v1", false, "Offline route rehearsal only."),
                "completed", new(1, 1, 1, 0, 0, 0, false)));
        });
        await application.StartAsync();
        return application;
    }

    private static async Task WriteAsync(string root, JsonObject result)
    {
        string directory = Path.Combine(root, "docs", Program.RunId);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "rehearsal.json"),
            JsonSerializer.Serialize(result, JsonOptions));
    }

    internal static (string Master, string Database) Connections(string name)
    {
        SqlConnectionStringBuilder master = new()
        { DataSource = @"(localdb)\MSSQLLocalDB", InitialCatalog = "master", IntegratedSecurity = true,
            TrustServerCertificate = true, ConnectTimeout = 30 };
        SqlConnectionStringBuilder database = new(master.ConnectionString) { InitialCatalog = name };
        return (master.ConnectionString, database.ConnectionString);
    }

    internal static AnalysisDbContext Database(string connection) => new(
        new DbContextOptionsBuilder<AnalysisDbContext>().UseSqlServer(connection).Options);

    internal static async Task DropAsync(string master, string name)
    {
        if (!name.StartsWith("AcademicServiceAcceptance_", StringComparison.Ordinal) || name.Length != 58)
            throw new InvalidOperationException("Refusing to drop a database outside the exact acceptance prefix.");
        await using SqlConnection connection = new(master);
        await connection.OpenAsync();
        using SqlCommand command = connection.CreateCommand();
        command.CommandText = $"IF DB_ID(N'{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END";
        await command.ExecuteNonQueryAsync();
    }
}

internal sealed class SyntheticAnalysisCapture
{
    private int summaryCalls;
    private int facultyCalls;
    public int SummaryCalls => Volatile.Read(ref summaryCalls);
    public int FacultyCalls => Volatile.Read(ref facultyCalls);
    public void AddSummary() => Interlocked.Increment(ref summaryCalls);
    public void AddFaculty() => Interlocked.Increment(ref facultyCalls);
}
