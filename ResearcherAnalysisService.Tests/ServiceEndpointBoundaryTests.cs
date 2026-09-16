using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
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
    [Fact]
    public async Task AnalysisApiRoutes_NoServiceKey_InNonDevelopmentHostAreReachable()
    {
        await using BoundaryHost host = await BoundaryHost.StartAsync(UnreachableDatabaseConnectionString());
        IReadOnlyList<RouteEndpoint> routes = host.AnalysisApiRoutes;

        Assert.Equal(24, routes.Count);
        Assert.Equal(22, host.ProductRoutes.Count);

        using HttpResponseMessage response = await host.SendAsync(
            "/api/v1/evaluations/profiles", method: "GET");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ProductRoutes_ResideInAnalysisAndPreserveSubjectAuthorization()
    {
        await using BoundaryHost host = await BoundaryHost.StartAsync(UnreachableDatabaseConnectionString());
        IReadOnlyList<RouteEndpoint> routes = host.ProductRoutes;

        Assert.Equal(22, routes.Count);
        Assert.Equal(22, routes.Select(route => route.RoutePattern.RawText)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(routes, route =>
        {
            Assert.Matches("^api/v1/[a-z0-9/-]+$", route.RoutePattern.RawText!);
            Assert.DoesNotContain("/products/", route.RoutePattern.RawText!, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("[action]", route.RoutePattern.RawText, StringComparison.OrdinalIgnoreCase);
            ControllerActionDescriptor action = Assert.IsType<ControllerActionDescriptor>(
                route.Metadata.GetMetadata<ControllerActionDescriptor>());
            Assert.Equal(typeof(ResearcherAnalysisService.Program).Assembly, action.ControllerTypeInfo.Assembly);
            Assert.Equal(["POST"],
                route.Metadata.GetRequiredMetadata<IHttpMethodMetadata>().HttpMethods);
        });

        using HttpResponseMessage response = await host.SendAsync(
            "/api/v1/knowledge/search", body: "{\"personelId\":\"P-1001\",\"query\":\"test\"}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RemovedRoutes_ReturnNotFound()
    {
        await using BoundaryHost host = await BoundaryHost.StartAsync(UnreachableDatabaseConnectionString());
        string[] removed =
        [
            "/api/v1/analyze", "/api/v1/articles/summarize", "/api/v1/articles/review",
            "/api/v1/articles/review/stages/quote",
            "/api/v1/articles/review/stages/generate", "/api/v1/articles/review/stages/verify",
            "/api/v1/faculty-assistant", "/api/v1/evaluations/execute",
            "/api/v1/researchers/coverage", "/api/v1/articles/summary/status",
            "/api/v1/articles/evidence"
        ];

        foreach (string path in removed)
        {
            using HttpResponseMessage response = await host.SendAsync(path);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound,
                $"{path} returned {(int)response.StatusCode}.");
        }

        using HttpResponseMessage configuration = await host.SendAsync(
            "/api/v1/articles/review/configuration", method: "GET");
        Assert.Equal(HttpStatusCode.NotFound, configuration.StatusCode);
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
            "/api/v1/researchers/analysis",
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
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?
                .ControllerTypeInfo.Namespace == "ResearcherAnalysisService.Products.Api.Controllers")
            .OrderBy(endpoint => endpoint.RoutePattern.RawText, StringComparer.Ordinal)
            .ToArray();

        public IReadOnlyList<RouteEndpoint> AnalysisApiRoutes => application.Services
            .GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith(
                "api/v1/", StringComparison.OrdinalIgnoreCase) == true)
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

        public async Task<HttpResponseMessage> SendAsync(string path, string body = "{}",
            string method = "POST")
        {
            using HttpRequestMessage request = new(new HttpMethod(method), path);
            if (!HttpMethods.IsGet(method))
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
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
