using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AcademicCollector.Analysis.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Analysis;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;
using AcademicCollectorDemo.Modules.AcademicPerformance.FacultyAssistant;
using AcademicCollectorDemo.Modules.AcademicPerformance.ProductAccess;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Knowledge;
using AcademicCollectorDemo.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class FacultyAssistantProcessorTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task EnqueueProcessReadAndDuplicate_CompleteOnceWithPinnedEvidence()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        (string personelId, int workId) = await SeedOwnerAsync(database);
        FacultyAssistantContextVersion context = new()
        {
            PersonelId = personelId, Version = 3, ContextJson = "{}",
            ContextFingerprint = new string('d', 64), CreatedByActorId = "actor",
            CreatedAt = DateTimeOffset.UtcNow
        };
        database.FacultyAssistantContextVersions.Add(context);
        await database.SaveChangesAsync();
        AcademicProductAccessGrant grant = new("grant", "actor", personelId,
            AcademicProductOperation.FacultyAssistantStart);
        FacultyAssistantScheduler scheduler = new(database, new FakeSearch(workId),
            Options.Create(new FacultyAssistantOptions()));
        StartFacultyAssistantRequest request = new() { PersonelId = personelId,
            ClientRequestId = Guid.NewGuid(), Mode = "OwnPaperMethods", Language = "en", Query = "method",
            ContextVersion = context.Version };
        FacultyAssistantRunResponse queued = await scheduler.EnqueueAsync(grant, request, default);
        FacultyAssistantRunResponse duplicate = await scheduler.EnqueueAsync(grant, request, default);
        Assert.Equal(queued.RunId, duplicate.RunId); Assert.True(duplicate.Reused);
        Assert.Equal("faculty-assistant-retrieval-v1", queued.RetrievalPolicyVersion);
        Assert.Equal(context.Id, queued.Context!.ContextVersionId);
        Assert.Equal(context.Version, queued.Context.Version);
        Assert.Equal(context.ContextFingerprint, queued.Context.Fingerprint);
        Assert.Equal("abstract", queued.Retrieval.SourceKindCoverage.Single().Value);
        Assert.Equal("abstract", queued.Retrieval.Evidence.Single().SourceKind);
        Assert.True(queued.Retrieval.Evidence.Single().IsPartial);
        Assert.Equal(1, queued.Retrieval.Evidence.Single().SourceCoverage.ProcessedChunks);
        Assert.Equal(2, queued.Retrieval.Evidence.Single().SourceCoverage.TotalChunks);

        int requests = 0;
        HttpClient http = new(new StubHandler(async message =>
        {
            requests++;
            FacultyAssistantAnalysisRequest input = (await message.Content!.ReadFromJsonAsync<FacultyAssistantAnalysisRequest>())!;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(
                new FacultyAssistantAnalysisReport(input.Mode, input.Language,
                    [new FacultyAssistantAnswerItem("source_observation", "Source.", null,
                        [new(input.Evidence[0].EvidenceId, input.Evidence[0].ExactText)])
                        { CandidateId = "assistant-1" }],
                    "gemini-3.8-flash", "faculty-evidence-assistant-v10", new("automatically_checked",
                        "gemini-3.8-flash", "faculty-evidence-assistant-verification-v7", true,
                        "Synthetic verification."),
                    "completed", new(1, 1, 1, 0, 0, 0, false))
                {
                    RequestCoverage = new("fulfilled",
                        [new("requirement-1", "Explain the method", "fulfilled", [1],
                            "The retained item fulfills the request.")],
                        "gemini-3.8-flash", "faculty-request-coverage-v1", true,
                        "Synthetic automatic request coverage."),
                    Generation = SyntheticGeneration(),
                    SourceChecks = SyntheticSourceChecks(input, 1, 0, 0),
                    Repair = NotRunRepair()
                }) };
        })) { BaseAddress = new Uri("http://127.0.0.1/") };
        FacultyAssistantProcessor processor = new(database,
            new FacultyAssistantServiceClient(http, Options.Create(new AnalysisServiceOptions())),
            new FakeAccess(false), NullLogger<FacultyAssistantProcessor>.Instance);
        Assert.True(await processor.ProcessNextAsync(default));
        FacultyAssistantRunResponse saved = (await new FacultyAssistantReadService(database).GetAsync(
            grant with { Operation = AcademicProductOperation.FacultyAssistantRead }, queued.RunId, default))!;
        Assert.Equal("Completed", saved.Status); Assert.NotNull(saved.Report); Assert.Equal(1, requests);
        Assert.Equal(queued.Retrieval.InputHash, saved.Retrieval.InputHash);
        Assert.Equal(queued.Retrieval.Evidence.Single().EvidenceId,
            saved.Retrieval.Evidence.Single().EvidenceId);
        Assert.Equal(queued.Retrieval.Evidence.Single().PinnedFreshnessStatus,
            saved.Retrieval.Evidence.Single().PinnedFreshnessStatus);
        Assert.Equal(queued.Retrieval.Evidence.Single().PinnedFreshnessReasons,
            saved.Retrieval.Evidence.Single().PinnedFreshnessReasons);
        Assert.Equal(queued.Retrieval.Evidence.Single().CurrentFreshnessStatus,
            saved.Retrieval.Evidence.Single().CurrentFreshnessStatus);
        Assert.Equal(queued.Retrieval.Evidence.Single().CurrentFreshnessReasons,
            saved.Retrieval.Evidence.Single().CurrentFreshnessReasons);
        Assert.Equal(queued.Context, saved.Context);
        Assert.Equal(new FacultyAssistantCoverage(1, 1, 1, 0, 0, 0, false), saved.Report.Coverage);
        FacultyAssistantRunResponse reusedAfterCompletion = await scheduler.EnqueueAsync(grant, request, default);
        Assert.True(reusedAfterCompletion.Reused);
        Assert.Equal(saved.Report.Coverage, reusedAfterCompletion.Report!.Coverage);
        Assert.False(await processor.ProcessNextAsync(default)); Assert.Equal(1, requests);
    }

    [Fact]
    public async Task GetAsync_LegacyStoredReportWithoutCoverageRemainsReadable()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        (string personelId, int workId) = await SeedOwnerAsync(database);
        FacultyAssistantRun run = Run(personelId, workId);
        run.Status = "Completed";
        run.ReportJson = JsonSerializer.Serialize(new
        {
            mode = "OwnPaperMethods", language = "en", items = Array.Empty<object>(),
            model = "gemini-3.8-flash", promptVersion = "faculty-evidence-assistant-v2",
            verification = new { status = "not_run", model = "", promptVersion = "",
                usesSameModelFamily = false, limitation = "Legacy empty report." },
            outcome = "no_supported_items"
        });
        database.FacultyAssistantRuns.Add(run);
        await database.SaveChangesAsync();
        AcademicProductAccessGrant grant = new("grant", "actor", personelId,
            AcademicProductOperation.FacultyAssistantRead);

        FacultyAssistantRunResponse response = (await new FacultyAssistantReadService(database)
            .GetAsync(grant, run.RunId, default))!;

        Assert.Equal("Completed", response.Status);
        Assert.Equal("no_supported_items", response.Report!.Outcome);
        Assert.Null(response.Report.Coverage);
        Assert.Null(response.Report.RequestCoverage);
        Assert.Null(response.Report.SourceChecks);
        Assert.Null(response.Report.Repair);
    }

    [Theory]
    [InlineData("partial", 1, 0, 1)]
    [InlineData("no_supported_items", 0, 1, 1)]
    public async Task ProcessReadAndReplay_VerifiedOmissionsPersistAsCompletedReport(
        string outcome, int supported, int unsupported, int uncertain)
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        (string personelId, int workId) = await SeedOwnerAsync(database);
        AcademicProductAccessGrant grant = new("grant", "actor", personelId,
            AcademicProductOperation.FacultyAssistantStart);
        FacultyAssistantScheduler scheduler = new(database, new FakeSearch(workId),
            Options.Create(new FacultyAssistantOptions()));
        StartFacultyAssistantRequest request = new() { PersonelId = personelId,
            ClientRequestId = Guid.NewGuid(), Mode = "OwnPaperMethods", Language = "en", Query = "method" };
        FacultyAssistantRunResponse queued = await scheduler.EnqueueAsync(grant, request, default);
        int requests = 0;
        HttpClient http = new(new StubHandler(async message =>
        {
            requests++;
            FacultyAssistantAnalysisRequest input = (await message.Content!
                .ReadFromJsonAsync<FacultyAssistantAnalysisRequest>())!;
            IReadOnlyList<FacultyAssistantAnswerItem> items = supported == 0 ? [] :
                [new FacultyAssistantAnswerItem("source_observation", "Source.", null,
                    [new(input.Evidence[0].EvidenceId, input.Evidence[0].ExactText)])
                    { CandidateId = "assistant-1" }];
            FacultyRequestCoverage requestCoverage = supported == 0
                ? new("unanswered", [new("requirement-1", "Original request", "unanswered", [],
                    "No retained items.")], "", "", false, "No coverage call was made.")
                : new("fulfilled", [new("requirement-1", "Explain the method", "fulfilled", [1],
                    "The retained item fulfills the request.")], "gemini-3.8-flash",
                    "faculty-request-coverage-v1", true, "Synthetic automatic request coverage.");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(
                new FacultyAssistantAnalysisReport(input.Mode, input.Language, items,
                    "gemini-3.8-flash", "faculty-evidence-assistant-v10",
                    new("automatically_checked", "gemini-3.8-flash",
                        "faculty-evidence-assistant-verification-v7", true, "Synthetic verification."),
                    outcome, new(2, 2, supported, unsupported, uncertain,
                        unsupported + uncertain, true))
                {
                    RequestCoverage = requestCoverage,
                    Generation = SyntheticGeneration(),
                    SourceChecks = SyntheticSourceChecks(input, supported, unsupported, uncertain),
                    Repair = CompletedEmptyRepair(Math.Min(2, unsupported + uncertain))
                }) };
        })) { BaseAddress = new Uri("http://127.0.0.1/") };
        FacultyAssistantProcessor processor = new(database,
            new FacultyAssistantServiceClient(http, Options.Create(new AnalysisServiceOptions())),
            new FakeAccess(false), NullLogger<FacultyAssistantProcessor>.Instance);

        Assert.True(await processor.ProcessNextAsync(default));
        FacultyAssistantRunResponse saved = (await new FacultyAssistantReadService(database).GetAsync(
            grant with { Operation = AcademicProductOperation.FacultyAssistantRead }, queued.RunId, default))!;
        FacultyAssistantRunResponse replay = await scheduler.EnqueueAsync(grant, request, default);

        Assert.Equal("Completed", saved.Status);
        Assert.Equal(outcome, saved.Report!.Outcome);
        Assert.Equal(supported, saved.Report.Items.Count);
        Assert.Equal(saved.Report.Coverage, replay.Report!.Coverage);
        Assert.True(replay.Reused);
        Assert.Equal(1, requests);
    }

    private static FacultyAssistantGeneration SyntheticGeneration() => new(
        [new(1, "high", "Success", "gemini-3.8-flash", 120, 0.000150m,
            "gemini-3.8-flash-standard-through-2026-12-31", true) { AttemptId = Guid.NewGuid() }], false);

    private static FacultyAssistantSourceChecks SyntheticSourceChecks(FacultyAssistantAnalysisRequest input,
        int supported, int unsupported, int uncertain)
    {
        string[] statuses = Enumerable.Repeat("supported", supported)
            .Concat(Enumerable.Repeat("unsupported", unsupported))
            .Concat(Enumerable.Repeat("uncertain", uncertain)).ToArray();
        FacultyAssistantSourceCheck[] checks = statuses.Select((status, index) => new FacultyAssistantSourceCheck(
            Guid.NewGuid(), $"assistant-{index + 1}", "initial", status, "Synthetic verdict.",
            "gemini-3.8-flash", "faculty-evidence-assistant-verification-v7",
            [input.Evidence[0].EvidenceId])).ToArray();
        return new(checks.Length, checks.Length, supported, unsupported, uncertain, 0, 0, checks);
    }

    private static FacultyAssistantRepair NotRunRepair() => new("not_run", 0, 0, 0, "", "", null);

    private static FacultyAssistantRepair CompletedEmptyRepair(int requested) => new("completed", requested,
        0, 0, "gemini-3.8-flash", "faculty-evidence-assistant-repair-v2",
        new(1, "medium", "Success", "gemini-3.8-flash", 120, 0.000150m,
            "gemini-3.8-flash-standard-through-2026-12-31", true) { AttemptId = Guid.NewGuid() });

    [Fact]
    public async Task EnqueueAsync_OversizedAuthorizedInputIsRejectedWithoutQueueRow()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        (string personelId, int workId) = await SeedOwnerAsync(database);
        AcademicProductAccessGrant grant = new("grant", "actor", personelId,
            AcademicProductOperation.FacultyAssistantStart);
        FacultyAssistantScheduler scheduler = new(database,
            new FakeSearch(workId, hitCount: 9, evidenceTextLength: 12000),
            Options.Create(new FacultyAssistantOptions()));
        StartFacultyAssistantRequest request = new()
        {
            PersonelId = personelId, ClientRequestId = Guid.NewGuid(), Mode = "OwnPaperMethods",
            Language = "en", Query = "method", CanonicalWorkIds = [workId], Take = 20
        };

        await Assert.ThrowsAsync<FacultyAssistantInputException>(() =>
            scheduler.EnqueueAsync(grant, request, default));
        Assert.False(await database.FacultyAssistantRuns.AnyAsync(value =>
            value.PersonelId == personelId));
    }

    [Fact]
    public async Task EnqueueAsync_KnownStaleEvidenceIsRejectedWithoutQueueRow()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        (string personelId, int workId) = await SeedOwnerAsync(database);
        AcademicProductAccessGrant grant = new("grant", "actor", personelId,
            AcademicProductOperation.FacultyAssistantStart);
        FacultyAssistantScheduler scheduler = new(database,
            new FakeSearch(workId, freshnessStatus: AnalysisFreshnessStatus.Stale),
            Options.Create(new FacultyAssistantOptions()));
        StartFacultyAssistantRequest request = new()
        {
            PersonelId = personelId, ClientRequestId = Guid.NewGuid(), Mode = "OwnPaperMethods",
            Language = "en", Query = "method", CanonicalWorkIds = [workId]
        };

        FacultyAssistantInputException error = await Assert.ThrowsAsync<FacultyAssistantInputException>(() =>
            scheduler.EnqueueAsync(grant, request, default));

        Assert.Contains("stale", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(await database.FacultyAssistantRuns.AnyAsync(value =>
            value.PersonelId == personelId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProcessNextAsync_PinnedEvidenceDriftFailsBeforeProviderDispatch(bool newerReplacement)
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        (string personelId, int workId) = await SeedOwnerAsync(database);
        CanonicalArticleAnalysisRun pinned = await AddAnalysisRunAsync(database, personelId, workId);
        if (newerReplacement)
        {
            _ = await AddAnalysisRunAsync(database, personelId, workId);
        }
        else
        {
            database.ArticleSummaryAutomationJobs.Add(new()
            {
                CanonicalWorkId = workId, Language = "tr",
                Status = ArticleSummaryAutomationJobStatus.Pending,
                DesiredInputHash = "input-b", DesiredPolicyVersion = "article-summary-v5",
                ProcessedInputHash = "input-a", ProcessedPolicyVersion = "article-summary-v5",
                LastSuccessfulAnalysisRunId = pinned.Id,
                NextAttemptAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            });
        }
        FacultyAssistantRun run = Run(personelId, workId);
        run.RetrievalManifestJson = JsonSerializer.Serialize(new AcademicEvidenceSearchResponse
        {
            Hits = [new() { AnalysisRunId = pinned.Id, CanonicalWorkId = workId }]
        });
        database.FacultyAssistantRuns.Add(run);
        await database.SaveChangesAsync();
        int requests = 0;
        HttpClient http = new(new StubHandler(_ =>
        {
            requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        })) { BaseAddress = new Uri("http://127.0.0.1/") };
        FacultyAssistantProcessor processor = new(database,
            new FacultyAssistantServiceClient(http, Options.Create(new AnalysisServiceOptions())),
            new FakeAccess(false), NullLogger<FacultyAssistantProcessor>.Instance,
            Options.Create(new ArticleSummaryAutomationOptions()));

        Assert.True(await processor.ProcessNextAsync(default));

        database.ChangeTracker.Clear();
        FacultyAssistantRun saved = await database.FacultyAssistantRuns.SingleAsync(value => value.Id == run.Id);
        Assert.Equal("Failed", saved.Status);
        Assert.Equal("EvidenceChanged", saved.ErrorCode);
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task ReadAsync_LegacyManifestReportsPinnedFreshnessAsUnknown()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        (string personelId, int workId) = await SeedOwnerAsync(database);
        FacultyAssistantRun run = Run(personelId, workId);
        run.Status = "Completed";
        run.RetrievalManifestJson = $$"""
            { "hits": [{ "analysisRunId": 999999999, "canonicalWorkId": {{workId}} }] }
            """;
        database.FacultyAssistantRuns.Add(run);
        await database.SaveChangesAsync();
        AcademicProductAccessGrant grant = new("grant", "actor", personelId,
            AcademicProductOperation.FacultyAssistantRead);

        FacultyAssistantRunResponse saved = (await new FacultyAssistantReadService(database)
            .GetAsync(grant, run.RunId, default))!;

        Assert.Null(saved.Retrieval.PinnedFreshnessHash);
        Assert.Null(saved.Retrieval.PinnedCurrentAnalysisRunCount);
        Assert.Null(saved.Retrieval.PinnedStaleAnalysisRunCount);
        Assert.Null(saved.Retrieval.PinnedUnknownAnalysisRunCount);
        Assert.Equal(AnalysisFreshnessStatus.Unknown,
            saved.Retrieval.Evidence.Single().PinnedFreshnessStatus);
        Assert.Contains("FreshnessNotRecorded",
            saved.Retrieval.Evidence.Single().PinnedFreshnessReasons!);
    }

    [Fact]
    public async Task ProcessNextAsync_AbandonedRunningRunBecomesInterruptedAndIsNotRetried()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        (string personelId, int workId) = await SeedOwnerAsync(database);
        FacultyAssistantRun run = Run(personelId, workId); run.Status = "Running";
        run.AttemptCount = 1; run.AttemptToken = Guid.NewGuid();
        database.FacultyAssistantRuns.Add(run); await database.SaveChangesAsync();
        int requests = 0;
        HttpClient http = new(new StubHandler(_ => { requests++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); }))
            { BaseAddress = new Uri("http://127.0.0.1/") };
        FacultyAssistantProcessor processor = new(database,
            new FacultyAssistantServiceClient(http, Options.Create(new AnalysisServiceOptions())),
            new FakeAccess(false), NullLogger<FacultyAssistantProcessor>.Instance);
        Assert.False(await processor.ProcessNextAsync(default));
        database.ChangeTracker.Clear();
        FacultyAssistantRun saved = await database.FacultyAssistantRuns.SingleAsync(value => value.Id == run.Id);
        Assert.Equal("Interrupted", saved.Status); Assert.Equal(1, saved.AttemptCount); Assert.Equal(0, requests);
    }

    [Fact]
    public async Task ProcessNextAsync_UpstreamCancellationBecomesInterruptedAndIsNotRetried()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        (string personelId, int workId) = await SeedOwnerAsync(database);
        FacultyAssistantRun run = Run(personelId, workId);
        database.FacultyAssistantRuns.Add(run);
        await database.SaveChangesAsync();
        int requests = 0;
        HttpClient http = new(new StubHandler(_ =>
        {
            requests++;
            return Task.FromCanceled<HttpResponseMessage>(new CancellationToken(canceled: true));
        })) { BaseAddress = new Uri("http://127.0.0.1/") };
        FacultyAssistantProcessor processor = new(database,
            new FacultyAssistantServiceClient(http, Options.Create(new AnalysisServiceOptions())),
            new FakeAccess(false), NullLogger<FacultyAssistantProcessor>.Instance);

        Assert.True(await processor.ProcessNextAsync(default));
        database.ChangeTracker.Clear();
        FacultyAssistantRun saved = await database.FacultyAssistantRuns.SingleAsync(value => value.Id == run.Id);
        Assert.Equal("Interrupted", saved.Status);
        Assert.Equal("Interrupted", saved.ErrorCode);
        Assert.Equal(1, saved.AttemptCount);
        Assert.Equal(1, requests);
        Assert.False(await processor.ProcessNextAsync(default));
        Assert.Equal(1, requests);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ProcessNextAsync_RevokedPermissionOrAssociationMakesNoProviderRequest(
        bool revokePermission, bool removeAssociation)
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        string personelId = "faculty-" + Guid.NewGuid().ToString("N");
        Researcher researcher = new() { PersonelId = personelId };
        CanonicalWork work = new() { NormalizedDoi = "10.9999/" + Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        database.AddRange(researcher, work); await database.SaveChangesAsync();
        if (!removeAssociation)
        {
            database.CanonicalResearcherWorks.Add(new() { PersonelId = personelId,
                CanonicalWorkId = work.Id, LastObservedAt = DateTime.UtcNow });
        }
        FacultyAssistantAnalysisRequest input = new()
        {
            Mode = "OwnPaperMethods", Language = "en", Query = "method",
            EvidenceCatalogHash = new string('a', 64), Evidence = [new(
                $"work:{work.Id}:snapshot:1:span:1", work.Id, 1, "src-1", 1, 0, 7, "Source.", "pdf", false)]
        };
        FacultyAssistantRun run = new()
        {
            RunId = Guid.NewGuid(), PersonelId = personelId, ActorAuditId = "actor-1",
            AuthorizationGrantId = "grant-1", ClientRequestId = Guid.NewGuid(), Mode = input.Mode,
            Language = input.Language, RetrievalPolicyVersion = "v1", Status = "Pending",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            RequestJson = "{}", RetrievalManifestJson = "{}",
            AuthorizedInputJson = JsonSerializer.Serialize(input), InputFingerprint = new string('b', 64)
        };
        database.FacultyAssistantRuns.Add(run); await database.SaveChangesAsync();
        int requests = 0;
        HttpClient http = new(new StubHandler(_ => { requests++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); }))
            { BaseAddress = new Uri("http://127.0.0.1/") };
        FacultyAssistantProcessor processor = new(database,
            new FacultyAssistantServiceClient(http, Options.Create(new AnalysisServiceOptions())),
            new FakeAccess(revokePermission), NullLogger<FacultyAssistantProcessor>.Instance);

        Assert.True(await processor.ProcessNextAsync(default));

        database.ChangeTracker.Clear();
        FacultyAssistantRun saved = await database.FacultyAssistantRuns.SingleAsync(value => value.Id == run.Id);
        Assert.Equal("Failed", saved.Status);
        Assert.Equal("AccessRevoked", saved.ErrorCode);
        Assert.Equal(1, saved.AttemptCount);
        Assert.Equal(0, requests);
    }

    private sealed class FakeAccess(bool revoke) : IAcademicProductAccessService
    {
        public Task<AcademicProductAccessGrant> AuthorizeAsync(System.Security.Claims.ClaimsPrincipal principal,
            AcademicProductAccessRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AcademicProductAccessGrant> ReauthorizeAsync(AcademicProductAccessGrant persistedGrant,
            CancellationToken cancellationToken) => revoke
            ? throw new AcademicProductAccessDeniedException()
            : Task.FromResult(persistedGrant);
    }

    private static async Task<(string PersonelId, int WorkId)> SeedOwnerAsync(AcademicDbContext database)
    {
        string personelId = "faculty-owner-" + Guid.NewGuid().ToString("N");
        Researcher researcher = new() { PersonelId = personelId };
        CanonicalWork work = new() { NormalizedDoi = "10.7070/" + Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        database.AddRange(researcher, work); await database.SaveChangesAsync();
        database.CanonicalResearcherWorks.Add(new() { PersonelId = personelId,
            CanonicalWorkId = work.Id, LastObservedAt = DateTime.UtcNow });
        await database.SaveChangesAsync(); return (personelId, work.Id);
    }

    private static FacultyAssistantRun Run(string personelId, int workId)
    {
        FacultyAssistantAnalysisRequest input = new() { Mode = "OwnPaperMethods", Language = "en",
            Query = "method", EvidenceCatalogHash = new string('a', 64), Evidence = [new(
                $"work:{workId}:snapshot:1:span:1", workId, 1, "src-1", 1, 0, 7, "Source.", "pdf", false)] };
        return new() { RunId = Guid.NewGuid(), PersonelId = personelId, ActorAuditId = "actor",
            AuthorizationGrantId = "grant", ClientRequestId = Guid.NewGuid(), Mode = input.Mode,
            Language = input.Language, RetrievalPolicyVersion = "v1", Status = "Pending",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, RequestJson = "{}",
            RetrievalManifestJson = "{}", AuthorizedInputJson = JsonSerializer.Serialize(input),
            InputFingerprint = new string('b', 64) };
    }

    private static async Task<CanonicalArticleAnalysisRun> AddAnalysisRunAsync(
        AcademicDbContext database, string personelId, int workId)
    {
        ArticleSourceSnapshot source = new()
        {
            CanonicalWorkId = workId,
            ExtractedTextHash = Guid.NewGuid().ToString("N").PadRight(64, 'a'),
            SourceKind = "Pdf", ExtractionVersion = "test-v1", CreatedAt = DateTimeOffset.UtcNow
        };
        SavedArticleSummary summary = new()
        {
            PersonelId = personelId, OriginalAcademicWorkId = Random.Shared.Next(1, int.MaxValue),
            SavedAt = DateTimeOffset.UtcNow,
            SourceHash = Guid.NewGuid().ToString("N").PadRight(64, 'b'), SourceKind = "Pdf",
            ExtractionVersion = "test-v1", SnapshotJson = "{}", ReportJson = "{}"
        };
        CanonicalArticleAnalysisRun run = new()
        {
            CanonicalWorkId = workId, ArticleSourceSnapshot = source, SavedArticleSummary = summary,
            AnalyzedAt = DateTimeOffset.UtcNow, SourceAcquiredAt = DateTimeOffset.UtcNow,
            SourceOrigin = "Test", Language = "tr", PolicyVersion = "article-summary-v5",
            Model = "synthetic", PromptVersion = "test", ExtractionMethod = "test",
            ProcessedChunks = 1, TotalChunks = 1, ProcessedPages = 1,
            TextBearingPages = 1, TotalPages = 1, OmissionReasonsJson = "[]",
            VerificationStatus = "automatically_checked", VerificationModel = "synthetic",
            VerificationPromptVersion = "test"
        };
        database.Add(run);
        await database.SaveChangesAsync();
        return run;
    }

    private sealed class FakeSearch(int workId, int hitCount = 1, int evidenceTextLength = 7,
        string freshnessStatus = AnalysisFreshnessStatus.Unknown)
        : IAcademicEvidenceSearchService
    {
        public Task<AcademicEvidenceSearchResponse> SearchAsync(string personelId,
            AcademicEvidenceSearchRequest request, CancellationToken cancellationToken)
        {
            string exactText = evidenceTextLength == 7 ? "Source." : new string('x', evidenceTextLength);
            return Task.FromResult(new AcademicEvidenceSearchResponse
            {
                QueryHash = new string('a', 64), CorpusHash = new string('b', 64),
                InputHash = new string('c', 64), RequestedCanonicalWorkCount = 1,
                EligibleCanonicalWorkCount = 1, CoveredCanonicalWorkCount = 1,
                CandidateSpanCount = hitCount,
                AnalysisLanguageCoverage = [new() { Value = "en", CanonicalWorkCount = 1 }],
                SourceKindCoverage = [new() { Value = "abstract", CanonicalWorkCount = 1 }],
                Hits = Enumerable.Range(1, hitCount).Select(index => new AcademicEvidenceSearchHitDto
                {
                    EvidenceId = $"work:{workId}:snapshot:1:span:{index}",
                    CanonicalWorkId = workId, ArticleSourceSnapshotId = 1, ArticleSourceSpanId = index,
                    AnalysisRunId = 1, SourceId = $"src-{index}", StartOffset = 0,
                    EndOffset = exactText.Length, ExactText = exactText,
                    AnalysisLanguage = "en", SourceKind = "abstract",
                    ExtractionVersion = "synthetic-v1", ExtractedTextHash = new string('e', 64),
                    IsPartial = true, AnalysisFreshnessStatus = freshnessStatus,
                    AnalysisFreshnessReasons = freshnessStatus == AnalysisFreshnessStatus.Stale
                        ? ["SourceInputChanged"] : ["FreshnessNotRecorded"],
                    SourceCoverage = new AcademicEvidenceSourceCoverageDto
                    {
                        ProcessedChunks = 1, TotalChunks = 2, ScopeReason = "Synthetic partial source."
                    }
                }).ToList()
            });
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request);
    }
}
