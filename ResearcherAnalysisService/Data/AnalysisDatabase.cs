using FluentMigrator.Runner;
using FluentMigrator.Runner.VersionTableInfo;
using Microsoft.Data.SqlClient;

namespace ResearcherAnalysisService.Data;

public static class AnalysisDatabase
{
    private const string MigrationLockName = "AcademicCollectorDemo.DatabaseMigrations";

    public static IServiceCollection AddAnalysisDatabaseMigrations(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string connectionString = GetConnectionString(configuration);
        services.AddSingleton(new AnalysisDatabaseConnection(connectionString));
        services.AddFluentMigratorCore()
            .ConfigureRunner(runner => runner
                .AddSqlServer()
                .WithGlobalConnectionString(connectionString)
                .WithVersionTable(new ResearcherAnalysisVersionTable())
                .ScanIn(typeof(AnalysisDatabase).Assembly)
                .For.Migrations());
        return services;
    }

    public static void MigrateAnalysisDatabase(this IServiceProvider services)
    {
        using SqlConnection migrationLock = AcquireMigrationLock(services);
        using IServiceScope scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
    }

    private static string GetConnectionString(IConfiguration configuration)
    {
        string? connectionString = configuration.GetConnectionString("UsageDatabase");
        return !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new InvalidOperationException(
                "ConnectionStrings:UsageDatabase is required for analysis database migrations.");
    }

    private static SqlConnection AcquireMigrationLock(IServiceProvider services)
    {
        string connectionString = services.GetRequiredService<AnalysisDatabaseConnection>()
            .ConnectionString;
        SqlConnectionStringBuilder lockConnection = new(connectionString)
        {
            Pooling = false
        };
        SqlConnection connection = new(lockConnection.ConnectionString);
        try
        {
            connection.Open();
            using SqlCommand command = connection.CreateCommand();
            command.CommandTimeout = 65;
            command.CommandText = """
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock
                    @Resource = @resource,
                    @LockMode = N'Exclusive',
                    @LockOwner = N'Session',
                    @LockTimeout = 60000;
                SELECT @result;
                """;
            command.Parameters.AddWithValue("@resource", MigrationLockName);
            int result = Convert.ToInt32(command.ExecuteScalar());
            if (result < 0)
                throw new InvalidOperationException(
                    $"Could not acquire the database migration lock (SQL result {result}).");
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private sealed record AnalysisDatabaseConnection(string ConnectionString);

    private sealed class ResearcherAnalysisVersionTable : IVersionTableMetaData
    {
        public string SchemaName => "dbo";
        public string TableName => "ResearcherAnalysisVersionInfo";
        public string ColumnName => "Version";
        public string DescriptionColumnName => "Description";
        public string UniqueIndexName => "UC_ResearcherAnalysisVersion";
        public string AppliedOnColumnName => "AppliedOn";
        public bool CreateWithPrimaryKey => false;
        public bool OwnsSchema => false;
    }
}

public sealed class AnalysisDatabaseMigrationHostedService(IServiceProvider services) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        services.MigrateAnalysisDatabase();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
