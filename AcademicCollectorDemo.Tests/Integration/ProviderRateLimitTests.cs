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
        Task<HttpResponseMessage> request1 = first.GetAsync("https://provider.test/page/1");
        Task<HttpResponseMessage> request2 = second.GetAsync("https://provider.test/page/2");
        HttpResponseMessage[] responses = await Task.WhenAll(request1, request2);
        using HttpResponseMessage response1 = responses[0];
        using HttpResponseMessage response2 = responses[1];
        using var blocked = await second.GetAsync("https://provider.test/page/3");
        Assert.Equal(2, starts.Count);
        Assert.True(starts[1] - starts[0] >= 60);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.NotNull(blocked.Headers.RetryAfter);
    }

    [Theory]
    [InlineData(429, false)]
    [InlineData(429, true)]
    [InlineData(503, false)]
    [InlineData(503, true)]
    public async Task SendAsync_RetryAfter_PersistsCooldownAcrossClients(int statusCode, bool useDate)
    {
        string name = Guid.NewGuid().ToString("N");
        using var first = Client(name, 1, 0, _ =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)statusCode);
            response.Headers.RetryAfter = useDate
                ? new(DateTimeOffset.UtcNow.AddHours(1))
                : new(TimeSpan.FromHours(1));
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

    [Fact]
    public async Task SendAsync_TransportFailure_PersistsCooldownAcrossClients()
    {
        string name = Guid.NewGuid().ToString("N");
        int requests = 0;
        using var first = AsyncClient(name, 1, 0, (_, _) =>
        {
            requests++;
            throw new HttpRequestException("Synthetic connection failure.");
        });
        await Assert.ThrowsAsync<HttpRequestException>(() => first.GetAsync("https://provider.test/1"));

        using var second = AsyncClient(name, 1, 0, (_, _) =>
        {
            requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        using var deferred = await second.GetAsync("https://provider.test/2");
        Assert.Equal(HttpStatusCode.TooManyRequests, deferred.StatusCode);
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task SendAsync_TimeoutAfterDispatch_PersistsCooldownAndReleasesLock()
    {
        string name = Guid.NewGuid().ToString("N");
        int requests = 0;
        TaskCompletionSource dispatched = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var first = AsyncClient(name, 1, 0, async (_, cancellationToken) =>
        {
            requests++;
            dispatched.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new(HttpStatusCode.OK);
        });
        using var timeout = new CancellationTokenSource();
        Task<HttpResponseMessage> pending = first.GetAsync("https://provider.test/1", timeout.Token);
        await dispatched.Task.WaitAsync(TimeSpan.FromSeconds(5));
        timeout.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);

        using var second = AsyncClient(name, 1, 0, (_, _) =>
        {
            requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        using var deferred = await second.GetAsync("https://provider.test/2");
        Assert.Equal(HttpStatusCode.TooManyRequests, deferred.StatusCode);
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task SendAsync_ResponseBodyFailure_PersistsCooldownAcrossClients()
    {
        string name = Guid.NewGuid().ToString("N");
        int requests = 0;
        using var first = AsyncClient(name, 1, 0, (_, _) =>
        {
            requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new FailingContent()
            });
        });
        await Assert.ThrowsAsync<HttpRequestException>(() => first.GetAsync("https://provider.test/1"));

        using var second = AsyncClient(name, 1, 0, (_, _) =>
        {
            requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        using var deferred = await second.GetAsync("https://provider.test/2");
        Assert.Equal(HttpStatusCode.TooManyRequests, deferred.StatusCode);
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task SendAsync_CancellationDuringBodyRead_PersistsCooldownAndReleasesLock()
    {
        string name = Guid.NewGuid().ToString("N");
        TaskCompletionSource bodyReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var first = AsyncClient(name, 1, 0, (_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new HangingContent(bodyReadStarted)
            }));
        using var cancellation = new CancellationTokenSource();
        Task<HttpResponseMessage> pending = first.GetAsync("https://provider.test/1", cancellation.Token);
        await bodyReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);

        using var second = AsyncClient(name, 1, 0, (_, _) =>
            throw new Exception("Body-failure cooldown should prevent this request."));
        using var deferred = await second.GetAsync("https://provider.test/2");
        Assert.Equal(HttpStatusCode.TooManyRequests, deferred.StatusCode);
    }

    [Fact]
    public async Task SendAsync_RetryAfterHeadersThenBodyFailure_PreservesLongCooldown()
    {
        string name = Guid.NewGuid().ToString("N");
        using var first = AsyncClient(name, 1, 0, (_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new FailingContent()
            };
            response.Headers.RetryAfter = new(TimeSpan.FromHours(1));
            return Task.FromResult(response);
        });
        using ProviderCallScope scope = new();
        await Assert.ThrowsAsync<HttpRequestException>(() => first.GetAsync("https://provider.test/1"));

        using var second = AsyncClient(name, 1, 0, (_, _) =>
            throw new Exception("Long cooldown should prevent this request."));
        using var deferred = await second.GetAsync("https://provider.test/2");
        Assert.Equal(HttpStatusCode.TooManyRequests, deferred.StatusCode);
        Assert.All(scope.Failures, failure => Assert.True(failure.RetryAt > DateTime.UtcNow.AddMinutes(50)));
    }

    [Fact]
    public async Task SendAsync_PreDispatchCancellation_ReleasesLockWithoutCooldown()
    {
        string name = Guid.NewGuid().ToString("N");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        int canceledRequests = 0;
        using var first = AsyncClient(name, 1, 0, (_, _) =>
        {
            canceledRequests++;
            throw new Exception("Canceled request must not reach the provider.");
        });
        using (ProviderCallScope scope = new())
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                first.GetAsync("https://provider.test/1", canceled.Token));
            Assert.Empty(scope.Failures);
            Assert.Equal(0, scope.RequestsStarted);
        }
        Assert.Equal(0, canceledRequests);

        int requests = 0;
        using var second = AsyncClient(name, 1, 0, (_, _) =>
        {
            requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        using var response = await second.GetAsync("https://provider.test/2");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task SendAsync_DisabledProvider_DoesNotCallUpstreamOrConsumeBudget()
    {
        string name = Guid.NewGuid().ToString("N");
        int requests = 0;
        using var disabled = Client(name, 1, 1, _ =>
        {
            requests++;
            return new(HttpStatusCode.OK);
        }, false);
        using var blocked = await disabled.GetAsync("https://provider.test/disabled");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, blocked.StatusCode);
        Assert.True(blocked.Headers.Contains("X-Academic-Provider-Disabled"));

        using var enabled = Client(name, 1, 1, _ =>
        {
            requests++;
            return new(HttpStatusCode.OK);
        });
        using var response = await enabled.GetAsync("https://provider.test/enabled");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task SendAsync_ResponseBufferLimit_RejectsOversizedBodyBeforeReturning()
    {
        string name = Guid.NewGuid().ToString("N");
        using var client = Client(name, 1, 0, _ => new(HttpStatusCode.OK)
        {
            Content = new StringContent(new string('x', 1025))
        });
        using HttpRequestMessage request = new(HttpMethod.Get, "https://provider.test/oversized");
        request.Options.Set(ProviderRateLimitHandler.ResponseBufferLimit, 1024);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.SendAsync(request));
    }

    private HttpClient Client(string name, int interval, int dailyLimit,
        Func<HttpRequestMessage, HttpResponseMessage> respond, bool enabled = true) => new(new ProviderRateLimitHandler(
            fixture.ConnectionString, [new()
            {
                Enabled = enabled,
                Name = name, Host = "provider.test", MinimumIntervalMilliseconds = interval,
                DailyRequestLimit = dailyLimit
            }]) { InnerHandler = new StubHttpHandler(respond) });

    private HttpClient AsyncClient(string name, int interval, int dailyLimit,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) =>
        new(new ProviderRateLimitHandler(fixture.ConnectionString, [new()
        {
            Name = name, Host = "provider.test", MinimumIntervalMilliseconds = interval,
            DailyRequestLimit = dailyLimit
        }]) { InnerHandler = new AsyncStubHttpHandler(respond) });

    private sealed class AsyncStubHttpHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
    }

    private sealed class FailingContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            Task.FromException(new HttpRequestException("Synthetic response body failure."));

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class HangingContent(TaskCompletionSource bodyReadStarted) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new NotSupportedException();

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context,
            CancellationToken cancellationToken)
        {
            bodyReadStarted.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
