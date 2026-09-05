namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.RateLimiting;

public sealed class ProviderRequestPolicy
{
    public string Name { get; init; } = string.Empty;
    public string Host { get; init; } = string.Empty;
    public int MinimumIntervalMilliseconds { get; init; } = 1000;
    public int DailyRequestLimit { get; init; }
}
