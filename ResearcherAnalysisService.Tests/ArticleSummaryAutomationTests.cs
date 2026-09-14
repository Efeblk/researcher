using AcademicCollector.Analysis.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Configuration;
using ResearcherAnalysisService.Products;
using ResearcherAnalysisService.Products.Api.Contracts;
using ResearcherAnalysisService.Products.ArticleSummaries;
using ResearcherAnalysisService.Products.Data;
using ResearcherAnalysisService.SourceData.Researchers;
using ResearcherAnalysisService.SourceData.Works;
using ResearcherAnalysisService.Tests.Infrastructure;

namespace ResearcherAnalysisService.Tests;

[Collection("Analysis Product SQL Server")]
public sealed class ArticleSummaryAutomationTests(AnalysisProductSqlServerFixture fixture)
{
    [Fact]
    public async Task Scheduler_DefaultEnabled_DeduplicatesIdsAndKeepsStableCanonicalInput()
    {
        MutableOptionsMonitor<ArticleSummaryAutomationOptions> options = Options();
        await using ServiceProvider services = CreateAutomationServices(options);
        string doi = Doi("stable");
        SyntheticCanonicalSource first = await fixture.SeedCanonicalSourceAsync(Person("stable-first"), doi: doi);
        await ScheduleAsync(services, first.CanonicalWorkId, first.CanonicalWorkId);
        ArticleSummaryAutomationJob initial = await LoadJobAsync(services, first.CanonicalWorkId, tracked: true);
        string initialHash = initial.DesiredInputHash;
        initial.Status = ArticleSummaryAutomationJobStatus.Failed;
        initial.Attempts = 3;
        await SaveJobAsync(services, initial);

        _ = await AddOwnerToCanonicalAsync(first.CanonicalWorkId, Person("stable-second"), doi,
            "A synthetic source abstract used only by the isolated test database.");
        await ScheduleAsync(services, first.CanonicalWorkId);

        ArticleSummaryAutomationJob repeated = await LoadJobAsync(services, first.CanonicalWorkId);
        Assert.Equal(initialHash, repeated.DesiredInputHash);
        Assert.Equal(ArticleSummaryAutomationJobStatus.Failed, repeated.Status);
        Assert.Equal(3, repeated.Attempts);
        Assert.Equal(1, await CountJobsAsync(services, first.CanonicalWorkId));
    }

    [Fact]
    public async Task Processor_DefaultQueue_GeneratesEvidenceAndSharedReadDoesNotLeakOwner()
    {
        CountingGenerator generator = new();
        MutableOptionsMonitor<ArticleSummaryAutomationOptions> options = Options();
        await using ServiceProvider services = CreateAutomationServices(options, generator);
        string doi = Doi("shared");
        SyntheticCanonicalSource first = await fixture.SeedCanonicalSourceAsync(Person("shared-first"), doi: doi);
        SyntheticOwner second = await AddOwnerToCanonicalAsync(first.CanonicalWorkId,
            Person("shared-second"), doi,
            "A synthetic source abstract used only by the isolated test database.");
        await ScheduleAsync(services, first.CanonicalWorkId);

        Assert.True(await ProcessTargetAsync(services, first.CanonicalWorkId));
        Assert.Equal(1, generator.Calls);
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        Assert.Equal(1, await database.CanonicalArticleAnalysisRuns.CountAsync(
            value => value.CanonicalWorkId == first.CanonicalWorkId));
        Assert.True(await database.CanonicalArticleClaims.AnyAsync(value =>
            value.CanonicalArticleAnalysisRun!.CanonicalWorkId == first.CanonicalWorkId));

        SavedArticleSummaryResponse? shared = await scope.ServiceProvider
            .GetRequiredService<ArticleSummaryWorkflow>()
            .GetLatestAsync(second.PersonelId, second.AcademicWorkId, "tr", CancellationToken.None);
        Assert.NotNull(shared);
        Assert.Equal(second.PersonelId, shared.PersonelID);
        Assert.Equal(second.AcademicWorkId, shared.OriginalAcademicWorkId);
        Assert.Null(shared.SourceUrl);
    }

