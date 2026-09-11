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
    public void Map_CrossrefOfficialRateWindow_ExposesLimitWithoutRemaining()
    {
        ProviderStatusDto provider = ProviderWithQuota();
        provider.Provider = "Crossref";
        provider.ProviderQuotas[0].Source = "ResponseHeaders";
        provider.ProviderQuotas[0].Scope = "request-pool";
        provider.ProviderQuotas[0].Unit = "requests";
        provider.ProviderQuotas[0].Window = "second";
        provider.ProviderQuotas[0].SourceFields = "X-Rate-Limit-Limit,X-Rate-Limit-Interval";
        provider.ProviderQuotas[0].Limit = 5;
        provider.ProviderQuotas[0].Remaining = null;
        provider.RemainingUsage.Items[0].Status = "Unknown";
        provider.RemainingUsage.Items[0].Value = null;

        ProviderQuotaSummaryDto quota = Assert.Single(Map(provider).Quotas);

        Assert.Equal(5, quota.Limit);
        Assert.Null(quota.Remaining);
        Assert.Equal("perSecond", quota.Period);
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

    [Fact]
    public void Map_DisabledProvider_PreservesDisabledWithoutTransportObservation()
    {
        ProviderStatusDto provider = new()
        {
            Provider = "SearchApi",
            Status = "Disabled",
            Transport = new() { Status = "Disabled" }
        };

        Assert.Equal("Disabled", Map(provider).Health);
    }

    [Fact]
    public void Map_GeminiSpending_PreservesOnlySanitizedAggregate()
    {
        ProviderStatusDto provider = new()
        {
            Provider = "Gemini",
            Status = "Healthy",
            Transport = new() { ObservedAt = Now.AddSeconds(-1), ExpiresAt = Now.AddSeconds(30) },
            Spending = new()
            {
                Available = true, Since = Now.AddDays(-1), RequestCount = 2, UnknownCount = 0,
                EstimatedTotalUsd = 0.003m,
                Last3 = [new() { At = Now, Model = "gemini-3.8-flash", EstimatedUsd = 0.002m }]
            }
        };

        ProviderSpendingSummaryDto spending = Map(provider).Spending!;

        Assert.True(spending.Available);
        Assert.Equal("USD", spending.Currency);
        Assert.Equal("paidStandardEstimate", spending.Kind);
        Assert.Equal(2, spending.RequestCount);
        Assert.Equal(0.003m, spending.EstimatedTotalUsd);
        Assert.Equal("gemini-3.8-flash", Assert.Single(spending.Last3).Model);
    }

    [Fact]
    public void Map_NonGeminiSpending_DoesNotExposeAggregate()
    {
        ProviderStatusDto provider = ProviderWithQuota();
        provider.Spending = new() { Available = true, EstimatedTotalUsd = 12m };

        Assert.Null(Map(provider).Spending);
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
