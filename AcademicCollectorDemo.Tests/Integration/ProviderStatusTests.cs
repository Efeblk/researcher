using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Endpoints;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Status;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.RateLimiting;
using AcademicCollectorDemo.Tests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class ProviderStatusTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task GetAsync_MixedOutcomes_IsolatesFailuresAndCachesConcurrentChecks()
    {
        using StubHttpHandler handler = new(request => request.RequestUri!.Host switch
        {
            "orcid.test" => StubHttpHandler.Json(
                """{"tomcatUp":true,"dbConnectionOk":true,"readOnlyDbConnectionOk":true,"overallOk":true}"""),
            "search.test" => Account(request),
            "openalex.test" => Limited(),
            "wos.test" => new(HttpStatusCode.Unauthorized),
            "yoksis.test" => new(HttpStatusCode.OK) { Content = new StringContent(
                "<definitions xmlns='http://schemas.xmlsoap.org/wsdl/'/>") },
            _ => throw new HttpRequestException("synthetic secret must not be exposed")
        });
        using HttpClient client = new(handler);
        ProviderStatusService service = CreateService(client);
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => service.GetAsync(default)));
        Assert.Equal(6, handler.RequestCount);
        Assert.All(responses, response => Assert.Same(responses[0], response));
        var providers = responses[0].Providers.ToDictionary(provider => provider.Provider);
        Assert.Equal("Healthy", providers["Orcid"].Status);
        Assert.Equal(70, providers["SearchApi"].ProviderQuotas[0].Remaining);
        Assert.Equal("ProviderReported", providers["SearchApi"].RemainingUsage.Status);
        Assert.Equal(70, providers["SearchApi"].RemainingUsage.Items[0].Value);
        Assert.Equal("Derived", providers["SearchApi"].RemainingUsage.Items[1].Status);
        Assert.Equal("RateLimited", providers["OpenAlex"].Status);
        Assert.NotNull(providers["OpenAlex"].RetryAt);
        Assert.Equal("credits", providers["OpenAlex"].ProviderQuotas[0].Unit);
        Assert.Equal("Unauthorized", providers["WebOfScience"].Status);
        Assert.Equal("Unavailable", providers["WebOfScience"].RemainingUsage.Status);
        Assert.Equal("Reachable", providers["Yoksis"].Status);
        Assert.Equal("Unavailable", providers["AnalysisService"].Status);
        Assert.DoesNotContain("synthetic secret", JsonSerializer.Serialize(responses[0]));
    }

    [Fact]
    public async Task GetAsync_MissingCredentials_SkipsProtectedProviders()
    {
        using StubHttpHandler handler = new(_ => StubHttpHandler.Json("{}"));
        using HttpClient client = new(handler);
        ProviderStatusResponse response = await CreateService(client, false).GetAsync(default);
        Assert.Equal(3, handler.RequestCount);
        Assert.Equal(3, response.Providers.Count(provider => provider.Status == "NotConfigured"));
        Assert.All(response.Providers.Where(provider => provider.Status == "NotConfigured"),
            provider => Assert.Equal("Unavailable", provider.RemainingUsage.Status));
        Assert.All(response.Providers.Where(provider => provider.Status != "NotConfigured"),
            provider => Assert.Equal("UnexpectedResponse", provider.Status));
    }

    [Fact]
    public async Task GetAsync_DisabledSearchApi_OverridesMissingCredentialsAndMakesNoRequest()
    {
        List<string> requestedHosts = [];
        using StubHttpHandler handler = new(request =>
        {
            lock (requestedHosts)
                requestedHosts.Add(request.RequestUri!.Host);
            return StubHttpHandler.Json("{}");
        });
        using HttpClient client = new(handler);
        ProviderStatusResponse response = await CreateService(client, false, searchApiEnabled: false)
            .GetAsync(default);

        Assert.Equal("Disabled", response.Providers.Single(provider => provider.Provider == "SearchApi").Status);
        Assert.DoesNotContain("search.test", requestedHosts);
    }

    [Fact]
    public async Task GetAsync_ProviderRequestsCarryOneMegabyteBufferLimit()
    {
        using StubHttpHandler handler = new(request =>
        {
            if (request.RequestUri!.Host == "search.test")
            {
                Assert.True(request.Options.TryGetValue(ProviderRateLimitHandler.ResponseBufferLimit,
                    out long bufferLimit));
                Assert.Equal(1024 * 1024, bufferLimit);
            }
            return request.RequestUri.Host switch
            {
                "orcid.test" => StubHttpHandler.Json(
                    """{"tomcatUp":true,"dbConnectionOk":true,"readOnlyDbConnectionOk":true,"overallOk":true}"""),
                "search.test" => Account(request),
                "openalex.test" => StubHttpHandler.Json("""{"results":[]}"""),
                "wos.test" => StubHttpHandler.Json("""{"metadata":{}}"""),
                "yoksis.test" => new(HttpStatusCode.OK) { Content = new StringContent(
                    "<definitions xmlns='http://schemas.xmlsoap.org/wsdl/'/>") },
                _ => StubHttpHandler.Json("""{"status":"Running"}""")
            };
        });
        using HttpClient client = new(handler);
        await CreateService(client).GetAsync(default);
    }

    [Fact]
    public async Task GetAsync_BudgetRolloverAndCooldown_ReportsSharedSqlState()
    {
        await using SqlConnection connection = new(fixture.ConnectionString);
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM ProviderRequestBudgets WHERE Provider IN ('Orcid', 'OpenAlex');
            INSERT INTO ProviderRequestBudgets VALUES
                ('Orcid', DATEADD(hour, 1, SYSUTCDATETIME()), CAST(SYSUTCDATETIME() AS date), 2),
                ('OpenAlex', SYSUTCDATETIME(), CAST(DATEADD(day, -1, SYSUTCDATETIME()) AS date), 99);
            """;
        await command.ExecuteNonQueryAsync();
        try
        {
            using HttpClient client = new(new StubHttpHandler(_ => StubHttpHandler.Json("{}")));
            var response = await CreateService(client, false).GetAsync(default);
            var orcid = response.Providers.Single(provider => provider.Provider == "Orcid").LocalBudget!;
            Assert.Equal("Exhausted", orcid.Status);
            Assert.Equal(0, orcid.RemainingToday);
            Assert.True(orcid.NextAllowedAt >= orcid.ResetsAt);
            var openAlex = response.Providers.Single(provider => provider.Provider == "OpenAlex").LocalBudget!;
            Assert.Equal(0, openAlex.RequestsToday);
            Assert.Null(openAlex.DailyRequestLimit);
            Assert.Null(openAlex.RemainingToday);
            Assert.Equal("Available", openAlex.Status);
        }
        finally
        {
            command.CommandText = "DELETE FROM ProviderRequestBudgets WHERE Provider IN ('Orcid', 'OpenAlex')";
            await command.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task ProviderStatus_HttpGet_ReturnsSixProvidersAndNoStore()
    {
        using HttpClient upstream = new(new StubHttpHandler(request => request.RequestUri!.Host switch
        {
            "search.test" => Account(request),
            "openalex.test" => OpenAlexWithQuotaHeaders(),
            "wos.test" => WebOfScienceWithQuotaHeaders(),
            _ => StubHttpHandler.Json("{}")
        }));
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(CreateService(upstream));
        builder.Services.AddControllers().AddApplicationPart(typeof(ProviderStatusEndpoint).Assembly);
        await using var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();
        using HttpClient client = new() { BaseAddress = new Uri(app.Urls.Single()) };
        using var response = await client.GetAsync("/Services/AcademicPerformance/V1/ProviderStatus");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        string json = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.Equal(["providers"], root.EnumerateObject().Select(property => property.Name));
        JsonElement[] providers = root.GetProperty("providers").EnumerateArray().ToArray();
        Assert.Equal(6, providers.Length);
        Assert.All(providers, provider => Assert.Equal(
            ["provider", "health", "quotas"],
            provider.EnumerateObject().Select(property => property.Name)));
        JsonElement quota = providers.Single(provider =>
            provider.GetProperty("provider").GetString() == "SearchApi")
            .GetProperty("quotas").EnumerateArray().First();
        Assert.Equal(
            ["limit", "remaining", "unit", "period", "resetsAt"],
            quota.EnumerateObject().Select(property => property.Name));
        Assert.DoesNotContain("message", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reason", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("remainingUsage", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("localBudget", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("checkedAt", json, StringComparison.OrdinalIgnoreCase);

        JsonElement openAlexQuota = Assert.Single(providers.Single(provider =>
            provider.GetProperty("provider").GetString() == "OpenAlex")
            .GetProperty("quotas").EnumerateArray());
        Assert.Equal(900, openAlexQuota.GetProperty("limit").GetDecimal());
        Assert.Equal(321, openAlexQuota.GetProperty("remaining").GetDecimal());
        Assert.Equal("credits", openAlexQuota.GetProperty("unit").GetString());
        Assert.Equal("daily", openAlexQuota.GetProperty("period").GetString());
        Assert.NotEqual(JsonValueKind.Null, openAlexQuota.GetProperty("resetsAt").ValueKind);

        JsonElement[] wosQuotas = providers.Single(provider =>
            provider.GetProperty("provider").GetString() == "WebOfScience")
            .GetProperty("quotas").EnumerateArray().ToArray();
        Assert.Equal(2, wosQuotas.Length);
        Assert.Equal(["daily", "perSecond"], wosQuotas.Select(quota =>
            quota.GetProperty("period").GetString()));
        Assert.Equal([222m, 3m], wosQuotas.Select(quota =>
            quota.GetProperty("remaining").GetDecimal()));
        Assert.All(wosQuotas, quota =>
        {
            Assert.Equal("requests", quota.GetProperty("unit").GetString());
            Assert.Equal(JsonValueKind.Null, quota.GetProperty("resetsAt").ValueKind);
        });
    }

    [Fact]
    public async Task GetAsync_CacheHitAfterQuotaExpiry_RebuildsRemainingUsageWithoutNewRequests()
    {
        using StubHttpHandler handler = new(request => request.RequestUri!.Host switch
        {
            "orcid.test" => StubHttpHandler.Json(
                """{"tomcatUp":true,"dbConnectionOk":true,"readOnlyDbConnectionOk":true,"overallOk":true}"""),
            "search.test" => Account(request),
            "openalex.test" => StubHttpHandler.Json("""{"results":[]}"""),
            "wos.test" => StubHttpHandler.Json("""{"metadata":{}}"""),
            "yoksis.test" => new(HttpStatusCode.OK) { Content = new StringContent(
                "<definitions xmlns='http://schemas.xmlsoap.org/wsdl/'/>") },
            _ => StubHttpHandler.Json("""{"status":"Running"}""")
        });
        using HttpClient client = new(handler);
        ProviderStatusService service = CreateService(client);
        ProviderStatusResponse first = await service.GetAsync(default);
        ProviderStatusDto search = first.Providers.Single(provider => provider.Provider == "SearchApi");
        Assert.Equal(70, search.RemainingUsage.Items[0].Value);
        foreach (ProviderQuotaDto quota in search.ProviderQuotas)
            quota.ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
        int requestsAfterFirst = handler.RequestCount;

        ProviderStatusResponse second = await service.GetAsync(default);

        Assert.Same(first, second);
        Assert.Equal(requestsAfterFirst, handler.RequestCount);
        Assert.Equal("Stale", search.RemainingUsage.Status);
        Assert.Null(search.RemainingUsage.Items[0].Value);
        Assert.Equal(70, search.ProviderQuotas[0].Remaining);
    }

    [Fact]
    public async Task GetAsync_OpenAlexSuffixedHeader_DoesNotPresentDailyAccountBalance()
    {
        using StubHttpHandler handler = new(request =>
        {
            if (request.RequestUri!.Host == "openalex.test")
            {
                HttpResponseMessage response = new(HttpStatusCode.TooManyRequests);
                response.Headers.Add("X-RateLimit-Limit-Second", "10");
                response.Headers.Add("X-RateLimit-Remaining-Second", "9");
                return response;
            }
            return StubHttpHandler.Json("{}");
        });
        using HttpClient client = new(handler);

        ProviderStatusResponse response = await CreateService(client, openAlexKey: true).GetAsync(default);
        ProviderStatusDto openAlex = response.Providers.Single(provider => provider.Provider == "OpenAlex");

        ProviderQuotaDto quota = Assert.Single(openAlex.ProviderQuotas);
        Assert.Equal("second", quota.Window);
        Assert.Equal("unknown", quota.Unit);
        Assert.Null(quota.Scope);
        Assert.Equal("Unknown", openAlex.RemainingUsage.Status);
        Assert.Null(openAlex.RemainingUsage.Items[0].Value);
    }

    [Theory]
    [InlineData(false, "RateLimited")]
    [InlineData(true, "LocallyLimited")]
    public async Task GetAsync_LocalAndRemote429_DistinguishesBudgetSource(bool local, string expected)
    {
        using HttpClient client = new(new StubHttpHandler(_ =>
        {
            var response = Limited();
            if (local) response.Headers.Add("X-Academic-Local-Deferral", "true");
            return response;
        }));
        var result = await CreateService(client, false).GetAsync(default);
        Assert.Equal(expected, result.Providers.Single(provider => provider.Provider == "Orcid").Status);
    }

    [Fact]
    public async Task GetAsync_CallerCancelled_PropagatesCancellation()
    {
        using HttpClient client = new(new StubHttpHandler(_ => throw new Exception("Should not send.")));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateService(client).GetAsync(cancellation.Token));
    }

    private ProviderStatusService CreateService(HttpClient client, bool credentials = true,
        bool openAlexKey = false, bool searchApiEnabled = true)
    {
        Dictionary<string, string?> settings = new()
        {
            ["ConnectionStrings:AcademicDatabase"] = fixture.ConnectionString,
            ["Orcid:ApiBaseUrl"] = "https://orcid.test/v3.0/" + Guid.NewGuid().ToString("N"),
            ["SearchApi:ApiBaseUrl"] = "https://search.test/api/v1/search",
            ["OpenAlex:ApiBaseUrl"] = "https://openalex.test",
            ["WebOfScience:ApiBaseUrl"] = "https://wos.test/v1",
            ["Yoksis:ServiceUrl"] = "https://yoksis.test/ws",
            ["AnalysisService:BaseUrl"] = "https://analysis.test",
            ["ProviderRequestLimits:Orcid:DailyRequestLimit"] = "2"
        };
        settings["ProviderRequestLimits:SearchApi:Enabled"] = searchApiEnabled.ToString();
        if (credentials)
            foreach (string key in new[] { "SearchApi:ApiKey", "WebOfScience:ApiKey", "Yoksis:Username", "Yoksis:Password" })
                settings[key] = "synthetic";
        if (openAlexKey) settings["OpenAlex:ApiKey"] = "synthetic";
        return new(client, new ClientFactory(client), new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
    }

    private static HttpResponseMessage Account(HttpRequestMessage request)
    {
        Assert.Equal("/api/v1/me", request.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        return StubHttpHandler.Json("""
            {"account":{"monthly_allowance":100,"current_month_usage":30,"remaining_credits":70},
             "api_usage":{"hourly_rate_limit":10,"searches_this_hour":4}}
            """);
    }

    private static HttpResponseMessage OpenAlexWithQuotaHeaders()
    {
        HttpResponseMessage response = StubHttpHandler.Json("""{"results":[]}""");
        response.Headers.Add("X-RateLimit-Limit", "900");
        response.Headers.Add("X-RateLimit-Remaining", "321");
        response.Headers.Add("X-RateLimit-Reset", "3600");
        return response;
    }

    private static HttpResponseMessage WebOfScienceWithQuotaHeaders()
    {
        HttpResponseMessage response = StubHttpHandler.Json("""{"metadata":{}}""");
        response.Headers.Add("X-RateLimit-Limit-Day", "700");
        response.Headers.Add("X-RateLimit-Remaining-Day", "222");
        response.Headers.Add("X-RateLimit-Limit-Second", "8");
        response.Headers.Add("X-RateLimit-Remaining-Second", "3");
        response.Headers.Add("X-RateLimit-Limit", "11");
        response.Headers.Add("X-RateLimit-Remaining", "7");
        response.Headers.Add("X-RateLimit-Reset", "2");
        return response;
    }

    private static HttpResponseMessage Limited()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new(TimeSpan.FromMinutes(2));
        response.Headers.Add("X-RateLimit-Remaining", "0");
        return response;
    }

    // The service owns factory-created clients, so each gets its own wrapper.
    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new ForwardHandler(client));
    }

    private sealed class ForwardHandler(HttpClient client) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using HttpRequestMessage copy = new(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
                copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
            return await client.SendAsync(copy, cancellationToken);
        }
    }
}