    [Fact]
    public async Task AutomationOff_SkipsQueueWhileManualSummaryStillWorks()
    {
        CountingGenerator generator = new();
        MutableOptionsMonitor<ArticleSummaryAutomationOptions> options = Options(enabled: false);
        await using ServiceProvider services = CreateAutomationServices(options, generator);
        SyntheticCanonicalSource source = await fixture.SeedCanonicalSourceAsync(Person("off"));

        await ScheduleAsync(services, source.CanonicalWorkId);
        Assert.Equal(0, await CountJobsAsync(services, source.CanonicalWorkId));
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        SavedArticleSummaryResponse? saved = await scope.ServiceProvider
            .GetRequiredService<ArticleSummaryWorkflow>()
            .SummarizeAsync(source.PersonelId, source.AcademicWorkId, "tr", CancellationToken.None);
        Assert.NotNull(saved);
        Assert.Equal(1, generator.Calls);
        Assert.Equal(0, await CountJobsAsync(services, source.CanonicalWorkId));
    }

    [Theory]
    [InlineData("unavailable", "RemoteFailure")]
    [InlineData("http", "RemoteFailure")]
    [InlineData("invalid", "InvalidReport")]
    [InlineData("json", "InvalidReport")]
    public async Task Processor_LocalTypedFailure_FailsTerminalWithoutBlindRetry(
        string failureKind, string expectedCode)
    {
        Exception failure = failureKind switch
        {
            "unavailable" => new AnalysisUnavailableException("Synthetic unavailable provider."),
            "http" => new HttpRequestException("Synthetic provider transport failure."),
            "invalid" => new InvalidAnalysisException(AnalysisFailure.InvalidEvidence),
            _ => new System.Text.Json.JsonException("Synthetic malformed provider output.")
        };
        CountingGenerator generator = new((_, _) =>
            Task.FromException<GeneratedArticleChunk>(failure));
        MutableOptionsMonitor<ArticleSummaryAutomationOptions> options = Options();
        await using ServiceProvider services = CreateAutomationServices(options, generator);
        SyntheticCanonicalSource source = await SeedQueuedAsync(services, failureKind);

        Assert.True(await ProcessTargetAsync(services, source.CanonicalWorkId));
        Assert.False(await ProcessTargetAsync(services, source.CanonicalWorkId));
        ArticleSummaryAutomationJob job = await LoadJobAsync(services, source.CanonicalWorkId);
        Assert.Equal(ArticleSummaryAutomationJobStatus.Failed, job.Status);
        Assert.Equal(expectedCode, job.LastOutcomeCode);
        Assert.Equal(1, job.Attempts);
        Assert.Equal(1, generator.Calls);
    }

    [Theory]
    [InlineData("too-large", "InvalidReport", 2)]
    [InlineData("invalid-output-limit", "InvalidReport", 2)]
    public async Task Processor_LocalBudgetFailure_UsesBoundedFallbackAndStillFailsTerminal(
        string failureKind, string expectedCode, int expectedCalls)
    {
        Exception failure = failureKind == "too-large"
            ? new AnalysisInputTooLargeException()
            : new InvalidAnalysisException(AnalysisFailure.OutputLimit);
        CountingGenerator generator = new((_, _) =>
            Task.FromException<GeneratedArticleChunk>(failure));
        MutableOptionsMonitor<ArticleSummaryAutomationOptions> options = Options();
        await using ServiceProvider services = CreateAutomationServices(options, generator);
        SyntheticCanonicalSource source = await SeedQueuedAsync(services, failureKind);

        Assert.True(await ProcessTargetAsync(services, source.CanonicalWorkId));
        Assert.False(await ProcessTargetAsync(services, source.CanonicalWorkId));
        ArticleSummaryAutomationJob job = await LoadJobAsync(services, source.CanonicalWorkId);
        Assert.Equal(ArticleSummaryAutomationJobStatus.Failed, job.Status);
        Assert.Equal(expectedCode, job.LastOutcomeCode);
        Assert.Equal(1, job.Attempts);
        Assert.Equal(expectedCalls, generator.Calls);
    }

