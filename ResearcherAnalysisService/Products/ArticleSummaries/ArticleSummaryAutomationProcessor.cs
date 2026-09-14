using System.Data;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Products.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ResearcherAnalysisService.Products.ArticleSummaries;

public sealed class ArticleSummaryAutomationProcessor(
    AnalysisDbContext database,
    IServiceScopeFactory scopes,
    IOptionsMonitor<ArticleSummaryAutomationOptions> options,
    AnalysisSourceLock sourceLock,
    ILogger<ArticleSummaryAutomationProcessor> logger)
{
    private const int PolicyReconciliationBatchSize = 100;

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default)
    {
        ArticleSummaryAutomationOptions settings = options.CurrentValue;
        if (!settings.Enabled || !settings.WorkerEnabled)
            return false;

        await using SqlApplicationLock? workerLock = await SqlApplicationLock.TryAcquireAsync(
            database.Database.GetConnectionString()!, "AcademicCollector.ArticleSummaryWorker", 0,
            cancellationToken);
        if (workerLock is null)
            return false;

        ArticleSummaryAutomationAttempt? attempt = await ClaimAsync(settings, cancellationToken);
        if (attempt is null)
            return false;

        logger.LogInformation(
            "Article summary job {JobId} generation {Generation} started for canonical work {CanonicalWorkId}.",
            attempt.JobId, await GetAttemptGenerationAsync(attempt.JobId, cancellationToken),
            attempt.CanonicalWorkId);
        try
        {
            using IServiceScope executionScope = scopes.CreateScope();
            await executionScope.ServiceProvider.GetRequiredService<ArticleSummaryWorkflow>()
                .SummarizeAutomaticAsync(attempt, cancellationToken);
        }
        catch (ArticleSummaryAutomationAttemptLostException)
        {
            logger.LogWarning("Article summary job {JobId} lost ownership before completion.", attempt.JobId);
        }
        catch (Exception exception)
        {
            Failure failure = Classify(exception, cancellationToken.IsCancellationRequested);
            await FinishFailureAsync(attempt, failure, settings, CancellationToken.None);
            logger.LogWarning(
                "Article summary job {JobId} ended with {OutcomeCode} ({ErrorType}).",
                attempt.JobId, failure.Code, exception.GetType().Name);
        }
        return true;
    }

    private async Task<ArticleSummaryAutomationAttempt?> ClaimAsync(
        ArticleSummaryAutomationOptions settings,
        CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        await sourceLock.AcquireWriteGateAsync(cancellationToken);
        DateTime now = DateTime.UtcNow;

        List<ArticleSummaryAutomationJob> abandoned = await database.ArticleSummaryAutomationJobs
            .Where(job => job.Status == ArticleSummaryAutomationJobStatus.Running)
            .ToListAsync(cancellationToken);
        foreach (ArticleSummaryAutomationJob job in abandoned)
        {
            bool desiredChanged = job.DesiredInputHash != job.RunningInputHash ||
                job.DesiredPolicyVersion != job.RunningPolicyVersion;
            job.ExecutionToken = null;
            job.RunningInputHash = null;
            job.RunningPolicyVersion = null;
            job.UpdatedAt = now;
            if (desiredChanged)
            {
                job.Status = ArticleSummaryAutomationJobStatus.Pending;
                job.Attempts = 0;
                job.NextAttemptAt = now;
                job.CompletedAt = null;
                job.LastOutcomeCode = "InputChanged";
                job.LastOutcomeMessage = "New source metadata remains queued after an interrupted attempt.";
            }
            else
            {
                job.Status = ArticleSummaryAutomationJobStatus.Failed;
                job.CompletedAt = now;
                job.LastOutcomeCode = "Interrupted";
                job.LastOutcomeMessage = "An earlier execution ended while running; remote cost may be unknown and no automatic retry was made.";
            }
        }

        string currentPolicyVersion = settings.PolicyVersion.Trim();
        List<ArticleSummaryAutomationJob> policyChanged = await database.ArticleSummaryAutomationJobs
            .Where(job => job.Status != ArticleSummaryAutomationJobStatus.Running &&
                (job.Status == ArticleSummaryAutomationJobStatus.Pending ||
                 job.Status == ArticleSummaryAutomationJobStatus.RetryWaiting ||
                 job.Status == ArticleSummaryAutomationJobStatus.Succeeded ||
                 job.Status == ArticleSummaryAutomationJobStatus.Failed) &&
                EF.Functions.Collate(job.DesiredPolicyVersion, "Latin1_General_100_BIN2") != currentPolicyVersion)
            .OrderBy(job => job.Id)
            .Take(PolicyReconciliationBatchSize)
            .ToListAsync(cancellationToken);
        foreach (ArticleSummaryAutomationJob job in policyChanged)
        {
            job.DesiredPolicyVersion = currentPolicyVersion;
            job.Status = ArticleSummaryAutomationJobStatus.Pending;
            job.Attempts = 0;
            job.NextAttemptAt = now;
            job.CompletedAt = null;
            job.UpdatedAt = now;
            job.LastOutcomeCode = "PolicyChanged";
            job.LastOutcomeMessage = "The article analysis policy changed and regeneration was queued.";
        }
        if (abandoned.Count > 0 || policyChanged.Count > 0)
            await database.SaveChangesAsync(cancellationToken);

        ArticleSummaryAutomationJob? selected = await database.ArticleSummaryAutomationJobs
            .Where(job => (job.Status == ArticleSummaryAutomationJobStatus.Pending ||
                    job.Status == ArticleSummaryAutomationJobStatus.RetryWaiting) &&
                EF.Functions.Collate(job.DesiredPolicyVersion, "Latin1_General_100_BIN2") == currentPolicyVersion &&
                job.NextAttemptAt <= now)
            .OrderBy(job => job.NextAttemptAt).ThenBy(job => job.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (selected is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        Guid token = Guid.NewGuid();
        selected.Status = ArticleSummaryAutomationJobStatus.Running;
        selected.RunningInputHash = selected.DesiredInputHash;
        selected.RunningPolicyVersion = selected.DesiredPolicyVersion;
        selected.ExecutionToken = token;
        selected.AttemptGeneration++;
        selected.Attempts++;
        selected.StartedAt = now;
        selected.CompletedAt = null;
        selected.UpdatedAt = now;
        selected.LastOutcomeCode = null;
        selected.LastOutcomeMessage = null;
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(selected.Id, selected.CanonicalWorkId, selected.Language,
            selected.RunningInputHash, selected.RunningPolicyVersion, token);
    }

    private async Task FinishFailureAsync(
        ArticleSummaryAutomationAttempt attempt,
        Failure failure,
        ArticleSummaryAutomationOptions settings,
        CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        await sourceLock.AcquireWriteGateAsync(cancellationToken);
        ArticleSummaryAutomationJob? job = await database.ArticleSummaryAutomationJobs
            .SingleOrDefaultAsync(value => value.Id == attempt.JobId, cancellationToken);
        if (job is null || job.Status != ArticleSummaryAutomationJobStatus.Running ||
            job.ExecutionToken != attempt.ExecutionToken)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        DateTime now = DateTime.UtcNow;
        job.UpdatedAt = now;
        job.LastOutcomeCode = failure.Code;
        job.LastOutcomeMessage = failure.Message;
        bool desiredChanged = job.DesiredInputHash != attempt.InputHash ||
            job.DesiredPolicyVersion != attempt.PolicyVersion;
        if (!string.IsNullOrWhiteSpace(failure.CurrentInputHash))
        {
            job.DesiredInputHash = failure.CurrentInputHash;
            desiredChanged = true;
        }
        if (!failure.ConsumesAttempt && job.Attempts > 0)
            job.Attempts--;
        if (desiredChanged)
        {
            job.Status = ArticleSummaryAutomationJobStatus.Pending;
            job.Attempts = 0;
            job.NextAttemptAt = now;
            job.CompletedAt = null;
        }
        else if (failure.Retryable && job.Attempts < settings.MaximumAttempts)
        {
            job.Status = ArticleSummaryAutomationJobStatus.RetryWaiting;
            job.NextAttemptAt = now.AddSeconds(failure.ConsumesAttempt
                ? settings.RetrySeconds * Math.Pow(2, Math.Max(0, job.Attempts - 1))
                : settings.PollSeconds);
            job.CompletedAt = null;
        }
        else
        {
            job.Status = ArticleSummaryAutomationJobStatus.Failed;
            job.CompletedAt = now;
        }
        job.ExecutionToken = null;
        job.RunningInputHash = null;
        job.RunningPolicyVersion = null;
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<int> GetAttemptGenerationAsync(long jobId, CancellationToken cancellationToken) =>
        await database.ArticleSummaryAutomationJobs.AsNoTracking()
            .Where(value => value.Id == jobId)
            .Select(value => value.AttemptGeneration)
            .SingleAsync(cancellationToken);

    private static Failure Classify(Exception exception, bool shutdownRequested)
    {
        if (shutdownRequested || exception is OperationCanceledException)
            return new("Interrupted",
                "Execution was interrupted; remote cost may be unknown and no automatic retry was made.",
                false, true, null);
        if (exception is ArticleSummaryBusyException)
            return new("Busy", "Another summary attempt is still running; retry scheduled.", true, false, null);
        if (exception is ArticleSummaryAutomationInputChangedException changedInput)
            return new("SourceChanged", "The source changed before analysis; the current source will be retried.",
                true, false, changedInput.CurrentInputHash);
        if (exception is JsonException or InvalidAnalysisException)
            return new("InvalidReport",
                "The analysis service returned an invalid report; no automatic retry was made because remote cost may be unknown.",
                false, true, null);
        if (exception is HttpRequestException or AnalysisUnavailableException or AnalysisInputTooLargeException)
            return new("RemoteFailure",
                "The analysis request failed; no automatic retry was made because remote cost may be unknown.",
                false, true, null);
        if (exception is SocketException)
            return new("RemoteFailure",
                "The analysis connection failed; no automatic retry was made because remote cost may be unknown.",
                false, true, null);
        if (exception is ArticleSourceException source)
        {
            bool changed = source.Message.Contains("changed", StringComparison.OrdinalIgnoreCase);
            bool transient = source.Message.Contains("network", StringComparison.OrdinalIgnoreCase) ||
                source.Message.Contains("HTTP status 429", StringComparison.OrdinalIgnoreCase) ||
                source.Message.Contains("HTTP status 5", StringComparison.OrdinalIgnoreCase) ||
                source.Message.Contains("budget expired", StringComparison.OrdinalIgnoreCase);
            return changed
                ? new("SourceChanged", "The source changed during analysis; no automatic retry was made because remote cost may be unknown.", false, true, null)
                : transient
                    ? new("TemporarySourceFailure", "A temporary source failure will be retried.", true, true, null)
                : new("Unavailable", "No supported public article source or abstract is currently available.", false, true, null);
        }
        return new("UnexpectedFailure",
            "Article summary generation failed; no automatic retry was made because remote cost may be unknown.",
            false, true, null);
    }

    private sealed record Failure(
        string Code,
        string Message,
        bool Retryable,
        bool ConsumesAttempt,
        string? CurrentInputHash);
}
