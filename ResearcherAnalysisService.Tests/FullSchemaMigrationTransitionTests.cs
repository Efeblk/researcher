using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ResearcherAnalysisService.Data;

namespace ResearcherAnalysisService.Tests;

public sealed class FullSchemaMigrationTransitionTests
{
    private static readonly string[] ExpectedTables =
    [
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
    ];

    [Fact]
    public async Task Migration_FreshDatabase_CreatesFullOwnedSchemaWithoutSourceTablesOrForeignKeys()
    {
        await using TemporaryDatabase database = await TemporaryDatabase.CreateAsync();
        await using ServiceProvider services = CreateServices(database.ConnectionString);

        services.MigrateAnalysisDatabase();
        services.MigrateAnalysisDatabase();

        string[] actual = await database.QueryStringsAsync("""
            SELECT CONCAT(SCHEMA_NAME(schema_id), '.', name)
            FROM sys.tables
            ORDER BY 1;
            """);
        Assert.Equal(ExpectedTables, actual);
        Assert.Equal(0, await database.ScalarAsync<int>("""
            SELECT COUNT(*)
            FROM sys.foreign_keys foreignKey
            JOIN sys.tables parentTable ON parentTable.[object_id] = foreignKey.[parent_object_id]
            JOIN sys.schemas parentSchema ON parentSchema.[schema_id] = parentTable.[schema_id]
            JOIN sys.tables referencedTable ON referencedTable.[object_id] = foreignKey.[referenced_object_id]
            JOIN sys.schemas referencedSchema ON referencedSchema.[schema_id] = referencedTable.[schema_id]
            WHERE parentSchema.[name] IN (N'analysis', N'hr', N'faculty')
              AND referencedSchema.[name] NOT IN (N'analysis', N'hr', N'faculty');
            """));
        Assert.Equal(16, await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [dbo].[ResearcherAnalysisVersionInfo]"));
    }

    [Fact]
    public async Task Migration_ExistingFullSchema_AdoptsRowsAndDropsOnlyCrossSourceForeignKeys()
    {
        await using TemporaryDatabase database = await TemporaryDatabase.CreateAsync();
        await using ServiceProvider services = CreateServices(database.ConnectionString);
        services.MigrateAnalysisDatabase();
        await database.ExecuteAsync("""
            EXEC(N'CREATE SCHEMA [core]');
            CREATE TABLE [core].[Researchers]
            (
                [PersonelID] nvarchar(200) NOT NULL PRIMARY KEY
            );
            INSERT INTO [core].[Researchers] VALUES (N'adoption-sentinel');
            INSERT INTO [analysis].[ResearcherAnalyses]
                ([PersonelID], [SavedAt], [SnapshotJson], [ReportJson])
            VALUES (N'adoption-sentinel', SYSDATETIMEOFFSET(), N'{}', N'{}');
            ALTER TABLE [analysis].[ResearcherAnalyses]
                ADD CONSTRAINT [FK_LegacyResearcherAnalyses_Researchers]
                FOREIGN KEY ([PersonelID]) REFERENCES [core].[Researchers] ([PersonelID]);
            CREATE TABLE [hr].[UnrelatedStaff]
            (
                [Id] int NOT NULL PRIMARY KEY,
                [PersonelID] nvarchar(200) NOT NULL
            );
            INSERT INTO [hr].[UnrelatedStaff] VALUES (1, N'adoption-sentinel');
            ALTER TABLE [hr].[UnrelatedStaff]
                ADD CONSTRAINT [FK_UnrelatedStaff_Researchers]
                FOREIGN KEY ([PersonelID]) REFERENCES [core].[Researchers] ([PersonelID]);
            DELETE FROM [dbo].[ResearcherAnalysisVersionInfo] WHERE [Version] > 202609140001;
            """);

        services.MigrateAnalysisDatabase();

        Assert.Equal(1, await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [analysis].[ResearcherAnalyses] WHERE [PersonelID]=N'adoption-sentinel'"));
        Assert.Equal(0, await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.foreign_keys WHERE [name]=N'FK_LegacyResearcherAnalyses_Researchers'"));
        Assert.Equal(1, await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.foreign_keys WHERE [name]=N'FK_UnrelatedStaff_Researchers'"));
        Assert.Equal(1, await database.ScalarAsync<int>("""
            SELECT COUNT(*) FROM sys.foreign_keys
            WHERE [name]=N'FK_CanonicalArticleClaims_AnalysisRuns';
            """));
        Assert.Equal(16, await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [dbo].[ResearcherAnalysisVersionInfo]"));
    }

    [Fact]
    public async Task Migration_PartialOwnedGroup_FailsWithoutCompletingThatGroup()
    {
        await using TemporaryDatabase database = await TemporaryDatabase.CreateAsync();
        await database.ExecuteAsync("""
            EXEC(N'CREATE SCHEMA [analysis]');
            CREATE TABLE [analysis].[ArticleSourceSnapshots] ([Id] bigint NOT NULL PRIMARY KEY);
            """);
        await using ServiceProvider services = CreateServices(database.ConnectionString);

        Exception error = Assert.ThrowsAny<Exception>(services.MigrateAnalysisDatabase);

        Assert.Contains("canonical article evidence schema is incomplete", error.ToString(),
            StringComparison.Ordinal);
        Assert.Equal(0, await database.ScalarAsync<int>("""
            SELECT COUNT(*) FROM sys.tables
            WHERE schema_id=SCHEMA_ID('analysis') AND name=N'ArticleSourcePages';
            """));
        Assert.Equal(0, await database.ScalarAsync<int>("""
            SELECT COUNT(*) FROM [dbo].[ResearcherAnalysisVersionInfo]
            WHERE [Version]=202609140005;
            """));
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
            string databaseName = "FullSchemaMigrationTransition_" + Guid.NewGuid().ToString("N");
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

        public async Task ExecuteAsync(string sql)
        {
            await using SqlConnection connection = new(ConnectionString);
            await connection.OpenAsync();
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        public async Task<T> ScalarAsync<T>(string sql)
        {
            await using SqlConnection connection = new(ConnectionString);
            await connection.OpenAsync();
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = sql;
            return (T)(await command.ExecuteScalarAsync())!;
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
