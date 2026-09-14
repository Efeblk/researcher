using AcademicCollectorDemo.Modules.AcademicPerformance;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using FluentMigrator.Runner;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AcademicCollectorDemo.Tests.Integration;

public sealed class RetiredMigrationHistoryTests
{
    [Fact]
    public async Task CollectorClean_LegacyCrossServiceForeignKey_FailsBeforeDowngrade()
    {
        await using TemporaryDatabase database = await TemporaryDatabase.CreateAsync();
        await using ServiceProvider services = CreateServices(database.ConnectionString);
        services.MigrateAcademicDatabase();
        await database.ExecuteAsync("""
            INSERT INTO [core].[Researchers] ([PersonelID], [FirstName])
            VALUES (N'legacy-owner', N'Legacy');
            EXEC(N'CREATE SCHEMA [analysis]');
            CREATE TABLE [analysis].[ResearcherAnalyses]
            (
                [Id] int NOT NULL PRIMARY KEY,
                [PersonelID] nvarchar(200) NOT NULL
            );
            INSERT INTO [analysis].[ResearcherAnalyses] VALUES (1, N'legacy-owner');
            ALTER TABLE [analysis].[ResearcherAnalyses]
                ADD CONSTRAINT [FK_LegacyResearcherAnalyses_Researchers]
                FOREIGN KEY ([PersonelID]) REFERENCES [core].[Researchers] ([PersonelID]);
            """);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            services.CleanAcademicDatabase);

        Assert.Contains("Start ResearcherAnalysisService once", error.Message, StringComparison.Ordinal);
        Assert.Equal(1, await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [analysis].[ResearcherAnalyses] WHERE [Id]=1"));
        Assert.Equal(1, await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID('core') AND name='CollectionChanges'"));
        Assert.Equal(1, await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID('dbo') AND name='VersionInfo'"));
    }

    [Fact]
    public async Task CollectionChangesMigration_ExistingNormalizedData_SeedsBootstrapEvents()
    {
        await using TemporaryDatabase database = await TemporaryDatabase.CreateAsync();
        await using ServiceProvider services = CreateServices(database.ConnectionString);
        using (IServiceScope scope = services.CreateScope())
            scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp(202609120013);

        await database.ExecuteAsync("""
            INSERT INTO [core].[Researchers] ([PersonelID], [FirstName])
            VALUES (N'bootstrap-person', N'Bootstrap');
            INSERT INTO [core].[AcademicWorks]
                ([PersonelID], [Provider], [Category], [CategorySource], [SyncedAt])
            VALUES (N'bootstrap-person', N'Orcid', N'Article', N'Provider', SYSUTCDATETIME());
            DECLARE @academicWorkId int = SCOPE_IDENTITY();
            INSERT INTO [core].[CanonicalWorks]
                ([SourceScopedKey], [HasRetractionObservation], [CreatedAt], [UpdatedAt])
            VALUES (N'bootstrap-work', 0, SYSUTCDATETIME(), SYSUTCDATETIME());
            DECLARE @canonicalWorkId int = SCOPE_IDENTITY();
            INSERT INTO [core].[CanonicalResearcherWorks]
                ([CanonicalWorkId], [PersonelID], [LastObservedAt])
            VALUES (@canonicalWorkId, N'bootstrap-person', SYSUTCDATETIME());
            INSERT INTO [core].[CanonicalWorkObservations]
                ([CanonicalWorkId], [AcademicWorkId], [PersonelID], [Provider],
                 [CategoryObserved], [ObservedAt])
            VALUES (@canonicalWorkId, @academicWorkId, N'bootstrap-person', N'Orcid',
                    N'Article', SYSUTCDATETIME());
            """);

        services.MigrateAcademicDatabase();

        Assert.Equal(1, await database.ScalarAsync<int>("""
            SELECT COUNT(*) FROM [core].[CollectionChanges]
            WHERE [ChangeKind]=N'ResearcherCollected' AND [PersonelID]=N'bootstrap-person'
              AND [CanonicalWorkId] IS NULL AND [AcademicWorkId] IS NULL;
            """));
        Assert.Equal(1, await database.ScalarAsync<int>("""
            SELECT COUNT(*)
            FROM [core].[CollectionChanges] change
            JOIN [core].[CanonicalResearcherWorks] relation
              ON relation.[CanonicalWorkId]=change.[CanonicalWorkId]
             AND relation.[PersonelID]=change.[PersonelID]
            JOIN [core].[CanonicalWorkObservations] observation
              ON observation.[AcademicWorkId]=change.[AcademicWorkId]
            WHERE change.[ChangeKind]=N'CanonicalWorkChanged'
              AND change.[PersonelID]=N'bootstrap-person';
            """));
    }

    [Fact]
    public async Task CollectorMigrationAndClean_IgnoreRetiredVersionsAndPreserveOwnedSchemas()
    {
        await using TemporaryDatabase database = await TemporaryDatabase.CreateAsync();
        await using ServiceProvider services = CreateServices(database.ConnectionString);
        using (IServiceScope scope = services.CreateScope())
            scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp(202609110008);

        await database.ExecuteAsync("""
            INSERT INTO [dbo].[VersionInfo] ([Version], [AppliedOn], [Description]) VALUES
                (202609080001, SYSUTCDATETIME(), N'Retired ResearcherAnalyses owner'),
                (202609100003, SYSUTCDATETIME(), N'Retired ArticleSummaries owner'),
                (202609120013, SYSUTCDATETIME(), N'Retired analysis authorization owner');
            EXEC(N'CREATE SCHEMA [analysis]');
            EXEC(N'CREATE SCHEMA [hr]');
            EXEC(N'CREATE SCHEMA [faculty]');
            CREATE TABLE [analysis].[OwnedSentinel] ([Id] int NOT NULL PRIMARY KEY);
            CREATE TABLE [hr].[OwnedSentinel] ([Id] int NOT NULL PRIMARY KEY);
            CREATE TABLE [faculty].[OwnedSentinel] ([Id] int NOT NULL PRIMARY KEY);
            INSERT INTO [analysis].[OwnedSentinel] VALUES (1);
            INSERT INTO [hr].[OwnedSentinel] VALUES (2);
            INSERT INTO [faculty].[OwnedSentinel] VALUES (3);
            CREATE TABLE [dbo].[ResearcherAnalysisVersionInfo] ([Version] bigint NOT NULL);
            INSERT INTO [dbo].[ResearcherAnalysisVersionInfo] VALUES (202609140016);
            """);

        services.MigrateAcademicDatabase();
        Assert.Equal(1, await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID('core') AND name='CollectionChanges'"));
        services.CleanAcademicDatabase();

        Assert.Equal(1, await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [analysis].[OwnedSentinel] WHERE [Id]=1"));
        Assert.Equal(1, await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [hr].[OwnedSentinel] WHERE [Id]=2"));
        Assert.Equal(1, await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [faculty].[OwnedSentinel] WHERE [Id]=3"));
        Assert.Equal(1, await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [dbo].[ResearcherAnalysisVersionInfo] WHERE [Version]=202609140016"));
        Assert.Equal(0, await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID('dbo') AND name='VersionInfo'"));
        Assert.Equal(0, await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID('core')"));
    }

    private static ServiceProvider CreateServices(string connectionString)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:AcademicDatabase"] = connectionString,
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
            string databaseName = "RetiredMigrationHistory_" + Guid.NewGuid().ToString("N");
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
