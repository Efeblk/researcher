using System.Text.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Metrics;

public sealed class PublicationMetricsReadService(
    AcademicDbContext database,
    IOptionsMonitor<PublicationMetricsOptions> options,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ResearcherPublicationMetricsStatusResponse?> GetAsync(
        string personelId,
        CancellationToken cancellationToken)
    {
        bool researcherExists = await database.Researchers.AsNoTracking()
            .AnyAsync(researcher => researcher.PersonelId == personelId, cancellationToken);
        if (!researcherExists)
            return null;

        PublicationMetricsRefreshState? state = await database.PublicationMetricsRefreshStates
            .AsNoTracking()
            .Include(value => value.LastSuccessfulSnapshot)
            .SingleOrDefaultAsync(value => value.PersonelId == personelId, cancellationToken);
        string currentCatalogVersion = options.CurrentValue.CatalogVersion.Trim();
        int currentComputationYear = timeProvider.GetUtcNow().Year;
        if (state is null)
        {
            return new()
            {
                PersonelId = personelId,
                CurrentCatalogVersion = currentCatalogVersion,
                CurrentComputationYear = currentComputationYear,
                Status = "Pending",
                IsStale = false
            };
        }

        PublicationMetricSnapshot? snapshot = state.LastSuccessfulSnapshot;
        ResearcherPublicationMetricsResponse? data = snapshot is null
            ? null
            : JsonSerializer.Deserialize<ResearcherPublicationMetricsResponse>(
                snapshot.ResultJson, JsonOptions);
        bool isCurrent = snapshot is not null &&
            state.ComputedRevision >= state.RequestedRevision &&
            snapshot.SourceRevision == state.ComputedRevision &&
            snapshot.CatalogVersion == currentCatalogVersion &&
            snapshot.ComputationYear == currentComputationYear;
        string status = snapshot is not null
            ? isCurrent ? "Current" : "Stale"
            : state.LastOutcomeCode is null ? "Pending" : "Failed";
        bool retryPending = !isCurrent && state.Attempts < options.CurrentValue.MaximumAttempts &&
            state.RequestedRevision > state.ComputedRevision;

        return new()
        {
            PersonelId = personelId,
            CurrentCatalogVersion = currentCatalogVersion,
            CurrentComputationYear = currentComputationYear,
            RequestedCatalogVersion = state.RequestedCatalogVersion,
            RequestedComputationYear = state.RequestedComputationYear,
            RequestedRevision = state.RequestedRevision,
            ComputedRevision = state.ComputedRevision,
            SnapshotId = snapshot?.Id,
            SnapshotCatalogVersion = snapshot?.CatalogVersion,
            SnapshotComputationYear = snapshot?.ComputationYear,
            Status = status,
            IsStale = snapshot is not null && !isCurrent,
            ComputedAt = AsUtc(snapshot?.ComputedAt),
            RefreshOutcome = new()
            {
                Attempts = state.Attempts,
                NextAttemptAt = retryPending ? AsUtc(state.NextAttemptAt) : null,
                LastSuccessAt = AsUtc(state.LastSuccessAt),
                Code = state.LastOutcomeCode,
                Message = state.LastOutcomeMessage
            },
            Data = data
        };
    }

    private static DateTime? AsUtc(DateTime? value) => value.HasValue
        ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
        : null;
}
