using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Products.ArticleSummaries;
using ResearcherAnalysisService.Products.Metrics;
using ResearcherAnalysisService.SourceData;

namespace ResearcherAnalysisService.Products.Data;

public sealed class CollectionChangeProcessor(
    AnalysisDbContext database,
    AnalysisSourceLock sourceLock,
    ArticleSummaryAutomationScheduler summaryScheduler,
    PublicationMetricsRefreshScheduler metricsScheduler,
    IOptionsMonitor<CollectionChangeOptions> options,
    TimeProvider timeProvider,
    ILogger<CollectionChangeProcessor> logger)
{
    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken = default)
    {
        if (!options.CurrentValue.WorkerEnabled)
            return 0;

        int processed = 0;
        try
        {
            while (processed < options.CurrentValue.BatchSize)
            {
                database.ChangeTracker.Clear();
                await using var transaction = await database.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable, cancellationToken);
                await sourceLock.AcquireWriteGateAsync(cancellationToken);
                CollectionChange? change = await database.CollectionChanges.AsNoTracking()
                    .Where(candidate => !database.CollectionChangeReceipts.Any(
                        receipt => receipt.EventId == candidate.EventId))
                    .OrderBy(candidate => candidate.OccurredAtUtc)
                    .ThenBy(candidate => candidate.EventId)
                    .FirstOrDefaultAsync(cancellationToken);
                if (change is null)
                {
                    await transaction.CommitAsync(cancellationToken);
                    break;
                }

                bool scheduled = false;
                if (change.ChangeKind == "ResearcherCollected" &&
                    !string.IsNullOrWhiteSpace(change.PersonelId))
                {
                    await metricsScheduler.ScheduleAsync(change.PersonelId, cancellationToken);
                    scheduled = true;
                }
                else if (change.ChangeKind == "CanonicalWorkChanged" && change.CanonicalWorkId is > 0)
                {
                    bool sourceExists = await database.CanonicalWorkObservations.AsNoTracking()
                        .AnyAsync(value => value.CanonicalWorkId == change.CanonicalWorkId, cancellationToken);
                    if (sourceExists)
                    {
                        await summaryScheduler.ScheduleAsync([change.CanonicalWorkId.Value], cancellationToken);
                        scheduled = true;
                    }
                    else
                    {
                        List<ArticleSummaryAutomationJob> jobs = await database.ArticleSummaryAutomationJobs
                            .Where(value => value.CanonicalWorkId == change.CanonicalWorkId)
                            .ToListAsync(cancellationToken);
                        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
                        foreach (ArticleSummaryAutomationJob job in jobs)
                        {
                            job.Status = ArticleSummaryAutomationJobStatus.Failed;
                            job.LastOutcomeCode = "SourceRemoved";
                            job.LastOutcomeMessage = "Collector source association was removed.";
                            job.UpdatedAt = now;
                        }
                        scheduled = jobs.Count != 0;
                    }
                }

                database.CollectionChangeReceipts.Add(new()
                {
                    EventId = change.EventId,
                    ReceivedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
                    ScheduledAtUtc = scheduled ? timeProvider.GetUtcNow().UtcDateTime : null
                });
                await database.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                processed++;
            }
        }
        catch (SqlException exception) when (exception.Number == 208)
        {
            logger.LogDebug("Collector source tables are not available; change processing will retry.");
        }
        return processed;
    }
}
