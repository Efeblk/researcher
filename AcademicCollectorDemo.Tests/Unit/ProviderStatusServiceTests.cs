using System.Net;
using System.Text.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Status;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class ProviderStatusServiceTests
{
    [Fact]
    public void ParseSearchApiQuotas_MissingOrMalformedFields_DoesNotInventRemaining()
    {
        using var document = JsonDocument.Parse("""
            {"account":{"monthly_allowance":"secret","remaining_credits":-1},
             "api_usage":{"hourly_rate_limit":100}}
            """);
        var quotas = ProviderStatusService.ParseSearchApiQuotas(document.RootElement);
        Assert.Equal(2, quotas.Count);
        Assert.All(quotas, quota => Assert.Null(quota.Remaining));
        Assert.Null(quotas[0].Limit);
        Assert.Equal(100, quotas[1].Limit);
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
    }
}
