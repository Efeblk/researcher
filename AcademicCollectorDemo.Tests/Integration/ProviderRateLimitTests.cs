using System.Diagnostics;
using System.Net;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.RateLimiting;
using AcademicCollectorDemo.Tests.Infrastructure;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class ProviderRateLimitTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task SendAsync_IndependentClients_SharePacingAndDailyBudget()
    {
        List<long> starts = [];
        Stopwatch timer = Stopwatch.StartNew();
        string name = Guid.NewGuid().ToString("N");
        using var first = Client(name, 80, 2, _ => { starts.Add(timer.ElapsedMilliseconds); return new(HttpStatusCode.OK); });
        using var second = Client(name, 80, 2, _ => { starts.Add(timer.ElapsedMilliseconds); return new(HttpStatusCode.OK); });
        using var response1 = await first.GetAsync("https://provider.test/page/1");
        using var response2 = await second.GetAsync("https://provider.test/page/2");
        using var blocked = await second.GetAsync("https://provider.test/page/3");
        Assert.Equal(2, starts.Count);
        Assert.True(starts[1] - starts[0] >= 60);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.NotNull(blocked.Headers.RetryAfter);
    }

    [Fact]
    public async Task SendAsync_RetryAfter_PersistsCooldownAcrossClients()
    {
        string name = Guid.NewGuid().ToString("N");
        using var first = Client(name, 1, 0, _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new(TimeSpan.FromHours(1));
            return response;
        });
        using ProviderCallScope scope = new();
        using var response = await first.GetAsync("https://provider.test/1");
        using var second = Client(name, 1, 0, _ => throw new Exception("Cooldown should prevent this request."));
        using var deferred = await second.GetAsync("https://provider.test/2");
        Assert.Equal(HttpStatusCode.TooManyRequests, deferred.StatusCode);
        Assert.Equal(2, scope.Failures.Count);
        Assert.All(scope.Failures, failure => Assert.True(failure.RetryAt > DateTime.UtcNow.AddMinutes(50)));
    }

    [Fact]
    public async Task SendAsync_ExhaustedProvider_DoesNotConsumeOtherProviderBudget()
    {
        using var first = Client(Guid.NewGuid().ToString("N"), 1, 1, _ => new(HttpStatusCode.OK));
        using var other = Client(Guid.NewGuid().ToString("N"), 1, 1, _ => new(HttpStatusCode.OK));
        using var response1 = await first.GetAsync("https://provider.test/1");
        using var blocked = await first.GetAsync("https://provider.test/2");
        using var unaffected = await other.GetAsync("https://provider.test/1");
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unaffected.StatusCode);
    }

    private HttpClient Client(string name, int interval, int dailyLimit,
        Func<HttpRequestMessage, HttpResponseMessage> respond) => new(new ProviderRateLimitHandler(
            fixture.ConnectionString, [new()
            {
                Name = name, Host = "provider.test", MinimumIntervalMilliseconds = interval,
                DailyRequestLimit = dailyLimit
            }]) { InnerHandler = new StubHttpHandler(respond) });
}
