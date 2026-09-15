namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.RateLimiting;

public sealed class ProviderRequestPolicy
{
    public bool Enabled { get; init; } = true;
    public string Name { get; init; } = string.Empty;
    public string Host { get; init; } = string.Empty;
    public int MinimumIntervalMilliseconds { get; init; } = 1000;
    public int DailyRequestLimit { get; init; }
    public int RateLimitCooldownSeconds { get; init; }

    public static int GetEffectiveDailyRequestLimit(IConfiguration configuration, string provider)
    {
        int configuredLimit = configuration.GetValue($"ProviderRequestLimits:{provider}:DailyRequestLimit", 0);
        if (provider != "OpenAlex" || !string.IsNullOrWhiteSpace(configuration["OpenAlex:ApiKey"]))
            return configuredLimit;
        return configuredLimit == 0 ? 1000 : Math.Min(configuredLimit, 1000);
    }
}
