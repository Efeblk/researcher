using System.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;
using Microsoft.EntityFrameworkCore;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Metrics;

public sealed class PublicationMetricsRefreshService(
    AcademicDbContext database,
    CanonicalWorkSynchronizer canonicalWorkSynchronizer,
    PublicationMetricsRefreshScheduler scheduler)
{
    public async Task<bool> ScheduleAsync(string personelId, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        await canonicalWorkSynchronizer.AcquireWriteGateAsync(cancellationToken);
        await canonicalWorkSynchronizer.AcquireResearcherLockAsync(personelId, cancellationToken);
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
