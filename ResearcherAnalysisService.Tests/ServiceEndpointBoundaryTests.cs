using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ResearcherAnalysisService.Tests;

public sealed class ServiceEndpointBoundaryTests
{
    private const string ServiceKey = "synthetic-boundary-key";

    [Fact]
    public async Task ProductRoutes_ResideInAnalysisAndRejectMissingOrWrongServiceKey()
    {
        await using BoundaryHost host = await BoundaryHost.StartAsync(UnreachableDatabaseConnectionString());
        IReadOnlyList<RouteEndpoint> routes = host.ProductRoutes;

        Assert.Equal(25, routes.Count);
        Assert.All(routes, route =>
        {
            ControllerActionDescriptor action = Assert.IsType<ControllerActionDescriptor>(
                route.Metadata.GetMetadata<ControllerActionDescriptor>());
            Assert.Equal(typeof(ResearcherAnalysisService.Program).Assembly, action.ControllerTypeInfo.Assembly);
            Assert.Equal(["POST"],
                route.Metadata.GetRequiredMetadata<IHttpMethodMetadata>().HttpMethods);
        });

        foreach (RouteEndpoint route in routes)
        {
            string path = "/" + route.RoutePattern.RawText;
            using HttpResponseMessage missing = await host.SendAsync(path, key: null);
            Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);

            using HttpResponseMessage wrong = await host.SendAsync(path, key: "wrong-boundary-key");
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }
    }

    [Fact]
    public async Task Health_StartsWithoutCollectorSource()
    {
        await using BoundaryHost host = await BoundaryHost.StartAsync(UnreachableDatabaseConnectionString());

        using HttpResponseMessage response = await host.Client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("ResearcherAnalysisService", await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SourceEndpoint_MissingCollectorSchema_ReturnsServiceUnavailable()
    {
        await using EmptySqlDatabase database = await EmptySqlDatabase.CreateAsync();
        await using BoundaryHost host = await BoundaryHost.StartAsync(database.ConnectionString);

        using HttpResponseMessage response = await host.SendAsync(
            "/api/v1/products/GetResearcherSourceCoverage", ServiceKey,
            "{\"personelId\":\"missing-source\"}");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private static string UnreachableDatabaseConnectionString() =>
        "Server=127.0.0.1,1;Database=BoundaryMustNotConnect;User ID=boundary;" +
        "Password=synthetic-only;Encrypt=true;TrustServerCertificate=true;Connect Timeout=1";

    private sealed class BoundaryHost(WebApplication application, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public IReadOnlyList<RouteEndpoint> ProductRoutes => application.Services
            .GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith(
                "api/v1/products/", StringComparison.OrdinalIgnoreCase) == true)
            .OrderBy(endpoint => endpoint.RoutePattern.RawText, StringComparer.Ordinal)
            .ToArray();

        public static async Task<BoundaryHost> StartAsync(string connectionString)
        {
            WebApplication application = ResearcherAnalysisService.Program.CreateApplication(
                ["--environment", "Testing"], builder =>
                {
                    builder.Configuration.Sources.Clear();
                    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Urls"] = "http://127.0.0.1:0",
                        ["ConnectionStrings:UsageDatabase"] = connectionString,
                        ["DatabaseMigrations:Enabled"] = "false",
                        ["Service:ApiKey"] = ServiceKey,
                        ["CollectionChanges:WorkerEnabled"] = "false",
                        ["ArticleSummaryAutomation:Enabled"] = "false",
                        ["ArticleSummaryAutomation:WorkerEnabled"] = "false",
                        ["PublicationMetrics:WorkerEnabled"] = "false",
                        ["ArticleEvaluation:WorkerEnabled"] = "false",
                        ["FacultyAssistant:WorkerEnabled"] = "false"
                    });
                    builder.Logging.ClearProviders();
                });
            await application.StartAsync();
            string address = application.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            HttpClient client = new()
            {
                BaseAddress = new Uri(address),
                Timeout = TimeSpan.FromSeconds(10)
            };
            return new BoundaryHost(application, client);
        }

        public async Task<HttpResponseMessage> SendAsync(string path, string? key,
            string body = "{}")
        {
            using HttpRequestMessage request = new(HttpMethod.Post, path)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            if (key is not null)
                request.Headers.Add("X-Analysis-Key", key);
            return await Client.SendAsync(request);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await application.StopAsync();
            await application.DisposeAsync();
        }
    }

    private sealed class EmptySqlDatabase : IAsyncDisposable
    {
        private readonly string _databaseName;
        private readonly string _masterConnectionString;
        public string ConnectionString { get; }

        private EmptySqlDatabase(string databaseName, string masterConnectionString,
            string connectionString)
        {
            _databaseName = databaseName;
            _masterConnectionString = masterConnectionString;
            ConnectionString = connectionString;
        }

        public static async Task<EmptySqlDatabase> CreateAsync()
        {
            string databaseName = "ServiceEndpointBoundary_" + Guid.NewGuid().ToString("N");
            SqlConnectionStringBuilder connection = new(
                Environment.GetEnvironmentVariable("ACADEMIC_TEST_SQLSERVER") ??
                @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;Encrypt=true;TrustServerCertificate=true")
            {
                Encrypt = SqlConnectionEncryptOption.Mandatory,
                InitialCatalog = "master",
                Pooling = false
            };
            string masterConnectionString = connection.ConnectionString;
            await using (SqlConnection master = new(masterConnectionString))
            {
                await master.OpenAsync();
                await using SqlCommand create = master.CreateCommand();
                create.CommandText = $"CREATE DATABASE [{databaseName}]";
                await create.ExecuteNonQueryAsync();
            }

            connection.InitialCatalog = databaseName;
            return new EmptySqlDatabase(databaseName, masterConnectionString,
                connection.ConnectionString);
        }

        public async ValueTask DisposeAsync()
        {
            await using SqlConnection master = new(_masterConnectionString);
            await master.OpenAsync();
            await using SqlCommand drop = master.CreateCommand();
            drop.CommandText = $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                $"DROP DATABASE [{_databaseName}]";
            await drop.ExecuteNonQueryAsync();
        }
    }
}
