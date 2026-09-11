using System.Net;
using System.Text;
using System.Security.Cryptography;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Status;
using AcademicCollectorDemo.Tests.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class ProviderStatusServiceTests(SqlServerFixture fixture)
{
    [Fact]
    public void CreateRequest_OpenAlexKey_UsesDedicatedPathAndBearerWithoutQuerySecret()
    {
        IConfiguration configuration = Configuration(fixture.ConnectionString);
        using HttpRequestMessage request = Service(new CountingHandler(), configuration)
            .CreateRequest("OpenAlex", "https://api.openalex.org");
        Assert.Equal("/rate-limit", request.RequestUri!.AbsolutePath);
        Assert.Empty(request.RequestUri.Query);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("synthetic-openalex-key", request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task CheckOrcidAsync_ConcurrentInstances_UseOneProbeAndSharedFiveMinuteResult()
    {
        CountingHandler handler = new();
        IConfiguration configuration = Configuration(fixture.ConnectionString);
        ProviderStatusService first = Service(handler, configuration);
        ProviderStatusService second = Service(handler, configuration);
        string baseUrl = "https://orcid.test/v3.0/" + Guid.NewGuid().ToString("N");

        var results = await Task.WhenAll(
            first.CheckOrcidAsync(baseUrl, CancellationToken.None),
            second.CheckOrcidAsync(baseUrl, CancellationToken.None));

        Assert.Equal(1, handler.RequestCount);
        Assert.Null(handler.Authorization);
        Assert.EndsWith("/pubStatus", handler.Path);
        Assert.All(results, result =>
        {
            Assert.Equal("Healthy", result.ReportedHealth!.Status);
            Assert.Equal("SqlDeployment", result.CacheScope);
            Assert.True(result.Transport.ExpiresAt > result.CheckedAt);
        });
    }

    [Fact]
    public async Task CheckOrcidAsync_CallerCancellationAfterReservation_PreventsImmediateRetry()
    {
        using CancellationTokenSource cancellation = new();
        CancellingHandler firstHandler = new(cancellation);
        IConfiguration configuration = Configuration(fixture.ConnectionString);
        ProviderStatusService first = Service(firstHandler, configuration);
        string baseUrl = "https://cancelled-orcid.test/v3.0/" + Guid.NewGuid().ToString("N");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.CheckOrcidAsync(
            baseUrl, cancellation.Token));

        CountingHandler secondHandler = new();
        ProviderStatusService second = Service(secondHandler, configuration);
        var result = await second.CheckOrcidAsync(baseUrl, CancellationToken.None);
        Assert.Equal("LocalCoordinationPending", result.Status);
        Assert.NotNull(result.RetryAt);
        Assert.Equal(0, secondHandler.RequestCount);
    }

    [Fact]
    public async Task CheckOrcidAsync_IncompleteCachedPayload_FailsClosedUntilExpiry()
    {
        string baseUrl = "https://corrupt-orcid.test/v3.0/" + Guid.NewGuid().ToString("N");
        string endpoint = baseUrl + "/pubStatus";
        Uri uri = new(endpoint);
        string key = "Orcid:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.SafeUnescaped).ToLowerInvariant() +
            uri.PathAndQuery)))[..32];
        await using (var connection = new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO [integrations].[ProviderStatusObservations] VALUES (@provider,SYSUTCDATETIME(),DATEADD(minute,5,SYSUTCDATETIME()),'{\"Provider\":\"Orcid\",\"Status\":\"Healthy\"}')";
            command.Parameters.AddWithValue("@provider", key);
            await command.ExecuteNonQueryAsync();
        }
        CountingHandler handler = new();
        var result = await Service(handler, Configuration(fixture.ConnectionString))
            .CheckOrcidAsync(baseUrl, CancellationToken.None);
        Assert.Equal("LocalCoordinationPending", result.Status);
        Assert.Equal(0, handler.RequestCount);
    }

    private static IConfiguration Configuration(string connectionString) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:AcademicDatabase"] = connectionString,
              ["Orcid:AccessToken"] = "must-not-be-sent",
              ["OpenAlex:ApiKey"] = "synthetic-openalex-key" }).Build();

    private static ProviderStatusService Service(HttpMessageHandler handler, IConfiguration configuration) =>
        new(new HttpClient(handler), new UnusedClientFactory(), configuration);

    private sealed class CountingHandler : HttpMessageHandler
    {
        private int _requestCount;
        public int RequestCount => _requestCount;
        public System.Net.Http.Headers.AuthenticationHeaderValue? Authorization { get; private set; }
        public string? Path { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            Authorization = request.Headers.Authorization;
            Path = request.RequestUri?.AbsolutePath;
            await Task.Delay(50, cancellationToken);
            return new(HttpStatusCode.OK) { Content = new StringContent(
                """{"tomcatUp":true,"dbConnectionOk":true,"readOnlyDbConnectionOk":true,"overallOk":true}""",
                Encoding.UTF8, "application/json") };
        }
    }

    private sealed class CancellingHandler(CancellationTokenSource cancellation) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            return Task.FromCanceled<HttpResponseMessage>(cancellationToken);
        }
    }

    private sealed class UnusedClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException();
    }
}
