using System.Net;
using System.Net.Http.Json;
using AcademicCollector.Analysis.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance;
using AcademicCollectorDemo.Modules.AcademicPerformance.Analysis;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;
using AcademicCollectorDemo.Tests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class ArticleSummaryAutomationTests(SqlServerFixture fixture)
{
    private const string Api = "/Services/AcademicPerformance/V1/";

    [Fact]
    public async Task CanonicalSync_DefaultEnabled_DeduplicatesSameDoiAndStableInput()
    {
        await using ServiceProvider services = CreateAutomationServices(workerEnabled: false);
        using IServiceScope scope = services.CreateScope();
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        string doi = "10.8800/" + Guid.NewGuid().ToString("N");
        Researcher firstOwner = Researcher("queue-first");
        Researcher secondOwner = Researcher("queue-second");
        AcademicWork first = Work(firstOwner.PersonelId, "first", "Stable abstract", doi);
        first.Sources.AddRange([
            new() { Kind = "Landing", Origin = "A", Url = "https://example.test/article" },
            new() { Kind = "Pdf", Origin = "B", Url = "https://example.test/article.pdf" }
        ]);
        AcademicWork second = Work(secondOwner.PersonelId, "second", "Stable abstract", doi);
        second.Sources.AddRange([
            new() { Kind = "Pdf", Origin = "B", Url = "https://example.test/article.pdf" },
            new() { Kind = "Landing", Origin = "A", Url = "https://example.test/article" }
        ]);
        database.Researchers.AddRange(firstOwner, secondOwner);
        database.AcademicWorks.AddRange(first, second);
        await database.SaveChangesAsync();

        CanonicalWorkSynchronizer synchronizer = scope.ServiceProvider
            .GetRequiredService<CanonicalWorkSynchronizer>();
        await synchronizer.SyncAsync(firstOwner.PersonelId, scheduleArticleSummaries: true);
        ArticleSummaryAutomationJob initial = await database.ArticleSummaryAutomationJobs.SingleAsync(
            value => value.Language == "tr" && value.CanonicalWork!.NormalizedDoi == doi);
        string initialHash = initial.DesiredInputHash;
        initial.Status = ArticleSummaryAutomationJobStatus.Failed;
        initial.Attempts = 3;
        await database.SaveChangesAsync();

        await synchronizer.SyncAsync(secondOwner.PersonelId, scheduleArticleSummaries: true);
        database.ChangeTracker.Clear();
        ArticleSummaryAutomationJob repeated = await database.ArticleSummaryAutomationJobs.SingleAsync(
            value => value.Language == "tr" && value.CanonicalWork!.NormalizedDoi == doi);
        Assert.Equal(initialHash, repeated.DesiredInputHash);
        Assert.Equal(ArticleSummaryAutomationJobStatus.Failed, repeated.Status);
        Assert.Equal(3, repeated.Attempts);
    }

    [Fact]
    public async Task Worker_DefaultQueue_GeneratesEvidenceAndSharedReadDoesNotLeakOwner()
    {
        await using ServiceProvider services = CreateAutomationServices(workerEnabled: false);
        (Researcher owner, AcademicWork work, int canonicalWorkId) = await SeedQueuedAsync(services, "worker");
        int calls = 0;
        await using WebApplication analysis = await StartAnalysisAsync(_ =>
        {
            Interlocked.Increment(ref calls);
            return HttpStatusCode.OK;
        });
        using var host = new HostProcess(fixture.ConnectionString, analysis.Urls.Single(),
            disablePublicationEnrichmentProviders: true,
            articleSummaryAutomationEnabled: true,
            articleSummaryAutomationWorkerEnabled: true,
            articleSummaryAutomationPollSeconds: 1,
            articleSummaryAutomationRetrySeconds: 1);
        host.Client.Timeout = TimeSpan.FromSeconds(30);
        await host.WaitUntilReadyAsync();
        await WaitForStatusAsync(canonicalWorkId, ArticleSummaryAutomationJobStatus.Succeeded);

        using IServiceScope readScope = fixture.Services.CreateScope();
        AcademicDbContext database = readScope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        Assert.Equal(1, calls);
        Assert.Equal(1, await database.CanonicalArticleAnalysisRuns.CountAsync(
            value => value.CanonicalWorkId == canonicalWorkId));

        Researcher other = Researcher("worker-shared");
        AcademicWork duplicate = Work(other.PersonelId, "other", "Synthetic article evidence", work.Doi);
        database.Researchers.Add(other);
        database.AcademicWorks.Add(duplicate);
        await database.SaveChangesAsync();
        database.CanonicalWorkObservations.Add(new()
        {
            CanonicalWorkId = canonicalWorkId,
            AcademicWorkId = duplicate.Id,
            PersonelId = other.PersonelId,
            Provider = duplicate.Provider,
            ProviderWorkId = duplicate.ProviderWorkId,
            CategoryObserved = duplicate.Category,
            ObservedAt = duplicate.SyncedAt
        });
        database.CanonicalResearcherWorks.Add(new()
        {
            CanonicalWorkId = canonicalWorkId,
            PersonelId = other.PersonelId,
            LastObservedAt = duplicate.SyncedAt
        });
        await database.SaveChangesAsync();
        using HttpResponseMessage response = await host.Client.PostAsJsonAsync(Api + "GetArticleSummary",
            new { PersonelID = other.PersonelId, AcademicWorkId = duplicate.Id, Language = "tr" });
        response.EnsureSuccessStatusCode();
        SavedArticleSummaryResponse shared =
            (await response.Content.ReadFromJsonAsync<SavedArticleSummaryResponse>())!;
        Assert.Equal(other.PersonelId, shared.PersonelID);
        Assert.Equal(duplicate.Id, shared.OriginalAcademicWorkId);
        Assert.Null(shared.SourceUrl);

        using HttpResponseMessage status = await host.Client.PostAsJsonAsync(
            Api + "GetArticleSummaryAutomationStatus",
            new { PersonelID = other.PersonelId, CanonicalWorkId = canonicalWorkId, Language = "tr" });
        status.EnsureSuccessStatusCode();
        ArticleSummaryAutomationStatusResponse body =
            (await status.Content.ReadFromJsonAsync<ArticleSummaryAutomationStatusResponse>())!;
        Assert.True(body.AutomationEnabled);
        Assert.Equal(ArticleSummaryAutomationJobStatus.Succeeded, body.Status);
        Assert.NotNull(body.LastSuccess);
    }

    [Fact]
    public async Task AutomationOff_SkipsQueueWhileManualSummaryStillWorks()
    {
        Researcher owner = Researcher("off");
        AcademicWork work = Work(owner.PersonelId, "off-work", "Manual summary evidence", null);
        using (IServiceScope scope = fixture.Services.CreateScope())
        {
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            database.Researchers.Add(owner);
            database.AcademicWorks.Add(work);
            await database.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<CanonicalWorkSynchronizer>()
                .SyncAsync(owner.PersonelId, scheduleArticleSummaries: true);
            Assert.False(await database.ArticleSummaryAutomationJobs.AnyAsync(value =>
                value.CanonicalWork!.Observations.Any(observation => observation.AcademicWorkId == work.Id)));
        }

        int calls = 0;
        await using WebApplication analysis = await StartAnalysisAsync(_ =>
        {
            Interlocked.Increment(ref calls);
            return HttpStatusCode.OK;
        });
        using var host = new HostProcess(fixture.ConnectionString, analysis.Urls.Single(),
            disablePublicationEnrichmentProviders: true);
        host.Client.Timeout = TimeSpan.FromSeconds(30);
        await host.WaitUntilReadyAsync();
        using HttpResponseMessage response = await host.Client.PostAsJsonAsync(Api + "SummarizeArticle",
            new { PersonelID = owner.PersonelId, AcademicWorkId = work.Id, Language = "tr" });
        response.EnsureSuccessStatusCode();
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Worker_RemoteHttpFailure_FailsTerminalWithoutRetry(HttpStatusCode status)
    {
        int calls = 0;
        await using WebApplication analysis = await StartAnalysisAsync(_ =>
        {
            Interlocked.Increment(ref calls);
            return status;
        });
        await using ServiceProvider services = CreateAutomationServices(
            workerEnabled: true, analysisBaseUrl: analysis.Urls.Single());
        (_, _, int canonicalWorkId) = await SeedQueuedAsync(services, "remote-status");

        Assert.True(await ProcessTargetAsync(services, canonicalWorkId));
        Assert.False(await ProcessTargetAsync(services, canonicalWorkId));
        using IServiceScope scope = services.CreateScope();
        ArticleSummaryAutomationJob job = await scope.ServiceProvider.GetRequiredService<AcademicDbContext>()
            .ArticleSummaryAutomationJobs.AsNoTracking()
            .SingleAsync(value => value.CanonicalWorkId == canonicalWorkId);
        Assert.Equal(ArticleSummaryAutomationJobStatus.Failed, job.Status);
        Assert.Equal("RemoteFailure", job.LastOutcomeCode);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Worker_TransportFailureOrTimeout_FailsTerminalWithoutRetry(bool timeout)
    {
        CountingFailureHandler handler = new(timeout);
        await using ServiceProvider services = CreateAutomationServices(
            workerEnabled: true, analysisHandler: handler);
        (_, _, int canonicalWorkId) = await SeedQueuedAsync(services, timeout ? "timeout" : "transport");

        Assert.True(await ProcessTargetAsync(services, canonicalWorkId));
        Assert.False(await ProcessTargetAsync(services, canonicalWorkId));
        using IServiceScope scope = services.CreateScope();
        ArticleSummaryAutomationJob job = await scope.ServiceProvider.GetRequiredService<AcademicDbContext>()
            .ArticleSummaryAutomationJobs.AsNoTracking()
            .SingleAsync(value => value.CanonicalWorkId == canonicalWorkId);
        Assert.Equal(ArticleSummaryAutomationJobStatus.Failed, job.Status);
        Assert.Equal(timeout ? "Interrupted" : "RemoteFailure", job.LastOutcomeCode);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Worker_PostAnalysisSourceChangeWithUnchangedDesiredInput_FailsTerminalAfterOneCall()
    {
        ServiceProvider? automationServices = null;
        int workId = 0;
        int calls = 0;
        await using WebApplication analysis = await StartAnalysisAsync(_ =>
        {
            Interlocked.Increment(ref calls);
            using IServiceScope scope = automationServices!.CreateScope();
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            AcademicWork work = database.AcademicWorks.Single(value => value.Id == workId);
            work.Abstract = "Changed after the paid analysis request.";
            database.SaveChanges();
            return HttpStatusCode.OK;
        });
        await using ServiceProvider services = automationServices = CreateAutomationServices(
            workerEnabled: true, analysisBaseUrl: analysis.Urls.Single());
        (_, AcademicWork work, int canonicalWorkId) = await SeedQueuedAsync(services, "post-analysis-change");
        workId = work.Id;

        Assert.True(await ProcessTargetAsync(services, canonicalWorkId));
        Assert.False(await ProcessTargetAsync(services, canonicalWorkId));
        using IServiceScope readScope = services.CreateScope();
        AcademicDbContext readDatabase = readScope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        ArticleSummaryAutomationJob job = await readDatabase.ArticleSummaryAutomationJobs.AsNoTracking()
            .SingleAsync(value => value.CanonicalWorkId == canonicalWorkId);
        Assert.Equal(ArticleSummaryAutomationJobStatus.Failed, job.Status);
        Assert.Equal("SourceChanged", job.LastOutcomeCode);
        Assert.Contains("no automatic retry", job.LastOutcomeMessage, StringComparison.Ordinal);
        Assert.Equal(1, job.Attempts);
        Assert.Equal(1, calls);
        Assert.False(await readDatabase.CanonicalArticleAnalysisRuns.AnyAsync(
            value => value.CanonicalWorkId == canonicalWorkId));
    }

    [Fact]
    public async Task Worker_PostAnalysisSourceChangeWithSynchronizedDesiredInput_QueuesNewGeneration()
    {
        ServiceProvider? automationServices = null;
        int workId = 0;
        int calls = 0;
        await using WebApplication analysis = await StartAnalysisAsync(_ =>
        {
            Interlocked.Increment(ref calls);
            using IServiceScope scope = automationServices!.CreateScope();
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            AcademicWork work = database.AcademicWorks.Single(value => value.Id == workId);
            work.Abstract = "Changed and synchronized after the paid analysis request.";
            database.SaveChanges();
            scope.ServiceProvider.GetRequiredService<CanonicalWorkSynchronizer>()
                .SyncAsync(work.PersonelId, scheduleArticleSummaries: true).GetAwaiter().GetResult();
            return HttpStatusCode.OK;
        });
        await using ServiceProvider services = automationServices = CreateAutomationServices(
            workerEnabled: true, analysisBaseUrl: analysis.Urls.Single());
        (_, AcademicWork work, int canonicalWorkId) = await SeedQueuedAsync(services, "post-analysis-synced");
        workId = work.Id;
        string originalDesired;
        using (IServiceScope scope = services.CreateScope())
        {
            originalDesired = await scope.ServiceProvider.GetRequiredService<AcademicDbContext>()
                .ArticleSummaryAutomationJobs.Where(value => value.CanonicalWorkId == canonicalWorkId)
                .Select(value => value.DesiredInputHash).SingleAsync();
        }

        Assert.True(await ProcessTargetAsync(services, canonicalWorkId));
        using IServiceScope readScope = services.CreateScope();
        AcademicDbContext readDatabase = readScope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        ArticleSummaryAutomationJob job = await readDatabase.ArticleSummaryAutomationJobs.AsNoTracking()
            .SingleAsync(value => value.CanonicalWorkId == canonicalWorkId);
        Assert.Equal(ArticleSummaryAutomationJobStatus.Pending, job.Status);
        Assert.Equal("SourceChanged", job.LastOutcomeCode);
        Assert.NotEqual(originalDesired, job.DesiredInputHash);
        Assert.Equal(0, job.Attempts);
        Assert.Null(job.CompletedAt);
        Assert.Equal(1, calls);
        Assert.False(await readDatabase.CanonicalArticleAnalysisRuns.AnyAsync(
            value => value.CanonicalWorkId == canonicalWorkId));
        await RemoveJobForCanonicalAsync(canonicalWorkId);
    }

    [Fact]
    public async Task Worker_AbandonedSameInput_FailsTerminalWithoutRemoteCall()
    {
        await using ServiceProvider services = CreateAutomationServices(workerEnabled: true);
        (_, _, int canonicalWorkId) = await SeedQueuedAsync(services, "abandoned");
        using (IServiceScope scope = services.CreateScope())
        {
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            ArticleSummaryAutomationJob job = await database.ArticleSummaryAutomationJobs
                .SingleAsync(value => value.CanonicalWorkId == canonicalWorkId);
            job.Status = ArticleSummaryAutomationJobStatus.Running;
            job.ExecutionToken = Guid.NewGuid();
            job.RunningInputHash = job.DesiredInputHash;
            job.RunningPolicyVersion = job.DesiredPolicyVersion;
            job.Attempts = 1;
            await database.SaveChangesAsync();
        }

        Assert.False(await ProcessTargetAsync(services, canonicalWorkId));
        using IServiceScope readScope = services.CreateScope();
        AcademicDbContext readDatabase = readScope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        ArticleSummaryAutomationJob recovered = await readDatabase.ArticleSummaryAutomationJobs.AsNoTracking()
            .SingleAsync(value => value.CanonicalWorkId == canonicalWorkId);
        Assert.Equal(ArticleSummaryAutomationJobStatus.Failed, recovered.Status);
        Assert.Equal("Interrupted", recovered.LastOutcomeCode);
        Assert.Contains("no automatic retry", recovered.LastOutcomeMessage, StringComparison.Ordinal);
        Assert.NotNull(recovered.CompletedAt);
        Assert.Null(recovered.ExecutionToken);
        Assert.Null(recovered.RunningInputHash);
        Assert.Null(recovered.RunningPolicyVersion);
        Assert.False(await readDatabase.CanonicalArticleAnalysisRuns.AnyAsync(
            value => value.CanonicalWorkId == canonicalWorkId));
    }

    [Fact]
    public async Task Worker_AbandonedChangedInput_TransitionsToPendingNewGeneration()
    {
        int calls = 0;
        await using WebApplication analysis = await StartAnalysisAsync(_ =>
        {
            Interlocked.Increment(ref calls);
            return HttpStatusCode.OK;
        });
        await using ServiceProvider services = CreateAutomationServices(
            workerEnabled: true, analysisBaseUrl: analysis.Urls.Single());
        (_, _, int canonicalWorkId) = await SeedQueuedAsync(services, "abandoned-changed");
        _ = await SeedQueuedAsync(services, "abandoned-changed-other");
        using (IServiceScope scope = services.CreateScope())
        {
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            ArticleSummaryAutomationJob job = await database.ArticleSummaryAutomationJobs
                .SingleAsync(value => value.CanonicalWorkId == canonicalWorkId);
            job.Status = ArticleSummaryAutomationJobStatus.Running;
            job.ExecutionToken = Guid.NewGuid();
            job.RunningInputHash = job.DesiredInputHash;
            job.RunningPolicyVersion = job.DesiredPolicyVersion;
            job.DesiredInputHash = new string('f', 64);
            job.Attempts = 2;
            await database.SaveChangesAsync();
        }

        using (IServiceScope scope = services.CreateScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<ArticleSummaryAutomationProcessor>()
                .ProcessNextAsync());

        using IServiceScope readScope = services.CreateScope();
        ArticleSummaryAutomationJob recovered = await readScope.ServiceProvider
            .GetRequiredService<AcademicDbContext>().ArticleSummaryAutomationJobs.AsNoTracking()
            .SingleAsync(value => value.CanonicalWorkId == canonicalWorkId);
        Assert.Equal(ArticleSummaryAutomationJobStatus.Pending, recovered.Status);
        Assert.Equal("InputChanged", recovered.LastOutcomeCode);
        Assert.Equal(0, recovered.Attempts);
        Assert.Null(recovered.CompletedAt);
        Assert.Null(recovered.ExecutionToken);
        Assert.Null(recovered.RunningInputHash);
        Assert.Null(recovered.RunningPolicyVersion);
        Assert.Equal(1, calls);
        await RemoveJobForCanonicalAsync(canonicalWorkId);
    }

    [Fact]
    public async Task Worker_UpstreamCancellation_FailsTerminalWithoutRetry()
    {
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using WebApplication analysis = await StartBlockingAnalysisAsync(entered);
        await using ServiceProvider services = CreateAutomationServices(
            workerEnabled: true, analysisBaseUrl: analysis.Urls.Single());
        (_, _, int canonicalWorkId) = await SeedQueuedAsync(services, "cancelled");
        using CancellationTokenSource cancellation = new();

        Task<bool> processing;
        using (IServiceScope scope = services.CreateScope())
        {
            processing = scope.ServiceProvider.GetRequiredService<ArticleSummaryAutomationProcessor>()
                .ProcessNextAsync(cancellation.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            Assert.True(await processing);
        }

        using IServiceScope readScope = services.CreateScope();
        AcademicDbContext database = readScope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        ArticleSummaryAutomationJob job = await database.ArticleSummaryAutomationJobs.AsNoTracking()
            .SingleAsync(value => value.CanonicalWorkId == canonicalWorkId);
        Assert.Equal(ArticleSummaryAutomationJobStatus.Failed, job.Status);
        Assert.Equal("Interrupted", job.LastOutcomeCode);
        Assert.Contains("no automatic retry", job.LastOutcomeMessage, StringComparison.Ordinal);
        Assert.NotNull(job.CompletedAt);
        Assert.Null(job.ExecutionToken);

        Assert.False(await ProcessTargetAsync(services, canonicalWorkId));
    }

    [Fact]
    public async Task AutomaticAttempt_StaleExecutionToken_CannotSaveOrComplete()
    {
        int calls = 0;
        await using WebApplication analysis = await StartAnalysisAsync(_ =>
        {
            Interlocked.Increment(ref calls);
            return HttpStatusCode.OK;
        });
        await using ServiceProvider services = CreateAutomationServices(
            workerEnabled: true, analysisBaseUrl: analysis.Urls.Single());
        (_, _, int canonicalWorkId) = await SeedQueuedAsync(services, "stale-token");
        ArticleSummaryAutomationAttempt stale;
        long jobId;
        using (IServiceScope scope = services.CreateScope())
        {
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            ArticleSummaryAutomationJob job = await database.ArticleSummaryAutomationJobs
                .SingleAsync(value => value.CanonicalWorkId == canonicalWorkId);
            jobId = job.Id;
            job.Status = ArticleSummaryAutomationJobStatus.Running;
            job.RunningInputHash = job.DesiredInputHash;
            job.RunningPolicyVersion = job.DesiredPolicyVersion;
            job.ExecutionToken = Guid.NewGuid();
            job.Attempts = 1;
            await database.SaveChangesAsync();
            stale = new(job.Id, canonicalWorkId, job.Language, job.RunningInputHash,
                job.RunningPolicyVersion, Guid.NewGuid());
        }

        using (IServiceScope scope = services.CreateScope())
        {
            await Assert.ThrowsAsync<ArticleSummaryAutomationAttemptLostException>(() =>
                scope.ServiceProvider.GetRequiredService<ArticleSummaryWorkflow>()
                    .SummarizeAutomaticAsync(stale, CancellationToken.None));
        }
        using (IServiceScope scope = services.CreateScope())
        {
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            ArticleSummaryAutomationJob job = await database.ArticleSummaryAutomationJobs
                .AsNoTracking().SingleAsync(value => value.Id == jobId);
            Assert.Equal(ArticleSummaryAutomationJobStatus.Running, job.Status);
            Assert.Null(job.LastSuccessfulAnalysisRunId);
            Assert.False(await database.CanonicalArticleAnalysisRuns.AnyAsync(
                value => value.CanonicalWorkId == canonicalWorkId));
        }
        Assert.Equal(1, calls);
        await RemoveJobAsync(jobId);
    }

    [Fact]
    public async Task ManualSuccess_QueuedAutomaticAttempt_ReusesWithoutSecondModelCall()
    {
        int calls = 0;
        await using WebApplication analysis = await StartAnalysisAsync(_ =>
        {
            Interlocked.Increment(ref calls);
            return HttpStatusCode.OK;
        });
        await using ServiceProvider services = CreateAutomationServices(
            workerEnabled: true, analysisBaseUrl: analysis.Urls.Single());
        (Researcher owner, AcademicWork work, int canonicalWorkId) =
            await SeedQueuedAsync(services, "manual-reuse");
        using (IServiceScope scope = services.CreateScope())
        {
            SavedArticleSummaryResponse? saved = await scope.ServiceProvider
                .GetRequiredService<ArticleSummaryWorkflow>()
                .SummarizeAsync(owner.PersonelId, work.Id, "tr", CancellationToken.None);
            Assert.NotNull(saved);
        }
        using (IServiceScope scope = services.CreateScope())
        {
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            ArticleSummaryAutomationJob job = await database.ArticleSummaryAutomationJobs
                .SingleAsync(value => value.CanonicalWorkId == canonicalWorkId);
            Assert.NotNull(job.LastSuccessfulAnalysisRunId);
            Assert.Equal(job.DesiredInputHash, job.ProcessedInputHash);
            Assert.Equal(job.DesiredPolicyVersion, job.ProcessedPolicyVersion);
            job.Status = ArticleSummaryAutomationJobStatus.Pending;
            job.NextAttemptAt = DateTime.UtcNow;
            job.CompletedAt = null;
            await database.SaveChangesAsync();
        }
        Assert.True(await ProcessTargetAsync(services, canonicalWorkId));
        using (IServiceScope scope = services.CreateScope())
        {
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            ArticleSummaryAutomationJob job = await database.ArticleSummaryAutomationJobs
                .AsNoTracking().SingleAsync(value => value.CanonicalWorkId == canonicalWorkId);
            Assert.Equal(ArticleSummaryAutomationJobStatus.Succeeded, job.Status);
            Assert.Equal("Reused", job.LastOutcomeCode);
            Assert.Equal(1, await database.CanonicalArticleAnalysisRuns.CountAsync(
                value => value.CanonicalWorkId == canonicalWorkId));
        }
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task AutomaticAttempt_OldSummaryPolicyRunIsNotReusedByCurrentPolicy()
    {
        int calls = 0;
        await using WebApplication analysis = await StartAnalysisAsync(_ =>
        {
            Interlocked.Increment(ref calls);
            return HttpStatusCode.OK;
        });
        await using ServiceProvider services = CreateAutomationServices(
            workerEnabled: true, analysisBaseUrl: analysis.Urls.Single());
        (_, _, int canonicalWorkId) = await SeedQueuedAsync(services, "policy-version");
        Assert.True(await ProcessTargetAsync(services, canonicalWorkId));
        using (IServiceScope scope = services.CreateScope())
        {
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            CanonicalArticleAnalysisRun oldRun = await database.CanonicalArticleAnalysisRuns
                .SingleAsync(value => value.CanonicalWorkId == canonicalWorkId);
            oldRun.PolicyVersion = "article-summary-v2";
            ArticleSummaryAutomationJob job = await database.ArticleSummaryAutomationJobs
                .SingleAsync(value => value.CanonicalWorkId == canonicalWorkId);
            job.Status = ArticleSummaryAutomationJobStatus.Pending;
            job.NextAttemptAt = DateTime.UtcNow;
            job.CompletedAt = null;
            await database.SaveChangesAsync();
        }

        Assert.True(await ProcessTargetAsync(services, canonicalWorkId));

        using (IServiceScope scope = services.CreateScope())
        {
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            Assert.Equal(2, await database.CanonicalArticleAnalysisRuns.CountAsync(
                value => value.CanonicalWorkId == canonicalWorkId));
            Assert.Contains(await database.CanonicalArticleAnalysisRuns
                .Where(value => value.CanonicalWorkId == canonicalWorkId)
                .Select(value => value.PolicyVersion).ToListAsync(),
                value => value == "article-summary-v5");
        }
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task PendingOldPolicy_IsReconciledBeforeDispatchUsingOrdinalPolicyIdentity()
    {
        int calls = 0;
        await using WebApplication analysis = await StartAnalysisAsync(_ =>
        {
            Interlocked.Increment(ref calls);
            return HttpStatusCode.OK;
        });
        await using ServiceProvider services = CreateAutomationServices(
            workerEnabled: true, analysisBaseUrl: analysis.Urls.Single());
        (_, _, int canonicalWorkId) = await SeedQueuedAsync(services, "pending-old-policy");
        using (IServiceScope scope = services.CreateScope())
        {
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            ArticleSummaryAutomationJob job = await database.ArticleSummaryAutomationJobs
                .SingleAsync(value => value.CanonicalWorkId == canonicalWorkId);
            job.DesiredPolicyVersion = "ARTICLE-SUMMARY-V3";
            job.Attempts = 2;
            await database.SaveChangesAsync();
        }

        Assert.True(await ProcessTargetAsync(services, canonicalWorkId));

        using (IServiceScope scope = services.CreateScope())
        {
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            ArticleSummaryAutomationJob job = await database.ArticleSummaryAutomationJobs.AsNoTracking()
                .SingleAsync(value => value.CanonicalWorkId == canonicalWorkId);
            Assert.Equal("article-summary-v5", job.DesiredPolicyVersion);
            Assert.Equal("article-summary-v5", job.ProcessedPolicyVersion);
            Assert.Equal(ArticleSummaryAutomationJobStatus.Succeeded, job.Status);
            Assert.Equal(1, job.Attempts);
        }
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task AutomaticAttempt_ManualLockBusy_DefersWithoutConsumingAttempt()
    {
        await using ServiceProvider services = CreateAutomationServices(workerEnabled: true);
        (_, _, int canonicalWorkId) = await SeedQueuedAsync(services, "busy");
        await using SqlApplicationLock? held = await SqlApplicationLock.TryAcquireAsync(
            fixture.ConnectionString, $"AcademicCollector.ArticleSummary.{canonicalWorkId}.tr", 0,
            CancellationToken.None);
        Assert.NotNull(held);
        Assert.True(await ProcessTargetAsync(services, canonicalWorkId));
        using (IServiceScope scope = services.CreateScope())
        {
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            ArticleSummaryAutomationJob job = await database.ArticleSummaryAutomationJobs
                .AsNoTracking().SingleAsync(value => value.CanonicalWorkId == canonicalWorkId);
            Assert.Equal(ArticleSummaryAutomationJobStatus.RetryWaiting, job.Status);
            Assert.Equal(0, job.Attempts);
            Assert.Equal("Busy", job.LastOutcomeCode);
        }
        await RemoveJobForCanonicalAsync(canonicalWorkId);
    }

    [Fact]
    public async Task RunningJob_NewSourceInput_PreservesAttemptFenceAndResetsNewGenerationAttempts()
    {
        await using ServiceProvider services = CreateAutomationServices(workerEnabled: false);
        (_, AcademicWork work, int canonicalWorkId) = await SeedQueuedAsync(services, "running-change");
        Guid token = Guid.NewGuid();
        string oldHash;
        using (IServiceScope scope = services.CreateScope())
        {
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            ArticleSummaryAutomationJob job = await database.ArticleSummaryAutomationJobs
                .SingleAsync(value => value.CanonicalWorkId == canonicalWorkId);
            oldHash = job.DesiredInputHash;
            job.Status = ArticleSummaryAutomationJobStatus.Running;
            job.RunningInputHash = oldHash;
            job.RunningPolicyVersion = job.DesiredPolicyVersion;
            job.ExecutionToken = token;
            job.Attempts = 2;
            AcademicWork current = await database.AcademicWorks.SingleAsync(value => value.Id == work.Id);
            current.Abstract = "Changed source input with new evidence.";
            await database.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<CanonicalWorkSynchronizer>()
                .SyncAsync(current.PersonelId, scheduleArticleSummaries: true);
        }
        using (IServiceScope scope = services.CreateScope())
        {
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            ArticleSummaryAutomationJob job = await database.ArticleSummaryAutomationJobs.AsNoTracking()
                .SingleAsync(value => value.CanonicalWorkId == canonicalWorkId);
            Assert.Equal(ArticleSummaryAutomationJobStatus.Running, job.Status);
            Assert.NotEqual(oldHash, job.DesiredInputHash);
            Assert.Equal(oldHash, job.RunningInputHash);
            Assert.Equal(token, job.ExecutionToken);
            Assert.Equal(0, job.Attempts);
        }
        await RemoveJobForCanonicalAsync(canonicalWorkId);
    }

    [Fact]
    public async Task EnabledOff_RetainsPendingJob_AndReenableProcessesIt()
    {
        await using WebApplication analysis = await StartAnalysisAsync(_ => HttpStatusCode.OK);
        await using ServiceProvider services = CreateAutomationServices(
            workerEnabled: true, analysisBaseUrl: analysis.Urls.Single());
        (_, _, int canonicalWorkId) = await SeedQueuedAsync(services, "toggle");
        IConfiguration configuration = services.GetRequiredService<IConfiguration>();
        configuration["ArticleSummaryAutomation:Enabled"] = "false";
        ((IConfigurationRoot)configuration).Reload();
        Assert.False(await ProcessTargetAsync(services, canonicalWorkId));
        using (IServiceScope scope = services.CreateScope())
        {
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            Assert.Equal(ArticleSummaryAutomationJobStatus.Pending,
                await database.ArticleSummaryAutomationJobs.AsNoTracking()
                    .Where(value => value.CanonicalWorkId == canonicalWorkId)
                    .Select(value => value.Status).SingleAsync());
        }

        configuration["ArticleSummaryAutomation:Enabled"] = "true";
        ((IConfigurationRoot)configuration).Reload();
        Assert.True(await ProcessTargetAsync(services, canonicalWorkId));
        using (IServiceScope scope = services.CreateScope())
        {
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            Assert.Equal(ArticleSummaryAutomationJobStatus.Succeeded,
                await database.ArticleSummaryAutomationJobs.AsNoTracking()
                    .Where(value => value.CanonicalWorkId == canonicalWorkId)
                    .Select(value => value.Status).SingleAsync());
        }
    }

    private ServiceProvider CreateAutomationServices(
        bool workerEnabled,
        string? analysisBaseUrl = null,
        HttpMessageHandler? analysisHandler = null)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:AcademicDatabase"] = fixture.ConnectionString,
                ["BulkCollection:WorkerEnabled"] = "false",
                ["ArticleSummaryAutomation:WorkerEnabled"] = workerEnabled.ToString(),
                ["AnalysisService:BaseUrl"] = analysisBaseUrl ?? "http://127.0.0.1:1/",
                ["ProviderRequestLimits:OpenAlex:Enabled"] = "false",
                ["ProviderRequestLimits:Crossref:Enabled"] = "false",
                ["ProviderRequestLimits:SemanticScholar:Enabled"] = "false"
            }).Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddAcademicPerformanceModule(configuration);
        if (analysisHandler is not null)
        {
            services.AddSingleton(new ArticleSummaryServiceClient(new HttpClient(analysisHandler)
            {
                BaseAddress = new Uri("http://127.0.0.1/")
            }, Microsoft.Extensions.Options.Options.Create(new AnalysisServiceOptions())));
        }
        return services.BuildServiceProvider();
    }

    private async Task<(Researcher Owner, AcademicWork Work, int CanonicalWorkId)> SeedQueuedAsync(
        ServiceProvider services, string prefix)
    {
        using IServiceScope scope = services.CreateScope();
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        Researcher owner = Researcher(prefix);
        AcademicWork work = Work(owner.PersonelId, Guid.NewGuid().ToString("N"),
            "Synthetic article evidence", null);
        database.Researchers.Add(owner);
        database.AcademicWorks.Add(work);
        await database.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<CanonicalWorkSynchronizer>()
            .SyncAsync(owner.PersonelId, scheduleArticleSummaries: true);
        int canonicalWorkId = await database.CanonicalWorkObservations
            .Where(value => value.AcademicWorkId == work.Id)
            .Select(value => value.CanonicalWorkId).SingleAsync();
        ArticleSummaryAutomationJob job = await database.ArticleSummaryAutomationJobs
            .SingleAsync(value => value.CanonicalWorkId == canonicalWorkId);
        job.NextAttemptAt = DateTime.UnixEpoch;
        await database.SaveChangesAsync();
        return (owner, work, canonicalWorkId);
    }

    private async Task WaitForStatusAsync(int canonicalWorkId, string expected)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(25);
        while (DateTime.UtcNow < deadline)
        {
            using IServiceScope scope = fixture.Services.CreateScope();
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            string? status = await database.ArticleSummaryAutomationJobs.AsNoTracking()
                .Where(value => value.CanonicalWorkId == canonicalWorkId)
                .Select(value => value.Status).SingleOrDefaultAsync();
            if (status == expected)
                return;
            await Task.Delay(200);
        }
        throw new TimeoutException($"Automatic summary job did not reach {expected}.");
    }

    private async Task RemoveJobAsync(long jobId)
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        await database.ArticleSummaryAutomationJobs.Where(value => value.Id == jobId).ExecuteDeleteAsync();
    }

    private async Task RemoveJobForCanonicalAsync(int canonicalWorkId)
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        await database.ArticleSummaryAutomationJobs
            .Where(value => value.CanonicalWorkId == canonicalWorkId).ExecuteDeleteAsync();
    }

    private async Task<bool> ProcessTargetAsync(ServiceProvider services, int canonicalWorkId)
    {
        List<(long Id, DateTime NextAttemptAt)> deferred;
        using (IServiceScope scope = services.CreateScope())
        {
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            List<ArticleSummaryAutomationJob> others = await database.ArticleSummaryAutomationJobs
                .Where(value => value.CanonicalWorkId != canonicalWorkId &&
                    (value.Status == ArticleSummaryAutomationJobStatus.Pending ||
                     value.Status == ArticleSummaryAutomationJobStatus.RetryWaiting))
                .ToListAsync();
            deferred = others.Select(value => (value.Id, value.NextAttemptAt)).ToList();
            foreach (ArticleSummaryAutomationJob other in others)
                other.NextAttemptAt = DateTime.UtcNow.AddHours(1);
            await database.SaveChangesAsync();
        }
        try
        {
            using IServiceScope scope = services.CreateScope();
            return await scope.ServiceProvider
                .GetRequiredService<ArticleSummaryAutomationProcessor>()
                .ProcessNextAsync();
        }
        finally
        {
            using IServiceScope scope = services.CreateScope();
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            long[] ids = deferred.Select(value => value.Id).ToArray();
            Dictionary<long, DateTime> times = deferred.ToDictionary(value => value.Id, value => value.NextAttemptAt);
            List<ArticleSummaryAutomationJob> others = await database.ArticleSummaryAutomationJobs
                .Where(value => ids.Contains(value.Id)).ToListAsync();
            foreach (ArticleSummaryAutomationJob other in others)
                other.NextAttemptAt = times[other.Id];
            await database.SaveChangesAsync();
        }
    }

    private static Researcher Researcher(string prefix) => new()
    {
        PersonelId = prefix + "-" + Guid.NewGuid().ToString("N"),
        FirstName = "Automation"
    };

    private static AcademicWork Work(
        string personelId, string providerWorkId, string abstractText, string? doi) => new()
    {
        PersonelId = personelId,
        Provider = AcademicWorkProvider.OpenAlex,
        ProviderWorkId = providerWorkId,
        Doi = doi,
        Abstract = abstractText,
        SyncedAt = DateTime.UtcNow
    };

    private static async Task<WebApplication> StartAnalysisAsync(
        Func<SummarizeArticleRequest, HttpStatusCode> status)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        WebApplication application = builder.Build();
        application.MapPost("/api/v1/articles/summarize", (SummarizeArticleRequest request) =>
        {
            HttpStatusCode responseStatus = status(request);
            if (responseStatus != HttpStatusCode.OK)
                return Results.StatusCode((int)responseStatus);
            ArticleSourceSpan span = request.SourceSpans!.First();
            ArticleClaim claim = new("Supported finding", [new(span.Text, span.PageNumber)
            {
                SourceId = span.SourceId,
                StartOffset = span.StartOffset,
                EndOffset = span.EndOffset
            }]);
            ArticleCoverage coverage = new(1, 1, 1, 1, 1, 0, true, request.ScopeReason)
            {
                CandidateClaims = 1,
                AutomaticallyCheckedClaims = 1,
                SupportedClaims = 1,
                UnsupportedClaims = 0,
                UncertainClaims = 0,
                DuplicateOrCappedClaims = 0,
                BudgetUnverifiedClaims = 0,
                OmissionReasons = []
            };
            return Results.Json(new ArticleSummaryReport(request.Language, request.SourceKind,
                request.SourceHash, request.ExtractionVersion, coverage,
                new([], [], [], [claim], []), "synthetic", "v1")
            {
                Verification = new("automatically_checked", "synthetic", "verify-v1", true, null),
                SourceFidelity = ArticleSourceFidelity.Create(request.SourceKind, request.ExtractionVersion)
            });
        });
        await application.StartAsync();
        return application;
    }

    private static async Task<WebApplication> StartBlockingAnalysisAsync(TaskCompletionSource entered)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        WebApplication application = builder.Build();
        application.MapPost("/api/v1/articles/summarize", async (CancellationToken cancellationToken) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Results.NoContent();
        });
        await application.StartAsync();
        return application;
    }

    private sealed class CountingFailureHandler(bool timeout) : HttpMessageHandler
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return timeout
                ? Task.FromException<HttpResponseMessage>(new OperationCanceledException())
                : Task.FromException<HttpResponseMessage>(new HttpRequestException("Synthetic transport failure."));
        }
    }
}
