using System.Diagnostics;
using AcademicCollector.Analysis.Contracts;

namespace ResearcherAnalysisService.Analysis;

public sealed record ArticleEvaluationUsage(
    string? ReturnedModel = null,
    int? InputTokens = null,
    int? OutputTokens = null,
    int? CacheReadTokens = null,
    int? CacheWriteTokens = null,
    int? ThinkingTokens = null,
    decimal? EstimatedCostUsd = null,
    string? PricingVersion = null);

public sealed class ArticleEvaluationAttemptRecorder(int maximumAttempts)
{
    private readonly AsyncLocal<AttemptContext?> _context = new();
    private readonly List<ArticleEvaluationAttemptTelemetry> _attempts = [];
    private readonly object _sync = new();
    private int _startedAttemptCount;

    public IDisposable Enter(string stage, string? role)
    {
        AttemptContext? previous = _context.Value;
        _context.Value = new(stage, role);
        return new ContextScope(() => _context.Value = previous);
    }

    public Attempt Begin(string provider, string requestedModel)
    {
        AttemptContext context = _context.Value ?? throw new InvalidOperationException(
            "Evaluation attempt context was not established.");
        lock (_sync)
        {
            if (_startedAttemptCount >= maximumAttempts)
                throw new InvalidOperationException("The evaluation provider call budget was exhausted.");
            int number = ++_startedAttemptCount;
            return new Attempt(this, number, context.Stage, context.Role, provider, requestedModel);
        }
    }

    public IReadOnlyList<ArticleEvaluationAttemptTelemetry> Snapshot()
    {
        lock (_sync) return _attempts.OrderBy(value => value.AttemptNumber).ToList();
    }

    public void MarkLatestCompletedAttemptFailed(string errorCode) =>
        TryUpdateLatest("completed", "failed", errorCode);

    public void MarkLatestCancelledAttemptTimedOut() =>
        TryUpdateLatest("cancelled", "timed_out", "timeout");

    private void TryUpdateLatest(string expectedStatus, string status, string errorCode)
    {
        lock (_sync)
        {
            int latestAttemptNumber = _startedAttemptCount;
            int index = _attempts.FindIndex(value => value.AttemptNumber == latestAttemptNumber);
            if (index >= 0 && _attempts[index].Status == expectedStatus)
                _attempts[index] = _attempts[index] with { Status = status, ErrorCode = errorCode };
        }
    }

    private void Add(ArticleEvaluationAttemptTelemetry telemetry)
    {
        lock (_sync) _attempts.Add(telemetry);
    }

    private sealed record AttemptContext(string Stage, string? Role);

    private sealed class ContextScope(Action dispose) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            dispose();
        }
    }

    public sealed class Attempt : IDisposable
    {
        private readonly ArticleEvaluationAttemptRecorder _owner;
        private readonly int _number;
        private readonly string _stage;
        private readonly string? _role;
        private readonly string _provider;
        private readonly string _requestedModel;
        private readonly DateTimeOffset _startedAtUtc = DateTimeOffset.UtcNow;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private bool _completed;

        internal Attempt(ArticleEvaluationAttemptRecorder owner, int number, string stage, string? role,
            string provider, string requestedModel)
        {
            _owner = owner;
            _number = number;
            _stage = stage;
            _role = role;
            _provider = provider;
            _requestedModel = requestedModel;
        }

        public void Complete(string status, string? errorCode, ArticleEvaluationUsage? usage = null)
        {
            if (_completed) return;
            _completed = true;
            _stopwatch.Stop();
            DateTimeOffset completedAtUtc = DateTimeOffset.UtcNow;
            usage ??= new();
            _owner.Add(new(_number, _stage, _role, _provider, _requestedModel, usage.ReturnedModel,
                status, _startedAtUtc, completedAtUtc, _stopwatch.ElapsedMilliseconds,
                usage.InputTokens, usage.OutputTokens, usage.CacheReadTokens, usage.CacheWriteTokens,
                usage.ThinkingTokens, usage.EstimatedCostUsd, usage.PricingVersion, errorCode));
        }

        public void Dispose() => Complete("failed", "provider_failure");
    }
}
