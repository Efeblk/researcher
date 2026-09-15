using System.Net;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.RateLimiting;
using Microsoft.Extensions.Configuration;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class ProviderRequestPolicyTests
{
    [Fact]
    public void GetEffectiveDailyRequestLimit_OpenAlexKeyConfigured_UsesConfiguredSafetyCeiling()
    {
        IConfiguration configuration = Configuration(new()
        {
            ["OpenAlex:ApiKey"] = "synthetic-key",
            ["ProviderRequestLimits:OpenAlex:DailyRequestLimit"] = "10000"
        });

        Assert.Equal(10000,
            ProviderRequestPolicy.GetEffectiveDailyRequestLimit(configuration, "OpenAlex"));
    }

    [Fact]
    public void GetEffectiveDailyRequestLimit_OpenAlexKeyMissing_ClampsConfiguredLimit()
    {
        IConfiguration configuration = Configuration(new()
        {
            ["ProviderRequestLimits:OpenAlex:DailyRequestLimit"] = "10000"
        });

        Assert.Equal(1000,
            ProviderRequestPolicy.GetEffectiveDailyRequestLimit(configuration, "OpenAlex"));
    }

    [Fact]
    public void GetEffectiveDailyRequestLimit_OpenAlexKeyMissingAndConfiguredUnlimited_UsesAnonymousCap()
    {
        IConfiguration configuration = Configuration(new()
        {
            ["ProviderRequestLimits:OpenAlex:DailyRequestLimit"] = "0"
        });

        Assert.Equal(1000,
            ProviderRequestPolicy.GetEffectiveDailyRequestLimit(configuration, "OpenAlex"));
    }

    [Fact]
    public void GetRetryAt_NoRetryAfter_UsesDefaultFallback()
    {
        DateTime now = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);
        using HttpResponseMessage response = new(HttpStatusCode.TooManyRequests);

        DateTime retryAt = ProviderRateLimitHandler.GetRetryAt(response, now);

        Assert.Equal(now.AddMinutes(1), retryAt);
    }

    [Fact]
    public void GetRetryAt_LongerRetryAfter_PreservesProviderValue()
    {
        DateTime now = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);
        using HttpResponseMessage response = new(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new(TimeSpan.FromMinutes(15));

        DateTime retryAt = ProviderRateLimitHandler.GetRetryAt(response, now);

        Assert.Equal(now.AddMinutes(15), retryAt);
    }

    private static IConfiguration Configuration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
}
