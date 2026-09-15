using FluentMigrator.Runner;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data;

public static class AcademicDatabase
{
    public const string ProviderName = "SqlServer";

    public static IServiceCollection AddAcademicDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string connectionString = GetConnectionString(configuration);

        services.AddDbContext<AcademicDbContext>(options =>
            options.UseSqlServer(connectionString));
        services.AddSingleton(new AcademicDatabaseConnection(connectionString));

        services.AddFluentMigratorCore()
            .ConfigureRunner(runner => runner
                .AddSqlServer()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof(AcademicDatabase).Assembly)
                .For.Migrations());

        return services;
    }

    public static void MigrateAcademicDatabase(this IServiceProvider services)
    {
        using SqlConnection migrationLock = AcquireMigrationLock(services);
        using IServiceScope scope = services.CreateScope();
        IMigrationRunner migrationRunner = scope.ServiceProvider
            .GetRequiredService<IMigrationRunner>();

        migrationRunner.MigrateUp();
    }

    public static void CleanAcademicDatabase(this IServiceProvider services)
    {
        using SqlConnection migrationLock = AcquireMigrationLock(services);
        using IServiceScope scope = services.CreateScope();
        IMigrationRunner migrationRunner = scope.ServiceProvider
            .GetRequiredService<IMigrationRunner>();

        migrationRunner.MigrateDown(0);

        AcademicDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<AcademicDbContext>();
        dbContext.Database.ExecuteSqlRaw(
            "DROP TABLE IF EXISTS [dbo].[VersionInfo]");
    }

    private static string GetConnectionString(IConfiguration configuration)
    {
        string? connectionString = configuration.GetConnectionString(
            "AcademicDatabase");

        return !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new InvalidOperationException(
                "Veritabanı bağlantı cümlesi bulunamadı.");
    }

    private static SqlConnection AcquireMigrationLock(IServiceProvider services)
    {
        string connectionString = services.GetRequiredService<AcademicDatabaseConnection>()
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
                    @Resource = N'AcademicCollectorDemo.DatabaseMigrations',
                    @LockMode = N'Exclusive',
                    @LockOwner = N'Session',
                    @LockTimeout = 60000;
                SELECT @result;
                """;
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

    private sealed record AcademicDatabaseConnection(string ConnectionString);
}
