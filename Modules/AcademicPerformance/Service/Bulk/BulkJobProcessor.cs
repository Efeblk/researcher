using System.Text.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Application;
using AcademicCollectorDemo.Modules.AcademicPerformance.Bulk.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Bulk;

public sealed class BulkJobProcessor(
    AcademicDbContext database, IServiceScopeFactory scopes, IOptions<BulkCollectionOptions> options,
    ILogger<BulkJobProcessor> logger)
{
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default)
    {
        // Serialize bulk researchers, including across hosts, to avoid competing entity merges.
        // The session stays open throughout collection; a crashed process loses ownership.
        await using SqlApplicationLock? gate = await SqlApplicationLock.TryAcquireAsync(
            database.Database.GetConnectionString()!, "AcademicCollector.BulkWorker", 0, cancellationToken);
        if (gate is null)
            return false;
        DateTime now = DateTime.UtcNow;
        // Running rows are abandoned only after acquiring the exclusive worker lock.
        await database.BulkCollectionJobs.Where(job => job.Status == BulkJobStatus.Running &&
                job.Attempts >= options.Value.MaximumAttempts)
            .ExecuteUpdateAsync(update => update.SetProperty(job => job.Status, BulkJobStatus.Failed)
                .SetProperty(job => job.CompletedAt, now)
                .SetProperty(job => job.ResultMessage, "Worker interrupted; retry limit reached."), cancellationToken);
        await database.BulkCollectionJobs.Where(job => job.Status == BulkJobStatus.Running &&
                job.Attempts < options.Value.MaximumAttempts)
            .ExecuteUpdateAsync(update => update.SetProperty(job => job.Status, BulkJobStatus.RetryWaiting)
                .SetProperty(job => job.NextAttemptAt, now), cancellationToken);
        BulkCollectionJob? job = await database.BulkCollectionJobs
            .Where(job => (job.Status == BulkJobStatus.Pending || job.Status == BulkJobStatus.RetryWaiting)
                && job.NextAttemptAt <= now)
            .OrderBy(job => job.NextAttemptAt).ThenBy(job => job.Id).FirstOrDefaultAsync(cancellationToken);
        if (job is null)
            return false;
        job.Status = BulkJobStatus.Running;
        job.StartedAt = now;
        job.CompletedAt = null;
        job.ResultMessage = null;
        job.Attempts++;
        await database.SaveChangesAsync(cancellationToken);
        DateTime startedAt = DateTime.UtcNow;
        logger.LogInformation("Bulk job {JobId} attempt {Attempt} started.", job.Id, job.Attempts);

        using ProviderCallScope providerCalls = new(cancellationToken);
        bool saved = false;
        bool retryable = false;
        try
        {
            BulkResearcherInput input = BulkCollectionService.ReadPersisted(job.InputJson).Input;
            // A separate EF scope keeps a failed collection transaction out of queue bookkeeping.
            using IServiceScope collectionScope = scopes.CreateScope();
            var service = collectionScope.ServiceProvider.GetRequiredService<IAcademicPerformanceApplicationService>();
            AcademicDataResponse response = await service.CollectAsync(new()
            {
                PersonelId = input.PersonelId,
                Orcid = input.Orcid,
                GoogleScholarId = input.GoogleScholarId,
                WebOfScienceResearcherId = input.WebOfScienceId,
                ScopusId = input.ScopusId
            });
            saved = response.IsSaved;
            bool persistenceDataTooLong = response.FailureCode == "PersistenceDataTooLong";
            bool hasErrors = providerCalls.Failures.Count > 0 ||
                response.Messages.Any(message => message.StartsWith("[HATA]", StringComparison.Ordinal)) ||
                (!string.IsNullOrWhiteSpace(input.Orcid) &&
                    (response.Researcher?.OrcidProfile is null || response.Researcher.OpenAlexProfile is null)) ||
                (!string.IsNullOrWhiteSpace(input.GoogleScholarId) && response.Researcher?.GoogleScholarProfile is null) ||
                (!string.IsNullOrWhiteSpace(input.WebOfScienceId) && response.Researcher?.WebOfScienceProfile is null);
            if (saved && !hasErrors)
            {
                job.Status = BulkJobStatus.Succeeded;
                job.ResultMessage = "Collection completed.";
            }
            else
            {
                retryable = !persistenceDataTooLong && (providerCalls.Failures.Any(failure => failure.Retryable) ||
                    (!saved && providerCalls.Failures.Count == 0));
                if (persistenceDataTooLong)
                    job.ResultMessage = "Collection could not be saved because provider metadata exceeds the database schema.";
                job.Status = saved ? BulkJobStatus.Partial : BulkJobStatus.Failed;
                job.ResultMessage ??= ProviderFailureMessage(providerCalls.Failures);
            }
        }
        catch (ArgumentException)
        {
            job.Status = BulkJobStatus.Rejected;
            job.ResultMessage = "Invalid researcher input.";
        }
        catch (Exception)
        {
            retryable = true;
            job.Status = BulkJobStatus.Failed;
            job.ResultMessage = "Collection failed; a retry may be scheduled.";
        }

        if (retryable && job.Attempts < options.Value.MaximumAttempts)
        {
            job.Status = BulkJobStatus.RetryWaiting;
            DateTime backoff = DateTime.UtcNow.AddSeconds(options.Value.RetrySeconds * Math.Pow(2, job.Attempts - 1));
            job.NextAttemptAt = providerCalls.Failures.Where(failure => failure.RetryAt.HasValue)
                .Select(failure => failure.RetryAt!.Value).Append(backoff).Max();
            string providers = ProviderNames(providerCalls.Failures);
            job.ResultMessage = providers.Length == 0
                ? "Temporary failure or provider cooldown; retry scheduled."
                : $"Temporary provider failure or cooldown ({providers}); retry scheduled.";
        }
        else
            job.CompletedAt = DateTime.UtcNow;
        // Persist the result even when shutdown was requested during a legacy collection call.
        await database.SaveChangesAsync(CancellationToken.None);
        logger.LogInformation("Bulk job {JobId} attempt {Attempt} ended with {Status} after {ElapsedMs} ms.",
            job.Id, job.Attempts, job.Status, (DateTime.UtcNow - startedAt).TotalMilliseconds);
        return true;
    }

    private static string ProviderFailureMessage(IReadOnlyList<ProviderCallFailure> failures)
    {
        string providers = ProviderNames(failures);
        return providers.Length == 0
            ? "Collection incomplete. Check provider configuration and researcher identifiers."
            : $"Collection incomplete after a provider failure ({providers}).";
    }

    private static string ProviderNames(IReadOnlyList<ProviderCallFailure> failures) =>
        string.Join(", ", failures.Select(failure => failure.Provider)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value));
}