    [Fact]
    public async Task Processor_PostAnalysisSourceChangeWithUnchangedDesiredInput_FailsTerminalAfterOneCall()
    {
        ServiceProvider? services = null;
        int workId = 0;
        CountingGenerator generator = new(async (spans, cancellationToken) =>
        {
            await ChangeAbstractAsync(workId, "Changed after the in-process analysis call.");
            return CountingGenerator.Success(spans);
        });
        MutableOptionsMonitor<ArticleSummaryAutomationOptions> options = Options();
        await using (services = CreateAutomationServices(options, generator))
        {
            SyntheticCanonicalSource source = await SeedQueuedAsync(services, "post-change");
            workId = source.AcademicWorkId;
            Assert.True(await ProcessTargetAsync(services, source.CanonicalWorkId));
            Assert.False(await ProcessTargetAsync(services, source.CanonicalWorkId));

            ArticleSummaryAutomationJob job = await LoadJobAsync(services, source.CanonicalWorkId);
            Assert.Equal(ArticleSummaryAutomationJobStatus.Failed, job.Status);
            Assert.Equal("SourceChanged", job.LastOutcomeCode);
            Assert.Contains("no automatic retry", job.LastOutcomeMessage, StringComparison.Ordinal);
            Assert.Equal(1, job.Attempts);
            Assert.Equal(1, generator.Calls);
            Assert.Equal(0, await CountRunsAsync(services, source.CanonicalWorkId));
        }
    }

    [Fact]
    public async Task Processor_PostAnalysisSourceChangeWithSynchronizedDesiredInput_QueuesNewGeneration()
    {
        ServiceProvider? services = null;
        int workId = 0;
        int canonicalWorkId = 0;
        CountingGenerator generator = new(async (spans, cancellationToken) =>
        {
            await ChangeAbstractAsync(workId, "Changed and synchronized after the in-process analysis call.");
            await ScheduleAsync(services!, canonicalWorkId);
            return CountingGenerator.Success(spans);
        });
        MutableOptionsMonitor<ArticleSummaryAutomationOptions> options = Options();
        await using (services = CreateAutomationServices(options, generator))
        {
            SyntheticCanonicalSource source = await SeedQueuedAsync(services, "post-change-synced");
            workId = source.AcademicWorkId;
            canonicalWorkId = source.CanonicalWorkId;
            string originalDesired = (await LoadJobAsync(services, canonicalWorkId)).DesiredInputHash;

            Assert.True(await ProcessTargetAsync(services, canonicalWorkId));
            ArticleSummaryAutomationJob job = await LoadJobAsync(services, canonicalWorkId);
            Assert.Equal(ArticleSummaryAutomationJobStatus.Pending, job.Status);
            Assert.Equal("SourceChanged", job.LastOutcomeCode);
            Assert.NotEqual(originalDesired, job.DesiredInputHash);
            Assert.Equal(0, job.Attempts);
            Assert.Null(job.CompletedAt);
            Assert.Equal(1, generator.Calls);
            Assert.Equal(0, await CountRunsAsync(services, canonicalWorkId));
            await RemoveJobAsync(services, job.Id);
        }
    }

    [Fact]
    public async Task Processor_AbandonedSameInput_FailsTerminalWithoutModelCall()
    {
        CountingGenerator generator = new();
        MutableOptionsMonitor<ArticleSummaryAutomationOptions> options = Options();
        await using ServiceProvider services = CreateAutomationServices(options, generator);
        SyntheticCanonicalSource source = await SeedQueuedAsync(services, "abandoned-same");
        ArticleSummaryAutomationJob job = await LoadJobAsync(services, source.CanonicalWorkId, tracked: true);
        job.Status = ArticleSummaryAutomationJobStatus.Running;
        job.ExecutionToken = Guid.NewGuid();
        job.RunningInputHash = job.DesiredInputHash;
        job.RunningPolicyVersion = job.DesiredPolicyVersion;
        job.Attempts = 1;
        await SaveJobAsync(services, job);

        Assert.False(await ProcessTargetAsync(services, source.CanonicalWorkId));
        ArticleSummaryAutomationJob recovered = await LoadJobAsync(services, source.CanonicalWorkId);
        Assert.Equal(ArticleSummaryAutomationJobStatus.Failed, recovered.Status);
        Assert.Equal("Interrupted", recovered.LastOutcomeCode);
        Assert.NotNull(recovered.CompletedAt);
        Assert.Null(recovered.ExecutionToken);
        Assert.Null(recovered.RunningInputHash);
        Assert.Null(recovered.RunningPolicyVersion);
        Assert.Equal(0, generator.Calls);
    }

