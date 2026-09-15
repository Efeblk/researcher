using AcademicCollectorDemo.Modules.AcademicPerformance;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AcademicCollectorDemo.Tests.Integration;

public sealed class MigrationOwnershipTests
{
    [Fact]
    public async Task CollectorMigrationAndClean_LeaveAnalysisLedgerAndHistoryUntouched()
    {
        string databaseName = "CollectorOwnershipTests_" + Guid.NewGuid().ToString("N");
        SqlConnectionStringBuilder connection = new(
            Environment.GetEnvironmentVariable("ACADEMIC_TEST_SQLSERVER") ??
            @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;Encrypt=true;TrustServerCertificate=true");
        connection.Encrypt = SqlConnectionEncryptOption.Mandatory;
        connection.InitialCatalog = "master";
        string masterConnectionString = connection.ConnectionString;
        await CreateDatabaseAsync(masterConnectionString, databaseName);
        connection.InitialCatalog = databaseName;
        string databaseConnectionString = connection.ConnectionString;

        try
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:AcademicDatabase"] = databaseConnectionString,
                    ["BulkCollection:WorkerEnabled"] = "false",
                    ["ArticleSummaryAutomation:Enabled"] = "false",
                    ["ArticleSummaryAutomation:WorkerEnabled"] = "false",
                    ["PublicationMetrics:WorkerEnabled"] = "false",
                    ["ArticleEvaluation:WorkerEnabled"] = "false",
                    ["FacultyAssistant:WorkerEnabled"] = "false"
                }).Build();
            ServiceCollection services = new();
            services.AddLogging();
            services.AddAcademicPerformanceModule(configuration);
            await using ServiceProvider provider = services.BuildServiceProvider();
            provider.MigrateAcademicDatabase();

            await using (SqlConnection database = new(databaseConnectionString))
            {
                await database.OpenAsync();
                Assert.Equal(0, await CountAsync(database,
                    "SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID('analysis') AND name='GeminiUsageAttempts'"));
                await using SqlCommand seed = database.CreateCommand();
                seed.CommandText = """
                    EXEC(N'CREATE SCHEMA [analysis]');
                    CREATE TABLE [analysis].[GeminiUsageAttempts]
                    (
                        [AttemptId] uniqueidentifier NOT NULL PRIMARY KEY,
                        [StartedAt] datetime2 NOT NULL,
                        [RequestedModel] nvarchar(200) NOT NULL,
                        [Outcome] nvarchar(40) NOT NULL
                    );
                    INSERT INTO [analysis].[GeminiUsageAttempts]
                        ([AttemptId], [StartedAt], [RequestedModel], [Outcome])
                    VALUES ('99d0c550-bca1-4ed5-8ace-45196b48f2d3', SYSUTCDATETIME(),
                        N'gemini-3.8-flash', N'Pending');
                    CREATE TABLE [dbo].[ResearcherAnalysisVersionInfo] ([Version] bigint NOT NULL);
                    INSERT INTO [dbo].[ResearcherAnalysisVersionInfo] ([Version]) VALUES (202609140001);
                    """;
                await seed.ExecuteNonQueryAsync();
            }

            provider.CleanAcademicDatabase();

            await using SqlConnection after = new(databaseConnectionString);
            await after.OpenAsync();
            Assert.Equal(1, await CountAsync(after,
                "SELECT COUNT(*) FROM [analysis].[GeminiUsageAttempts] WHERE [AttemptId]='99d0c550-bca1-4ed5-8ace-45196b48f2d3'"));
            Assert.Equal(1, await CountAsync(after,
                "SELECT COUNT(*) FROM [dbo].[ResearcherAnalysisVersionInfo] WHERE [Version]=202609140001"));
            Assert.Equal(0, await CountAsync(after,
                "SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID('dbo') AND name='VersionInfo'"));
            await after.CloseAsync();

            provider.MigrateAcademicDatabase();

            await after.OpenAsync();
            Assert.Equal(1, await CountAsync(after,
                "SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID('core') AND name='Researchers'"));
            Assert.Equal(1, await CountAsync(after,
                "SELECT COUNT(*) FROM [analysis].[GeminiUsageAttempts] WHERE [AttemptId]='99d0c550-bca1-4ed5-8ace-45196b48f2d3'"));
        }
        finally
        {
            await DropDatabaseAsync(masterConnectionString, databaseName);
        }
    }

    private static async Task<int> CountAsync(SqlConnection connection, string sql)
    {
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task CreateDatabaseAsync(string masterConnectionString, string databaseName)
    {
        await using SqlConnection master = new(masterConnectionString);
        await master.OpenAsync();
        await using SqlCommand command = master.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{databaseName}]";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(string masterConnectionString, string databaseName)
    {
        await using SqlConnection master = new(masterConnectionString);
        await master.OpenAsync();
        await using SqlCommand command = master.CreateCommand();
        command.CommandText = $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
            $"DROP DATABASE [{databaseName}]";
        await command.ExecuteNonQueryAsync();
    }
}
