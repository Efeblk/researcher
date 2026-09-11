using System.Collections.Concurrent;
using ResearcherAnalysisService.Integrations.Gemini;

namespace ResearcherAnalysisService.Tests.Infrastructure;

internal sealed class TestGeminiUsageRepository : IGeminiUsageRepository
{
    private readonly ConcurrentDictionary<Guid, Entry> _entries = new();

    public bool FailBegin { get; set; }
    public bool FailComplete { get; set; }
    public GeminiSpendingStatus? SpendingOverride { get; set; }
    public IReadOnlyList<Entry> Entries => _entries.Values.OrderBy(item => item.StartedAt)
        .ThenBy(item => item.AttemptId).ToList();

    public Task BeginAsync(Guid attemptId, DateTime startedAt, string requestedModel,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FailBegin) throw new InvalidOperationException("Synthetic storage failure.");
        if (!_entries.TryAdd(attemptId, new(attemptId, startedAt, requestedModel, null)))
            throw new InvalidOperationException("Duplicate attempt.");
        return Task.CompletedTask;
    }

    public Task CompleteAsync(Guid attemptId, DateTime completedAt, GeminiUsageCompletion completion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FailComplete) throw new InvalidOperationException("Synthetic completion failure.");
        if (!_entries.TryGetValue(attemptId, out Entry? entry) || entry.Completion is not null)
            throw new InvalidOperationException("Attempt not pending.");
        _entries[attemptId] = entry with { Completion = completion };
        return Task.CompletedTask;
    }

    public Task<GeminiSpendingStatus> GetSpendingAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (SpendingOverride is not null) return Task.FromResult(SpendingOverride);
        Entry[] entries = _entries.Values.OrderByDescending(item => item.StartedAt)
            .ThenByDescending(item => item.AttemptId).ToArray();
        long unknown = entries.LongCount(item => item.Completion?.EstimatedUsd is null);
        return Task.FromResult(new GeminiSpendingStatus
        {
            Available = true,
            Since = entries.Length == 0 ? null : entries.Min(item => item.StartedAt),
            RequestCount = entries.LongLength,
            UnknownCount = unknown,
            EstimatedTotalUsd = unknown == 0 ? entries.Sum(item => item.Completion!.EstimatedUsd!.Value) : null,
            Last3 = entries.Take(3).Select(item => new GeminiSpendingItem(item.StartedAt,
                item.Completion?.ReturnedModel ?? item.RequestedModel, item.Completion?.EstimatedUsd)).ToList()
        });
    }

    internal sealed record Entry(Guid AttemptId, DateTime StartedAt, string RequestedModel,
        GeminiUsageCompletion? Completion);
}
