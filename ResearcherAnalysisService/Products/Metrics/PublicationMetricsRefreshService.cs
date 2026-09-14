using System.Data;
using ResearcherAnalysisService.Products.Data;
using Microsoft.EntityFrameworkCore;

namespace ResearcherAnalysisService.Products.Metrics;

public sealed class PublicationMetricsRefreshService(
    AnalysisDbContext database,
    AnalysisSourceLock sourceLock,
    PublicationMetricsRefreshScheduler scheduler)
{
    public async Task<bool> ScheduleAsync(string personelId, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        await sourceLock.AcquireWriteGateAsync(cancellationToken);
        await sourceLock.AcquireResearcherLockAsync(personelId, cancellationToken);
        bool exists = await database.Researchers.AsNoTracking()
            .AnyAsync(researcher => researcher.PersonelId == personelId, cancellationToken);
        if (!exists)
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        await scheduler.ScheduleAsync(personelId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
