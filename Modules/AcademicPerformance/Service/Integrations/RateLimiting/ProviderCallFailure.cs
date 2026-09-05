namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.RateLimiting;

public sealed record ProviderCallFailure(string Provider, bool Retryable, DateTime? RetryAt);