    [Fact]
    public async Task Processor_AbandonedChangedInput_TransitionsToPendingNewGeneration()
    {
        CountingGenerator generator = new();
        MutableOptionsMonitor<ArticleSummaryAutomationOptions> options = Options();
        await using ServiceProvider services = CreateAutomationServices(options, generator);
        SyntheticCanonicalSource target = await SeedQueuedAsync(services, "abandoned-changed");
        _ = await SeedQueuedAsync(services, "abandoned-changed-other");
        ArticleSummaryAutomationJob job = await LoadJobAsync(services, target.CanonicalWorkId, tracked: true);
        job.Status = ArticleSummaryAutomationJobStatus.Running;
        job.ExecutionToken = Guid.NewGuid();
        job.RunningInputHash = job.DesiredInputHash;
        job.RunningPolicyVersion = job.DesiredPolicyVersion;
        job.DesiredInputHash = new string('f', 64);
        job.Attempts = 2;
        await SaveJobAsync(services, job);

        await using (AsyncServiceScope scope = services.CreateAsyncScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<ArticleSummaryAutomationProcessor>()
                .ProcessNextAsync());

        ArticleSummaryAutomationJob recovered = await LoadJobAsync(services, target.CanonicalWorkId);
        Assert.Equal(ArticleSummaryAutomationJobStatus.Pending, recovered.Status);
        Assert.Equal("InputChanged", recovered.LastOutcomeCode);
        Assert.Equal(0, recovered.Attempts);
        Assert.Null(recovered.CompletedAt);
        Assert.Null(recovered.ExecutionToken);
        Assert.Null(recovered.RunningInputHash);
        Assert.Null(recovered.RunningPolicyVersion);
        Assert.Equal(1, generator.Calls);
        await RemoveJobAsync(services, recovered.Id);
    }

    [Fact]
    public async Task Processor_UpstreamCancellation_FailsTerminalWithoutRetry()
    {
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CountingGenerator generator = new(async (_, cancellationToken) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        });
        MutableOptionsMonitor<ArticleSummaryAutomationOptions> options = Options();
        await using ServiceProvider services = CreateAutomationServices(options, generator);
        SyntheticCanonicalSource source = await SeedQueuedAsync(services, "cancelled");
        using CancellationTokenSource cancellation = new();

        Task<bool> processing = ProcessTargetAsync(services, source.CanonicalWorkId, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        Assert.True(await processing);

        ArticleSummaryAutomationJob job = await LoadJobAsync(services, source.CanonicalWorkId);
        Assert.Equal(ArticleSummaryAutomationJobStatus.Failed, job.Status);
        Assert.Equal("Interrupted", job.LastOutcomeCode);
        Assert.NotNull(job.CompletedAt);
        Assert.Null(job.ExecutionToken);
        Assert.Equal(1, generator.Calls);
        Assert.False(await ProcessTargetAsync(services, source.CanonicalWorkId));
    }

    [Fact]
    public async Task AutomaticAttempt_StaleExecutionToken_CannotSaveOrComplete()
    {
        CountingGenerator generator = new();
        MutableOptionsMonitor<ArticleSummaryAutomationOptions> options = Options();
        await using ServiceProvider services = CreateAutomationServices(options, generator);
        SyntheticCanonicalSource source = await SeedQueuedAsync(services, "stale-token");
        ArticleSummaryAutomationJob job = await LoadJobAsync(services, source.CanonicalWorkId, tracked: true);
        job.Status = ArticleSummaryAutomationJobStatus.Running;
        job.RunningInputHash = job.DesiredInputHash;
        job.RunningPolicyVersion = job.DesiredPolicyVersion;
        job.ExecutionToken = Guid.NewGuid();
        job.Attempts = 1;
        await SaveJobAsync(services, job);
        ArticleSummaryAutomationAttempt stale = new(job.Id, source.CanonicalWorkId, job.Language,
            job.RunningInputHash, job.RunningPolicyVersion, Guid.NewGuid());

        await using (AsyncServiceScope scope = services.CreateAsyncScope())
        {
            await Assert.ThrowsAsync<ArticleSummaryAutomationAttemptLostException>(() =>
                scope.ServiceProvider.GetRequiredService<ArticleSummaryWorkflow>()
                    .SummarizeAutomaticAsync(stale, CancellationToken.None));
        }
        ArticleSummaryAutomationJob unchanged = await LoadJobAsync(services, source.CanonicalWorkId);
        Assert.Equal(ArticleSummaryAutomationJobStatus.Running, unchanged.Status);
        Assert.Null(unchanged.LastSuccessfulAnalysisRunId);
        Assert.Equal(0, await CountRunsAsync(services, source.CanonicalWorkId));
        Assert.Equal(1, generator.Calls);
        await RemoveJobAsync(services, job.Id);
    }

