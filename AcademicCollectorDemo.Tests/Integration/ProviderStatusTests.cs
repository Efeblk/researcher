using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Endpoints;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Status;
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
        Assert.Equal("RateLimited", providers["OpenAlex"].Status);
        Assert.NotNull(providers["OpenAlex"].RetryAt);
        Assert.Equal("credits", providers["OpenAlex"].ProviderQuotas[0].Unit);
        Assert.Equal("Unauthorized", providers["WebOfScience"].Status);
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
        Assert.All(response.Providers.Where(provider => provider.Status != "NotConfigured"),
            provider => Assert.Equal("UnexpectedResponse", provider.Status));
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
        using HttpClient upstream = new(new StubHttpHandler(_ => StubHttpHandler.Json("{}")));
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(CreateService(upstream, false));
        builder.Services.AddControllers().AddApplicationPart(typeof(ProviderStatusEndpoint).Assembly);
        await using var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();
        using HttpClient client = new() { BaseAddress = new Uri(app.Urls.Single()) };
        using var response = await client.GetAsync("/Services/AcademicPerformance/V1/ProviderStatus");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadFromJsonAsync<ProviderStatusResponse>();
        Assert.Equal(6, body!.Providers.Count);
        Assert.True(body.ExpiresAt > body.CheckedAt);
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

    private ProviderStatusService CreateService(HttpClient client, bool credentials = true)
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
        if (credentials)
            foreach (string key in new[] { "SearchApi:ApiKey", "WebOfScience:ApiKey", "Yoksis:Username", "Yoksis:Password" })
                settings[key] = "synthetic";
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
