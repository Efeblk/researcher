namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.RateLimiting;

// Tracks structured HTTP outcomes even when legacy provider clients catch exceptions.
// AsyncLocal keeps simultaneous interactive requests out of a bulk job's result.
public sealed class ProviderCallScope : IDisposable
{
    private static readonly AsyncLocal<ProviderCallScope?> Current = new();
    private readonly ProviderCallScope? _previous = Current.Value;
    private int _requestOrdinal;
    public List<ProviderCallFailure> Failures { get; } = [];
    public int RequestsStarted => _requestOrdinal;
    public static CancellationToken Cancellation => Current.Value?._cancellationToken ?? CancellationToken.None;
    public static bool IsActive => Current.Value is not null;
    private readonly CancellationToken _cancellationToken;

    public ProviderCallScope(CancellationToken cancellationToken = default)
    {
        _cancellationToken = cancellationToken;
        Current.Value = this;
    }

    public static void Record(string provider, bool retryable, DateTime? retryAt = null,
        bool isLocalDeferral = false, bool isDisabled = false)
        => Current.Value?.Failures.Add(new(provider, retryable, retryAt, isLocalDeferral, isDisabled));

    public static bool HasFailure(string provider) => Current.Value?.Failures.Any(failure =>
        string.Equals(failure.Provider, provider, StringComparison.OrdinalIgnoreCase)) == true;

    public static int NextRequestOrdinal() => Current.Value is null ? 0 : ++Current.Value._requestOrdinal;

    public void Dispose() => Current.Value = _previous;
}
