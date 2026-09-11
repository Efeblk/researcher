using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Endpoints;

public static class ProviderStatusSummaryMapper
{
    public static ProviderStatusSummaryResponse Map(ProviderStatusResponse source, DateTime now) => new()
    {
        Providers = source.Providers.Select(provider => MapProvider(provider, now)).ToList()
    };

    private static ProviderStatusSummaryDto MapProvider(ProviderStatusDto provider, DateTime now) => new()
    {
        Provider = provider.Provider,
        Health = MapHealth(provider, now),
        Quotas = provider.ProviderQuotas
            .Select((quota, index) => MapQuota(provider, quota, index, now))
            .Where(quota => quota is not null)
            .Cast<ProviderQuotaSummaryDto>()
            .ToList(),
        Spending = provider.Provider == "Gemini" && provider.Spending is not null
            ? MapSpending(provider.Spending) : null
    };

    private static ProviderSpendingSummaryDto MapSpending(ProviderSpendingDto spending) => new()
    {
        Available = spending.Available,
        Currency = spending.Currency,
        Kind = spending.Kind,
        Since = spending.Since,
        RequestCount = spending.RequestCount,
        UnknownCount = spending.UnknownCount,
        EstimatedTotalUsd = spending.EstimatedTotalUsd,
        Last3 = spending.Last3.Select(item => new ProviderSpendingItemSummaryDto
        {
            At = item.At, Model = item.Model, EstimatedUsd = item.EstimatedUsd
        }).ToList()
    };

    private static string MapHealth(ProviderStatusDto provider, DateTime now)
    {
        if (provider.Status == "Disabled")
            return "Disabled";

        if (provider.Provider == "Orcid" && provider.ReportedHealth is { } reported)
        {
            if (reported.ObservedAt <= now && reported.ExpiresAt > now)
                return reported.Status is "Healthy" or "Unhealthy" ? reported.Status : "Unknown";
            return "Unknown";
        }

        if (provider.Transport.ObservedAt > now || provider.Transport.ExpiresAt <= now)
            return "Unknown";

        return provider.Status switch
        {
            "Healthy" or "Reachable" => "Reachable",
            "Unauthorized" => "Unauthorized",
            "RateLimited" => "RateLimited",
            "Unavailable" or "Timeout" => "Unavailable",
            _ => "Unknown"
        };
    }

    private static ProviderQuotaSummaryDto? MapQuota(ProviderStatusDto provider, ProviderQuotaDto quota,
        int index, DateTime now)
    {
        if (!HasVerifiedProviderScope(provider.Provider, quota) ||
            string.IsNullOrWhiteSpace(quota.Unit) || quota.Unit == "unknown" ||
            string.IsNullOrWhiteSpace(quota.Window) || quota.Window == "unspecified")
            return null;

        bool current = quota.ObservedAt.HasValue && quota.ExpiresAt.HasValue &&
            quota.ObservedAt <= now && quota.ExpiresAt > now &&
            (!quota.ResetsAt.HasValue || quota.ResetsAt > now);
        bool observationUsable = provider.Status is "Healthy" or "Reachable" or "RateLimited" && current;
        ProviderRemainingUsageItemDto? usage = index < provider.RemainingUsage.Items.Count
            ? provider.RemainingUsage.Items[index]
            : null;

        return new()
        {
            Limit = observationUsable ? quota.Limit : null,
            Remaining = observationUsable && usage?.Status is "ProviderReported" or "Derived"
                ? usage.Value
                : null,
            Unit = quota.Unit,
            Period = MapPeriod(quota.Window),
            ResetsAt = observationUsable ? quota.ResetsAt : null
        };
    }

    private static bool HasVerifiedProviderScope(string provider, ProviderQuotaDto quota)
    {
        bool recognizedAnonymousScope = provider == "OpenAlex" && quota.Scope == "anonymous";
        bool recognizedRequestPool = provider == "Crossref" && quota.Scope == "request-pool";
        if (quota.Scope is not ("account" or "api-key") && !recognizedAnonymousScope && !recognizedRequestPool ||
            quota.ValueKind is not ("ProviderReported" or "DerivedFromProviderValues"))
            return false;
        if (quota.Source == "AccountApi")
            return true;
        if (quota.Source != "ResponseHeaders")
            return false;
        return (provider == "OpenAlex" && quota.Scope is "api-key" or "anonymous" &&
                quota.SourceFields is "X-RateLimit-Limit,X-RateLimit-Remaining" or
                    "X-RateLimit-Limit,X-RateLimit-Remaining,X-RateLimit-Reset") ||
            (provider == "WebOfScience" && quota.Scope == "api-key" &&
                quota.SourceFields is "X-RateLimit-Limit-Day,X-RateLimit-Remaining-Day" or
                    "X-RateLimit-Limit-Second,X-RateLimit-Remaining-Second") ||
            (provider == "Crossref" && quota.Scope == "request-pool" &&
                quota.SourceFields == "X-Rate-Limit-Limit,X-Rate-Limit-Interval");
    }

    private static string MapPeriod(string window) => window.ToLowerInvariant() switch
    {
        "second" => "perSecond",
        "minute" => "perMinute",
        "hour" => "hourly",
        "day" => "daily",
        "week" => "weekly",
        "month" => "monthly",
        "year" => "yearly",
        _ => window
    };
}
