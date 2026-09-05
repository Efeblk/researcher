namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.RateLimiting;

// Tracks structured HTTP outcomes even when legacy provider clients catch exceptions.
// AsyncLocal keeps simultaneous interactive requests out of a bulk job's result.
public sealed class ProviderCallScope : IDisposable
{
    private static readonly AsyncLocal<ProviderCallScope?> Current = new();
    private readonly ProviderCallScope? _previous = Current.Value;
    public List<ProviderCallFailure> Failures { get; } = [];
    public static CancellationToken Cancellation => Current.Value?._cancellationToken ?? CancellationToken.None;
    private readonly CancellationToken _cancellationToken;

    public ProviderCallScope(CancellationToken cancellationToken = default)
    {
        _cancellationToken = cancellationToken;
        Current.Value = this;
    }

    public static void Record(string provider, bool retryable, DateTime? retryAt = null)
        => Current.Value?.Failures.Add(new(provider, retryable, retryAt));

    public void Dispose() => Current.Value = _previous;
}
