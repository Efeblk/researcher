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
    AcademicDbContext database, IServiceScopeFactory scopes, IOptions<BulkCollectionOptions> options)
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
        job.Attempts++;
        await database.SaveChangesAsync(cancellationToken);

        using ProviderCallScope providerCalls = new(cancellationToken);
        bool saved = false;
        bool retryable = false;
        try
        {
            BulkResearcherInput input = JsonSerializer.Deserialize<BulkResearcherInput>(job.InputJson)!;
            // A separate EF scope keeps a failed collection transaction out of queue bookkeeping.
            using IServiceScope collectionScope = scopes.CreateScope();
            var service = collectionScope.ServiceProvider.GetRequiredService<IAcademicPerformanceApplicationService>();
            AcademicDataResponse response = await service.CollectAsync(new()
            {
                Orcid = input.Orcid,
                GoogleScholarId = input.GoogleScholarId,
                WebOfScienceResearcherId = input.WebOfScienceId
            });
            saved = response.IsSaved;
            job.ResearcherId = response.Researcher?.Id > 0 ? response.Researcher.Id : null;
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
                retryable = providerCalls.Failures.Any(failure => failure.Retryable) ||
                    (!saved && providerCalls.Failures.Count == 0);
                job.Status = saved ? BulkJobStatus.Partial : BulkJobStatus.Failed;
                job.ResultMessage = "Collection incomplete. Check provider configuration and researcher identifiers.";
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
            job.ResultMessage = "Temporary failure or provider cooldown; retry scheduled.";
        }
        else
            job.CompletedAt = DateTime.UtcNow;
        // Persist the result even when shutdown was requested during a legacy collection call.
        await database.SaveChangesAsync(CancellationToken.None);
        return true;
    }
}
