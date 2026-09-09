using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Endpoints;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class ProviderStatusSummaryMapperTests
{
    private static readonly DateTime Now = new(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Map_FreshPartialProviderQuota_PreservesLimitWithUnknownRemaining()
    {
        ProviderStatusDto provider = ProviderWithQuota();
        provider.ProviderQuotas[0].Remaining = null;
        provider.RemainingUsage.Items[0].Status = "Unknown";
        provider.RemainingUsage.Items[0].Value = null;

        ProviderQuotaSummaryDto quota = Assert.Single(Map(provider).Quotas);

        Assert.Equal(100, quota.Limit);
        Assert.Null(quota.Remaining);
        Assert.Equal("searches", quota.Unit);
        Assert.Equal("monthly", quota.Period);
    }

    [Theory]
    [InlineData(70)]
    [InlineData(0)]
    public void Map_FreshReportedQuota_PreservesRemaining(int remaining)
    {
        ProviderStatusDto provider = ProviderWithQuota();
        provider.ProviderQuotas[0].Remaining = remaining;
        provider.RemainingUsage.Items[0].Value = remaining;

        Assert.Equal(remaining, Assert.Single(Map(provider).Quotas).Remaining);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Map_StaleOrFailedObservation_SuppressesNumericValues(bool failed)
    {
        ProviderStatusDto provider = ProviderWithQuota();
        provider.Status = failed ? "Unauthorized" : "Healthy";
        if (!failed) provider.ProviderQuotas[0].ExpiresAt = Now;

        ProviderQuotaSummaryDto quota = Assert.Single(Map(provider).Quotas);

        Assert.Null(quota.Limit);
        Assert.Null(quota.Remaining);
        Assert.Null(quota.ResetsAt);
    }

    [Fact]
    public void Map_UnknownScope_DoesNotExposeQuotaWindow()
    {
        ProviderStatusDto provider = ProviderWithQuota();
        provider.ProviderQuotas[0].Scope = null;

        Assert.Empty(Map(provider).Quotas);
    }

    [Fact]
    public void Map_Health_ExpiresOrcidReportAndUsesReachabilityForOtherProviders()
    {
        ProviderStatusDto orcid = new()
        {
            Provider = "Orcid",
            Status = "Healthy",
            ReportedHealth = new()
            {
                Status = "Healthy", ObservedAt = Now.AddMinutes(-6), ExpiresAt = Now
            }
        };
        ProviderStatusDto searchApi = new()
        {
            Provider = "SearchApi",
            Status = "Healthy",
            Transport = new() { ObservedAt = Now.AddSeconds(-30), ExpiresAt = Now.AddSeconds(30) }
        };

        ProviderStatusSummaryResponse result = ProviderStatusSummaryMapper.Map(new()
        {
            Providers = [orcid, searchApi]
        }, Now);

        Assert.Equal("Unknown", result.Providers[0].Health);
        Assert.Equal("Reachable", result.Providers[1].Health);
    }

    private static ProviderStatusSummaryDto Map(ProviderStatusDto provider) =>
        Assert.Single(ProviderStatusSummaryMapper.Map(new() { Providers = [provider] }, Now).Providers);

    private static ProviderStatusDto ProviderWithQuota() => new()
    {
        Provider = "SearchApi",
        Status = "Healthy",
        ProviderQuotas = [new()
        {
            Source = "AccountApi", Scope = "account", Unit = "searches", Window = "month",
            Limit = 100, Remaining = 70, ValueKind = "ProviderReported",
            ObservedAt = Now.AddSeconds(-30), ExpiresAt = Now.AddSeconds(30),
            ResetsAt = Now.AddDays(1)
        }],
        RemainingUsage = new()
        {
            Items = [new() { Status = "ProviderReported", Value = 70 }]
        }
    };
}
