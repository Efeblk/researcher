using System.Net;
using System.Text.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Status;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class ProviderStatusServiceTests
{
    [Theory]
    [InlineData(0, "ProviderReported")]
    [InlineData(null, "Unknown")]
    public void BuildRemainingUsage_ReportedZeroAndMissingRemaining_AreDistinct(
        int? remaining, string expectedStatus)
    {
        DateTime now = new(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc);
        ProviderStatusDto provider = ProviderWithQuota(now, remaining);

        ProviderRemainingUsageDto usage = ProviderStatusService.BuildRemainingUsage(provider, now);

        Assert.Equal(expectedStatus, usage.Status);
        Assert.Equal(remaining, usage.Items[0].Value);
    }

    [Fact]
    public void BuildRemainingUsage_DerivedSearchApiRemainder_IsExplicit()
    {
        DateTime now = new(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc);
        ProviderStatusDto provider = ProviderWithQuota(now, 88);
        provider.ProviderQuotas[0].ValueKind = "DerivedFromProviderValues";

        ProviderRemainingUsageDto usage = ProviderStatusService.BuildRemainingUsage(provider, now);

        Assert.Equal("Derived", usage.Status);
        Assert.Equal(88, usage.Items[0].Value);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void BuildRemainingUsage_ExpiredObservationOrElapsedReset_IsStale(
        bool observationExpired, bool resetElapsed)
    {
        DateTime now = new(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc);
        ProviderStatusDto provider = ProviderWithQuota(now, 42);
        provider.ProviderQuotas[0].ExpiresAt = observationExpired ? now : now.AddMinutes(1);
        provider.ProviderQuotas[0].ResetsAt = resetElapsed ? now : now.AddHours(1);

        ProviderRemainingUsageDto usage = ProviderStatusService.BuildRemainingUsage(provider, now);

        Assert.Equal("Stale", usage.Status);
        Assert.Null(usage.Items[0].Value);
    }

    [Fact]
    public void BuildRemainingUsage_UnknownHeaderScope_DoesNotExposeBalance()
    {
        DateTime now = new(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc);
        ProviderStatusDto provider = ProviderWithQuota(now, 42);
        provider.ProviderQuotas[0].Source = "ResponseHeaders";
        provider.ProviderQuotas[0].Scope = null;
        provider.ProviderQuotas[0].Unit = "unknown";

        ProviderRemainingUsageDto usage = ProviderStatusService.BuildRemainingUsage(provider, now);

        Assert.Equal("Unknown", usage.Status);
        Assert.Null(usage.Items[0].Value);
        Assert.Equal(42, provider.ProviderQuotas[0].Remaining);
    }

    [Fact]
    public void RefreshRemainingUsage_CachedObservationCrossesExpiryBoundary_NullsValue()
    {
        DateTime observed = new(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc);
        ProviderStatusDto provider = ProviderWithQuota(observed, 9);
        ProviderStatusResponse cached = new()
        {
            CheckedAt = observed, ExpiresAt = observed.AddMinutes(1), Providers = [provider]
        };
        provider.ProviderQuotas[0].ExpiresAt = observed.AddSeconds(10);
        provider.RemainingUsage = ProviderStatusService.BuildRemainingUsage(provider, observed);
        Assert.Equal(9, provider.RemainingUsage.Items[0].Value);

        ProviderStatusService.RefreshRemainingUsage(cached, observed.AddSeconds(11));

        Assert.Equal("Stale", provider.RemainingUsage.Status);
        Assert.Null(provider.RemainingUsage.Items[0].Value);
        Assert.True(cached.ExpiresAt > observed.AddSeconds(11));
    }

    [Theory]
    [InlineData("NotConfigured")]
    [InlineData("Unavailable")]
    public void BuildRemainingUsage_MissingKeyOrUpstreamFailure_IsUnavailable(string status)
    {
        ProviderRemainingUsageDto usage = ProviderStatusService.BuildRemainingUsage(
            new ProviderStatusDto { Provider = "SearchApi", Status = status }, DateTime.UtcNow);

        Assert.Equal("Unavailable", usage.Status);
        Assert.Empty(usage.Items);
    }

    [Fact]
    public void BuildRemainingUsage_UnauthorizedResponseSuppressesOtherwiseNumericItem()
    {
        DateTime now = new(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc);
        ProviderStatusDto provider = ProviderWithQuota(now, 12);
        provider.Status = "Unauthorized";

        ProviderRemainingUsageDto usage = ProviderStatusService.BuildRemainingUsage(provider, now);

        Assert.Equal("Unavailable", usage.Status);
        Assert.Equal("Unavailable", usage.Items[0].Status);
        Assert.Null(usage.Items[0].Value);
        Assert.Equal(12, provider.ProviderQuotas[0].Remaining);
    }

    [Fact]
    public void ParseSearchApiQuotas_MissingOrMalformedFields_DoesNotInventRemaining()
    {
        using var document = JsonDocument.Parse("""
            {"account":{"monthly_allowance":"secret","remaining_credits":-1},
             "api_usage":{"hourly_rate_limit":100}}
            """);
        var quotas = ProviderStatusService.ParseSearchApiQuotas(document.RootElement);
        Assert.Single(quotas);
        Assert.Null(quotas[0].Remaining);
        Assert.Equal(100, quotas[0].Limit);
    }

    [Fact]
    public void ParseHeaderQuotas_MultipleWindows_KeepsDailyAndSecondSeparate()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Add("X-RateLimit-Limit-Day", "500");
        response.Headers.Add("X-RateLimit-Remaining-Day", "123");
        response.Headers.Add("X-RateLimit-Limit-Second", "5");
        response.Headers.Add("X-RateLimit-Remaining-Second", "invalid");
        var quotas = ProviderStatusService.ParseHeaderQuotas(response);
        Assert.Equal(2, quotas.Count);
        Assert.Equal("day", quotas[0].Window);
        Assert.Equal(123, quotas[0].Remaining);
        Assert.Equal("second", quotas[1].Window);
        Assert.Null(quotas[1].Remaining);
        Assert.Equal("unknown", quotas[0].Unit);
        Assert.Null(quotas[0].Scope);
    }

    [Theory]
    [InlineData("90", true)]
    [InlineData("invalid", false)]
    [InlineData("999999999999999999999", false)]
    public void ParseOpenAlexResetAt_StandardSecondsHeader_RejectsInvalidOrOverflow(
        string value, bool expected)
    {
        DateTime observedAt = new(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc);
        using HttpResponseMessage response = new(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("X-RateLimit-Reset", value);

        DateTime? resetsAt = ProviderStatusService.ParseOpenAlexResetAt(response, observedAt);

        Assert.Equal(expected, resetsAt.HasValue);
        if (expected) Assert.Equal(observedAt.AddSeconds(90), resetsAt);
    }

    [Theory]
    [InlineData("""{"overallOk":false,"tomcatUp":true,"dbConnectionOk":true,"readOnlyDbConnectionOk":true}""", "Unhealthy", false)]
    [InlineData("""{"tomcatUp":true,"dbConnectionOk":true,"readOnlyDbConnectionOk":true}""", "UnexpectedResponse", null)]
    [InlineData("""{"overallOk":"true","tomcatUp":true,"dbConnectionOk":true,"readOnlyDbConnectionOk":true}""", "UnexpectedResponse", null)]
    public void ParseOrcidHealth_InvalidOrUnhealthyResponse_DoesNotClaimHealthy(
        string json, string expectedStatus, bool? expectedOverall)
    {
        using var document = JsonDocument.Parse(json);
        var provider = new AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts.ProviderStatusDto
            { Status = "Healthy" };
        var health = ProviderStatusService.ParseOrcidHealth(document.RootElement, DateTime.UtcNow, provider);
        Assert.Equal(expectedStatus, health.Status);
        Assert.Equal(expectedStatus, provider.Status);
        Assert.Equal(expectedOverall, health.OverallOk);
        Assert.True(health.TomcatUp);
    }

    [Fact]
    public void ParseOrcidHealth_AllBooleanComponents_PreservesValues()
    {
        using var document = JsonDocument.Parse(
            """{"overallOk":true,"tomcatUp":true,"dbConnectionOk":false,"readOnlyDbConnectionOk":true}""");
        var health = ProviderStatusService.ParseOrcidHealth(document.RootElement, DateTime.UtcNow);
        Assert.True(health.OverallOk);
        Assert.False(health.DbConnectionOk);
    }

    [Fact]
    public void ParseOpenAlexQuotas_PublishedShape_ParsesOnlySanitizedQuotaFields()
    {
        using var document = JsonDocument.Parse("""
            {"api_key":"must-not-leak","rate_limit":{"credits_limit":10000,"credits_used":125,
             "credits_remaining":9875,"resets_at":"2026-09-10T00:00:00Z","resets_in_seconds":123}}
            """);
        var quota = Assert.Single(ProviderStatusService.ParseOpenAlexQuotas(document.RootElement));
        Assert.Equal(9875, quota.Remaining);
        Assert.Equal("api-key", quota.Scope);
        Assert.Equal(new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc), quota.ResetsAt);
        Assert.DoesNotContain("api_key", JsonSerializer.Serialize(quota), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"rate_limit\":{}}")]
    [InlineData("{\"rate_limit\":{\"credits_limit\":\"100\"}}")]
    [InlineData("{\"rate_limit\":{\"resets_at\":\"2026-09-10T00:00:00Z\"}}")]
    public void ParseOpenAlexQuotas_UnknownOrMalformedSchema_ReturnsNoQuota(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Empty(ProviderStatusService.ParseOpenAlexQuotas(document.RootElement));
    }

    [Fact]
    public void ParseSearchApiQuotas_PartialValues_PreservesEvidenceAndDerivation()
    {
        using var document = JsonDocument.Parse("""
            {"account":{"monthly_allowance":1000},"api_usage":{"hourly_rate_limit":100,"searches_this_hour":12},
             "subscription":{"period_end":"2026-10-01T00:00:00Z"}}
            """);
        var quotas = ProviderStatusService.ParseSearchApiQuotas(document.RootElement);
        Assert.Equal(2, quotas.Count);
        Assert.Null(quotas[0].Remaining);
        Assert.Equal(88, quotas[1].Remaining);
        Assert.Equal("DerivedFromProviderValues", quotas[1].ValueKind);
        Assert.Null(quotas[1].ResetsAt);
        Assert.NotNull(quotas[1].SubscriptionPeriodEndsAt);
    }

    private static ProviderStatusDto ProviderWithQuota(DateTime now, decimal? remaining) => new()
    {
        Provider = "SearchApi",
        Status = "Healthy",
        ProviderQuotas = [new()
        {
            Source = "AccountApi", Scope = "account", Unit = "searches", Window = "month",
            Remaining = remaining, ValueKind = "ProviderReported", ObservedAt = now.AddSeconds(-30),
            ExpiresAt = now.AddSeconds(30)
        }]
    };
}
