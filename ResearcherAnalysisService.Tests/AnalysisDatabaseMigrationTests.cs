using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ResearcherAnalysisService.Data;

namespace ResearcherAnalysisService.Tests;

public sealed class AnalysisDatabaseMigrationTests
{
    [Fact]
    public async Task Migration_FreshDatabase_CreatesOnlyOwnedTablesAndHistoryAndIsIdempotent()
    {
        await using TemporaryDatabase database = await TemporaryDatabase.CreateAsync();
        await using ServiceProvider services = CreateServices(database.ConnectionString);

        services.MigrateAnalysisDatabase();
        services.MigrateAnalysisDatabase();

        string[] tables = await database.QueryStringsAsync("""
            SELECT CONCAT(SCHEMA_NAME(schema_id), '.', name)
            FROM sys.tables
            ORDER BY 1;
            """);
        Assert.Equal([
            "analysis.ArticleEvaluationAttempts", "analysis.ArticleEvaluationCases",
            "analysis.ArticleEvaluationResults", "analysis.ArticleEvaluationRuns",
            "analysis.ArticleEvaluationWorkItems", "analysis.ArticleReviewStageCheckpoints",
            "analysis.ArticleReviewWorkItems", "analysis.ArticleSourcePages",
            "analysis.ArticleSourceSnapshots", "analysis.ArticleSourceSpans",
            "analysis.ArticleSummaries", "analysis.ArticleSummaryAutomationJobs",
            "analysis.CanonicalArticleAnalysisRuns", "analysis.CanonicalArticleClaimEvidence",
            "analysis.CanonicalArticleClaims", "analysis.CanonicalArticleReviewEvidence",
            "analysis.CanonicalArticleReviewFindings", "analysis.CanonicalArticleReviewRuns",
            "analysis.CollectionChangeReceipts", "analysis.GeminiUsageAttempts",
            "analysis.PublicationMetricProviderSnapshots", "analysis.PublicationMetricSnapshots",
            "analysis.PublicationMetricsRefreshStates", "analysis.ReferencePopulationManifests",
            "analysis.ReferencePopulationMembers", "analysis.ResearcherAnalyses",
            "dbo.ResearcherAnalysisVersionInfo", "faculty.AssistantContextVersions",
            "faculty.AssistantRuns", "hr.DossierReviewActions", "hr.EvidenceDossiers"
        ], tables);
        Assert.Equal(1, await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [dbo].[ResearcherAnalysisVersionInfo] WHERE [Version]=202609140017"));
    }

    [Fact]
    public async Task HostedStartup_MigratesBeforeServiceListens()
    {
        await using TemporaryDatabase database = await TemporaryDatabase.CreateAsync();
        await using WebApplication application = Program.CreateApplication(
            ["--environment", "Testing"], builder =>
            {
                builder.Configuration.Sources.Clear();
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Urls"] = "http://127.0.0.1:0",
                    ["ConnectionStrings:UsageDatabase"] = database.ConnectionString
                });
            });

        await application.StartAsync();

        Assert.Equal(1, await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID('analysis') AND name='GeminiUsageAttempts'"));
        string address = application.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using HttpClient client = new() { BaseAddress = new Uri(address) };
        using HttpResponseMessage health = await client.GetAsync("/health");
        health.EnsureSuccessStatusCode();
        await application.StopAsync();
    }

    private static ServiceProvider CreateServices(string connectionString)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:UsageDatabase"] = connectionString
            }).Build();
        ServiceCollection services = new();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddAnalysisDatabaseMigrations(configuration);
        return services.BuildServiceProvider();
    }

    private sealed class TemporaryDatabase : IAsyncDisposable
    {
        private readonly string _databaseName;
        private readonly string _masterConnectionString;
        public string ConnectionString { get; }

        private TemporaryDatabase(string databaseName, string masterConnectionString,
            string connectionString)
        {
            _databaseName = databaseName;
            _masterConnectionString = masterConnectionString;
            ConnectionString = connectionString;
        }

        public static async Task<TemporaryDatabase> CreateAsync()
        {
            string databaseName = "AnalysisMigrationTests_" + Guid.NewGuid().ToString("N");
            SqlConnectionStringBuilder connection = new(
                Environment.GetEnvironmentVariable("ACADEMIC_TEST_SQLSERVER") ??
                @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;Encrypt=true;TrustServerCertificate=true");
            connection.Encrypt = SqlConnectionEncryptOption.Mandatory;
            connection.InitialCatalog = "master";
            string masterConnectionString = connection.ConnectionString;
            await using SqlConnection master = new(masterConnectionString);
            await master.OpenAsync();
            await using SqlCommand create = master.CreateCommand();
            create.CommandText = $"CREATE DATABASE [{databaseName}]";
            await create.ExecuteNonQueryAsync();
            connection.InitialCatalog = databaseName;
            return new(databaseName, masterConnectionString, connection.ConnectionString);
        }

        public async Task CreateLedgerAsync(string schema, Guid sentinel)
        {
            await using SqlConnection connection = new(ConnectionString);
            await connection.OpenAsync();
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = $"""
                IF SCHEMA_ID(N'{schema}') IS NULL
                    EXEC(N'CREATE SCHEMA [{schema}] AUTHORIZATION [dbo]');
                CREATE TABLE [{schema}].[GeminiUsageAttempts]
                (
                    [AttemptId] uniqueidentifier NOT NULL CONSTRAINT [PK_{schema}_GeminiUsageAttempts] PRIMARY KEY,
                    [StartedAt] datetime2 NOT NULL,
                    [CompletedAt] datetime2 NULL,
                    [RequestedModel] nvarchar(200) NOT NULL,
                    [ReturnedModel] nvarchar(200) NULL,
                    [Outcome] nvarchar(40) NOT NULL,
                    [HttpStatus] int NULL,
                    [PromptTokenCount] bigint NULL,
                    [CachedTokenCount] bigint NULL,
                    [CandidateTokenCount] bigint NULL,
                    [ThoughtTokenCount] bigint NULL,
                    [TotalTokenCount] bigint NULL,
                    [PricingVersion] nvarchar(80) NULL,
                    [EstimatedUsd] decimal(19, 9) NULL
                );
                CREATE INDEX [IX_GeminiUsageAttempts_StartedAt_AttemptId]
                    ON [{schema}].[GeminiUsageAttempts] ([StartedAt] DESC, [AttemptId] DESC);
                INSERT INTO [{schema}].[GeminiUsageAttempts]
                    ([AttemptId], [StartedAt], [RequestedModel], [Outcome])
                VALUES (@sentinel, SYSUTCDATETIME(), N'gemini-3.8-flash', N'Pending');
                """;
            command.Parameters.AddWithValue("@sentinel", sentinel);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<T> ScalarAsync<T>(string sql)
        {
            await using SqlConnection connection = new(ConnectionString);
            await connection.OpenAsync();
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = sql;
            object value = (await command.ExecuteScalarAsync())!;
            return (T)value;
        }

        public async Task<string[]> QueryStringsAsync(string sql)
        {
            List<string> values = [];
            await using SqlConnection connection = new(ConnectionString);
            await connection.OpenAsync();
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = sql;
            await using SqlDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                values.Add(reader.GetString(0));
            return values.ToArray();
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
