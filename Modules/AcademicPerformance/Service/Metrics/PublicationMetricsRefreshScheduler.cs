using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Metrics;

public sealed class PublicationMetricsRefreshScheduler(
    AcademicDbContext database,
    IOptionsMonitor<PublicationMetricsOptions> options,
    TimeProvider timeProvider)
{
    public async Task ScheduleAsync(string personelId, CancellationToken cancellationToken = default)
    {
        if (database.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A transaction is required to schedule publication metrics.");

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        PublicationMetricsOptions settings = options.CurrentValue;
        PublicationMetricsRefreshState? state = database.PublicationMetricsRefreshStates.Local
            .SingleOrDefault(value => value.PersonelId == personelId);
        if (state is not null && database.Entry(state).State == EntityState.Unchanged)
            await database.Entry(state).ReloadAsync(cancellationToken);
        state ??= await database.PublicationMetricsRefreshStates
            .SingleOrDefaultAsync(value => value.PersonelId == personelId, cancellationToken);
        if (state is null)
        {
            database.PublicationMetricsRefreshStates.Add(new()
            {
                PersonelId = personelId,
                RequestedRevision = 1,
                ComputedRevision = 0,
                RequestedCatalogVersion = settings.CatalogVersion.Trim(),
                RequestedComputationYear = now.Year,
                NextAttemptAt = now,
                UpdatedAt = now
            });
        }
        else
        {
            state.RequestedRevision = checked(state.RequestedRevision + 1);
            state.RequestedCatalogVersion = settings.CatalogVersion.Trim();
            state.RequestedComputationYear = now.Year;
            state.NextAttemptAt = now;
            state.Attempts = 0;
            state.UpdatedAt = now;
            state.LastOutcomeCode = null;
            state.LastOutcomeMessage = null;
        }
        await database.SaveChangesAsync(cancellationToken);
    }
}