    [Fact]
    public async Task ManualSuccess_QueuedAutomaticAttempt_ReusesWithoutSecondModelCall()
    {
        CountingGenerator generator = new();
        MutableOptionsMonitor<ArticleSummaryAutomationOptions> options = Options();
        await using ServiceProvider services = CreateAutomationServices(options, generator);
        SyntheticCanonicalSource source = await SeedQueuedAsync(services, "manual-reuse");
        await using (AsyncServiceScope scope = services.CreateAsyncScope())
        {
            SavedArticleSummaryResponse? saved = await scope.ServiceProvider
                .GetRequiredService<ArticleSummaryWorkflow>()
                .SummarizeAsync(source.PersonelId, source.AcademicWorkId, "tr", CancellationToken.None);
            Assert.NotNull(saved);
        }
        ArticleSummaryAutomationJob job = await LoadJobAsync(services, source.CanonicalWorkId, tracked: true);
        Assert.NotNull(job.LastSuccessfulAnalysisRunId);
        Assert.Equal(job.DesiredInputHash, job.ProcessedInputHash);
        Assert.Equal(job.DesiredPolicyVersion, job.ProcessedPolicyVersion);
        job.Status = ArticleSummaryAutomationJobStatus.Pending;
        job.NextAttemptAt = DateTime.UnixEpoch;
        job.CompletedAt = null;
        await SaveJobAsync(services, job);

        Assert.True(await ProcessTargetAsync(services, source.CanonicalWorkId));
        ArticleSummaryAutomationJob completed = await LoadJobAsync(services, source.CanonicalWorkId);
        Assert.Equal(ArticleSummaryAutomationJobStatus.Succeeded, completed.Status);
        Assert.Equal("Reused", completed.LastOutcomeCode);
        Assert.Equal(1, await CountRunsAsync(services, source.CanonicalWorkId));
        Assert.Equal(1, generator.Calls);
    }

    [Fact]
    public async Task AutomaticAttempt_OldSummaryPolicyRunIsNotReusedByCurrentPolicy()
    {
        CountingGenerator generator = new();
        MutableOptionsMonitor<ArticleSummaryAutomationOptions> options = Options();
        await using ServiceProvider services = CreateAutomationServices(options, generator);
        SyntheticCanonicalSource source = await SeedQueuedAsync(services, "old-policy-run");
        Assert.True(await ProcessTargetAsync(services, source.CanonicalWorkId));
        await using (AsyncServiceScope scope = services.CreateAsyncScope())
        {
            AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
            CanonicalArticleAnalysisRun oldRun = await database.CanonicalArticleAnalysisRuns
                .SingleAsync(value => value.CanonicalWorkId == source.CanonicalWorkId);
            oldRun.PolicyVersion = "article-summary-v2";
            ArticleSummaryAutomationJob job = await database.ArticleSummaryAutomationJobs
                .SingleAsync(value => value.CanonicalWorkId == source.CanonicalWorkId);
            job.Status = ArticleSummaryAutomationJobStatus.Pending;
            job.NextAttemptAt = DateTime.UnixEpoch;
            job.CompletedAt = null;
            await database.SaveChangesAsync();
        }

        Assert.True(await ProcessTargetAsync(services, source.CanonicalWorkId));
        Assert.Equal(2, await CountRunsAsync(services, source.CanonicalWorkId));
        Assert.Equal(2, generator.Calls);
    }

    [Fact]
    public async Task PendingOldPolicy_IsReconciledBeforeDispatchUsingOrdinalPolicyIdentity()
    {
        CountingGenerator generator = new();
        MutableOptionsMonitor<ArticleSummaryAutomationOptions> options = Options();
        await using ServiceProvider services = CreateAutomationServices(options, generator);
        SyntheticCanonicalSource source = await SeedQueuedAsync(services, "pending-old-policy");
        ArticleSummaryAutomationJob job = await LoadJobAsync(services, source.CanonicalWorkId, tracked: true);
        job.DesiredPolicyVersion = "ARTICLE-SUMMARY-V3";
        job.Attempts = 2;
        await SaveJobAsync(services, job);

        Assert.True(await ProcessTargetAsync(services, source.CanonicalWorkId));
        ArticleSummaryAutomationJob completed = await LoadJobAsync(services, source.CanonicalWorkId);
        Assert.Equal("article-summary-v5", completed.DesiredPolicyVersion);
        Assert.Equal("article-summary-v5", completed.ProcessedPolicyVersion);
        Assert.Equal(ArticleSummaryAutomationJobStatus.Succeeded, completed.Status);
        Assert.Equal(1, completed.Attempts);
        Assert.Equal(1, generator.Calls);
    }

