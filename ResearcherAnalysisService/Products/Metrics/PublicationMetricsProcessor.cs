using System.Data;
using ResearcherAnalysisService.Products.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ResearcherAnalysisService.Products.Metrics;

public sealed class PublicationMetricsProcessor(
    AnalysisDbContext database,
    IPublicationMetricsComputer computer,
    AnalysisSourceLock sourceLock,
    IOptionsMonitor<PublicationMetricsOptions> options,
    TimeProvider timeProvider)
{
    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken = default)
    {
        PublicationMetricsOptions settings = options.CurrentValue;
        if (!settings.WorkerEnabled)
            return 0;

        await using SqlApplicationLock? workerLock = await SqlApplicationLock.TryAcquireAsync(
            database.Database.GetConnectionString()!,
            "AcademicCollector.PublicationMetricsWorker", 0, cancellationToken);
        if (workerLock is null)
            return 0;

        string catalogVersion = settings.CatalogVersion.Trim();
        int computationYear = timeProvider.GetUtcNow().Year;
        await InitializeTargetsAsync(
            catalogVersion, computationYear, settings.BatchSize, cancellationToken);

        int processed = 0;
        while (processed < settings.BatchSize && await ComputeOneAsync(
            catalogVersion, computationYear, settings, cancellationToken))
        {
            processed++;
        }
        return processed;
    }

    private async Task InitializeTargetsAsync(
        string catalogVersion,
        int computationYear,
        int batchSize,
        CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        await sourceLock.AcquireWriteGateAsync(cancellationToken);
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;

        string[] missingStateIds = await database.Researchers.AsNoTracking()
            .Where(researcher => !database.PublicationMetricsRefreshStates.Any(state =>
                state.PersonelId == researcher.PersonelId))
            .OrderBy(researcher => researcher.PersonelId)
            .Select(researcher => researcher.PersonelId)
            .Take(batchSize)
            .ToArrayAsync(cancellationToken);
        foreach (string personelId in missingStateIds)
        {
            database.PublicationMetricsRefreshStates.Add(new()
            {
                PersonelId = personelId,
                RequestedRevision = 1,
                ComputedRevision = 0,
                RequestedCatalogVersion = catalogVersion,
                RequestedComputationYear = computationYear,
                NextAttemptAt = now,
                UpdatedAt = now
            });
        }

        List<PublicationMetricsRefreshState> changedTargets = await database
            .PublicationMetricsRefreshStates
            .Where(state => state.RequestedCatalogVersion != catalogVersion ||
                state.RequestedComputationYear != computationYear)
            .OrderBy(state => state.PersonelId)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
        foreach (PublicationMetricsRefreshState state in changedTargets)
        {
            state.RequestedRevision = checked(state.RequestedRevision + 1);
            state.RequestedCatalogVersion = catalogVersion;
            state.RequestedComputationYear = computationYear;
            state.NextAttemptAt = now;
            state.Attempts = 0;
            state.UpdatedAt = now;
            state.LastOutcomeCode = null;
            state.LastOutcomeMessage = null;
        }

        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<bool> ComputeOneAsync(
        string catalogVersion,
        int computationYear,
        PublicationMetricsOptions settings,
        CancellationToken cancellationToken)
    {
        RefreshTarget? target = null;
        try
        {
            database.ChangeTracker.Clear();
            await using var transaction = await database.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            await sourceLock.AcquireWriteGateAsync(cancellationToken);
            DateTime now = timeProvider.GetUtcNow().UtcDateTime;
            if (now.Year != computationYear)
            {
                await transaction.CommitAsync(cancellationToken);
                return false;
            }
            PublicationMetricsRefreshState? state = await database.PublicationMetricsRefreshStates
                .Where(value => value.RequestedRevision > value.ComputedRevision &&
                    value.RequestedCatalogVersion == catalogVersion &&
                    value.RequestedComputationYear == computationYear &&
                    value.Attempts < settings.MaximumAttempts &&
                    value.NextAttemptAt <= now)
                .OrderBy(value => value.NextAttemptAt)
                .ThenBy(value => value.PersonelId)
                .FirstOrDefaultAsync(cancellationToken);
            if (state is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return false;
            }

            target = new(state.PersonelId, state.RequestedRevision,
                state.RequestedCatalogVersion, state.RequestedComputationYear);
            PublicationMetricComputation computation = await computer.ComputeAsync(
                state.PersonelId, catalogVersion, now, cancellationToken);
            PublicationMetricSnapshot snapshot = new()
            {
                PersonelId = state.PersonelId,
                CatalogVersion = catalogVersion,
                SourceRevision = state.RequestedRevision,
                ComputationYear = computationYear,
                ComputedAt = computation.Data.ComputedAt,
                ResultJson = computation.ResultJson,
                CanonicalWorkCount = computation.Data.CanonicalWorkCount,
                ProviderObservationCount = computation.Data.ProviderObservationCount,
                UnmappedAcademicWorkCount = computation.Data.UnmappedAcademicWorkCount
            };
            snapshot.ProviderMetrics.AddRange(
                PublicationProviderMetricsMapper.CreateSnapshotRows(computation.Data.ProviderMetrics));
            database.PublicationMetricSnapshots.Add(snapshot);
            await database.SaveChangesAsync(cancellationToken);

            state.ComputedRevision = state.RequestedRevision;
            state.LastSuccessfulSnapshotId = snapshot.Id;
            state.LastSuccessAt = snapshot.ComputedAt;
            state.NextAttemptAt = snapshot.ComputedAt;
            state.Attempts = 0;
            state.UpdatedAt = snapshot.ComputedAt;
            state.LastOutcomeCode = null;
            state.LastOutcomeMessage = null;
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            if (target is null)
                throw;
            await RecordFailureAsync(target, settings, CancellationToken.None);
            return true;
        }
    }

    private async Task RecordFailureAsync(
        RefreshTarget target,
        PublicationMetricsOptions settings,
        CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        await sourceLock.AcquireWriteGateAsync(cancellationToken);
        PublicationMetricsRefreshState? state = await database.PublicationMetricsRefreshStates
            .SingleOrDefaultAsync(value => value.PersonelId == target.PersonelId, cancellationToken);
        if (state is null || state.ComputedRevision >= target.RequestedRevision ||
            state.RequestedRevision != target.RequestedRevision ||
            state.RequestedCatalogVersion != target.CatalogVersion ||
            state.RequestedComputationYear != target.ComputationYear)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        state.Attempts++;
        state.UpdatedAt = now;
        state.LastOutcomeCode = "MetricComputationFailed";
        state.LastOutcomeMessage =
            "Publication metrics computation failed; saved source data and the last successful snapshot were retained.";
        state.NextAttemptAt = state.Attempts >= settings.MaximumAttempts
            ? DateTime.MaxValue
            : now.AddSeconds(settings.RetrySeconds * Math.Pow(2, state.Attempts - 1));
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private sealed record RefreshTarget(
        string PersonelId,
        long RequestedRevision,
        string CatalogVersion,
        int ComputationYear);
}