    [Fact]
    public async Task AutomaticAttempt_ManualLockBusy_DefersWithoutConsumingAttempt()
    {
        CountingGenerator generator = new();
        MutableOptionsMonitor<ArticleSummaryAutomationOptions> options = Options();
        await using ServiceProvider services = CreateAutomationServices(options, generator);
        SyntheticCanonicalSource source = await SeedQueuedAsync(services, "busy");
        await using SqlApplicationLock? held = await SqlApplicationLock.TryAcquireAsync(
            fixture.ConnectionString,
            $"AcademicCollector.ArticleSummary.{source.CanonicalWorkId}.tr", 0,
            CancellationToken.None);
        Assert.NotNull(held);

        Assert.True(await ProcessTargetAsync(services, source.CanonicalWorkId));
        ArticleSummaryAutomationJob job = await LoadJobAsync(services, source.CanonicalWorkId);
        Assert.Equal(ArticleSummaryAutomationJobStatus.RetryWaiting, job.Status);
        Assert.Equal(0, job.Attempts);
        Assert.Equal("Busy", job.LastOutcomeCode);
        Assert.Equal(0, generator.Calls);
        await RemoveJobAsync(services, job.Id);
    }

    [Fact]
    public async Task RunningJob_NewSourceInput_PreservesAttemptFenceAndResetsNewGenerationAttempts()
    {
        CountingGenerator generator = new();
        MutableOptionsMonitor<ArticleSummaryAutomationOptions> options = Options(workerEnabled: false);
        await using ServiceProvider services = CreateAutomationServices(options, generator);
        SyntheticCanonicalSource source = await SeedQueuedAsync(services, "running-change");
        Guid token = Guid.NewGuid();
        ArticleSummaryAutomationJob job = await LoadJobAsync(services, source.CanonicalWorkId, tracked: true);
        string oldHash = job.DesiredInputHash;
        job.Status = ArticleSummaryAutomationJobStatus.Running;
        job.RunningInputHash = oldHash;
        job.RunningPolicyVersion = job.DesiredPolicyVersion;
        job.ExecutionToken = token;
        job.Attempts = 2;
        await SaveJobAsync(services, job);
        await ChangeAbstractAsync(source.AcademicWorkId, "Changed source input with new evidence.");
        await ScheduleAsync(services, source.CanonicalWorkId);

        ArticleSummaryAutomationJob changed = await LoadJobAsync(services, source.CanonicalWorkId);
        Assert.Equal(ArticleSummaryAutomationJobStatus.Running, changed.Status);
        Assert.NotEqual(oldHash, changed.DesiredInputHash);
        Assert.Equal(oldHash, changed.RunningInputHash);
        Assert.Equal(token, changed.ExecutionToken);
        Assert.Equal(0, changed.Attempts);
        await RemoveJobAsync(services, job.Id);
    }

    [Fact]
    public async Task EnabledOff_RetainsPendingJob_AndReenableProcessesIt()
    {
        CountingGenerator generator = new();
        MutableOptionsMonitor<ArticleSummaryAutomationOptions> options = Options();
        await using ServiceProvider services = CreateAutomationServices(options, generator);
        SyntheticCanonicalSource source = await SeedQueuedAsync(services, "toggle");
        options.Set(WithEnabled(options.CurrentValue, false));

        Assert.False(await ProcessTargetAsync(services, source.CanonicalWorkId));
        Assert.Equal(ArticleSummaryAutomationJobStatus.Pending,
            (await LoadJobAsync(services, source.CanonicalWorkId)).Status);

        options.Set(WithEnabled(options.CurrentValue, true));
        Assert.True(await ProcessTargetAsync(services, source.CanonicalWorkId));
        Assert.Equal(ArticleSummaryAutomationJobStatus.Succeeded,
            (await LoadJobAsync(services, source.CanonicalWorkId)).Status);
        Assert.Equal(1, generator.Calls);
    }

    private ServiceProvider CreateAutomationServices(
        MutableOptionsMonitor<ArticleSummaryAutomationOptions> automationOptions,
        CountingGenerator? generator = null)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:UsageDatabase"] = fixture.ConnectionString,
                ["CollectionChanges:WorkerEnabled"] = "false",
                ["FacultyAssistant:WorkerEnabled"] = "false",
                ["PublicationMetrics:WorkerEnabled"] = "false",
                ["ArticleEvaluation:WorkerEnabled"] = "false",
                ["ArticleSummary:OcrEnabled"] = "false",
                ["ArticleSummary:MaximumSourceRequests"] = "2",
                ["ArticleSummary:TotalTimeoutSeconds"] = "60"
            }).Build();
        generator ??= new CountingGenerator();
        ServiceCollection services = new();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton(configuration);
        services.AddAcademicProducts(configuration);
        services.AddSingleton<IOptionsMonitor<ArticleSummaryAutomationOptions>>(automationOptions);
        services.AddSingleton<IOptions<AiOptions>>(Microsoft.Extensions.Options.Options.Create(new AiOptions()));
        services.AddSingleton<IArticleSummaryGenerator>(generator);
        services.AddSingleton<IArticleClaimVerifier, SupportingVerifier>();
        services.AddScoped<ArticleSummarizer>();
        return services.BuildServiceProvider();
    }

    private async Task<SyntheticCanonicalSource> SeedQueuedAsync(ServiceProvider services, string prefix)
    {
        SyntheticCanonicalSource source = await fixture.SeedCanonicalSourceAsync(Person(prefix));
        await ScheduleAsync(services, source.CanonicalWorkId);
        ArticleSummaryAutomationJob job = await LoadJobAsync(services, source.CanonicalWorkId, tracked: true);
        job.NextAttemptAt = DateTime.UnixEpoch;
        await SaveJobAsync(services, job);
        return source;
    }

    private static async Task ScheduleAsync(ServiceProvider services, params int[] canonicalWorkIds)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ArticleSummaryAutomationScheduler>()
            .ScheduleAsync(canonicalWorkIds);
    }

    private async Task<SyntheticOwner> AddOwnerToCanonicalAsync(
        int canonicalWorkId, string personelId, string doi, string abstractText)
    {
        Researcher researcher = new() { PersonelId = personelId, FirstName = "Automation" };
        AcademicWork work = new()
        {
            PersonelId = personelId,
            Provider = AcademicWorkProvider.OpenAlex,
            ProviderWorkId = "https://openalex.org/W" + Guid.NewGuid().ToString("N"),
            Title = "Synthetic source article",
            Doi = doi,
            PublicationYear = 2025,
            Abstract = abstractText,
            SyncedAt = DateTime.UtcNow,
            CanonicalObservation = new CanonicalWorkObservation
            {
                CanonicalWorkId = canonicalWorkId,
                PersonelId = personelId,
                Provider = AcademicWorkProvider.OpenAlex,
                DoiObserved = doi,
                TitleObserved = "Synthetic source article",
                ObservedAt = DateTime.UtcNow
            }
        };
        CanonicalResearcherWork association = new()
        {
            CanonicalWorkId = canonicalWorkId,
            PersonelId = personelId,
            LastObservedAt = DateTime.UtcNow
        };
        await using DbContext database = fixture.CreateSeedContext();
        database.AddRange(researcher, work, association);
        await database.SaveChangesAsync();
        return new(personelId, work.Id);
    }

    private async Task ChangeAbstractAsync(int academicWorkId, string value)
    {
        await using DbContext database = fixture.CreateSeedContext();
        AcademicWork work = await database.Set<AcademicWork>().SingleAsync(item => item.Id == academicWorkId);
        work.Abstract = value;
        work.SyncedAt = DateTime.UtcNow;
        await database.SaveChangesAsync();
    }

    private static async Task<ArticleSummaryAutomationJob> LoadJobAsync(
        ServiceProvider services, int canonicalWorkId, bool tracked = false)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IQueryable<ArticleSummaryAutomationJob> query = scope.ServiceProvider
            .GetRequiredService<AnalysisDbContext>().ArticleSummaryAutomationJobs;
        if (!tracked)
            query = query.AsNoTracking();
        return await query.SingleAsync(value => value.CanonicalWorkId == canonicalWorkId);
    }

    private static async Task SaveJobAsync(ServiceProvider services, ArticleSummaryAutomationJob job)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        database.Update(job);
        await database.SaveChangesAsync();
    }

    private static async Task<int> CountJobsAsync(ServiceProvider services, int canonicalWorkId)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AnalysisDbContext>()
            .ArticleSummaryAutomationJobs.CountAsync(value => value.CanonicalWorkId == canonicalWorkId);
    }

    private static async Task<int> CountRunsAsync(ServiceProvider services, int canonicalWorkId)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AnalysisDbContext>()
            .CanonicalArticleAnalysisRuns.CountAsync(value => value.CanonicalWorkId == canonicalWorkId);
    }

    private static async Task RemoveJobAsync(ServiceProvider services, long jobId)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AnalysisDbContext>()
            .ArticleSummaryAutomationJobs.Where(value => value.Id == jobId).ExecuteDeleteAsync();
    }

    private static async Task<bool> ProcessTargetAsync(
        ServiceProvider services, int canonicalWorkId, CancellationToken cancellationToken = default)
    {
        List<(long Id, DateTime NextAttemptAt)> deferred;
        await using (AsyncServiceScope scope = services.CreateAsyncScope())
        {
            AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
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
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ArticleSummaryAutomationProcessor>()
                .ProcessNextAsync(cancellationToken);
        }
        finally
        {
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
            long[] ids = deferred.Select(value => value.Id).ToArray();
            Dictionary<long, DateTime> times = deferred.ToDictionary(value => value.Id, value => value.NextAttemptAt);
            List<ArticleSummaryAutomationJob> others = await database.ArticleSummaryAutomationJobs
                .Where(value => ids.Contains(value.Id)).ToListAsync();
            foreach (ArticleSummaryAutomationJob other in others)
                other.NextAttemptAt = times[other.Id];
            await database.SaveChangesAsync();
        }
    }

    private static MutableOptionsMonitor<ArticleSummaryAutomationOptions> Options(
        bool enabled = true, bool workerEnabled = true) => new(new()
        {
            Enabled = enabled,
            WorkerEnabled = workerEnabled,
            Language = "tr",
            PollSeconds = 1,
            RetrySeconds = 1,
            MaximumAttempts = 3,
            PolicyVersion = "article-summary-v5"
        });

    private static ArticleSummaryAutomationOptions WithEnabled(
        ArticleSummaryAutomationOptions value, bool enabled) => new()
    {
        Enabled = enabled,
        WorkerEnabled = value.WorkerEnabled,
        Language = value.Language,
        PollSeconds = value.PollSeconds,
        RetrySeconds = value.RetrySeconds,
        MaximumAttempts = value.MaximumAttempts,
        PolicyVersion = value.PolicyVersion
    };

    private static string Person(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N");
    private static string Doi(string prefix) => "10.9876/" + prefix + Guid.NewGuid().ToString("N");

    private sealed record SyntheticOwner(string PersonelId, int AcademicWorkId);

    private sealed class MutableOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; private set; } = value;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
        public void Set(T next) => CurrentValue = next;
    }

    private sealed class CountingGenerator(
        Func<IReadOnlyList<ArticleSourceSpan>, CancellationToken, Task<GeneratedArticleChunk>>? generate = null)
        : IArticleSummaryGenerator
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);

        public async Task<GeneratedArticleChunk> GenerateAsync(
            string language, string sourceKind, IReadOnlyList<ArticleSourceSpan> sourceSpans,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return generate is null
                ? Success(sourceSpans)
                : await generate(sourceSpans, cancellationToken);
        }

        public static GeneratedArticleChunk Success(IReadOnlyList<ArticleSourceSpan> spans)
        {
            ArticleSourceSpan span = spans[0];
            GeneratedArticleClaim claim = new("claim-1", "Synthetic supported finding.", [span.SourceId]);
            return new(new([], [], [], [claim], []), "synthetic-generator", "synthetic-v1");
        }
    }

    private sealed class SupportingVerifier : IArticleClaimVerifier
    {
        public Task<GeneratedVerificationBatch> VerifyAsync(
            string language, IReadOnlyList<GeneratedArticleClaim> claims,
            IReadOnlyList<ArticleSourceSpan> sourceSpans, CancellationToken cancellationToken) =>
            Task.FromResult(new GeneratedVerificationBatch(
                claims.Select(claim => new GeneratedClaimVerdict(
                    claim.ClaimId, "supported", "Synthetic exact source match.")).ToList(),
                "synthetic-verifier", "synthetic-verify-v1"));
    }
}
