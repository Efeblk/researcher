using FluentMigrator.Runner;
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
        using IServiceScope scope = services.CreateScope();
        IMigrationRunner migrationRunner = scope.ServiceProvider
            .GetRequiredService<IMigrationRunner>();

        migrationRunner.MigrateUp();
    }

    public static void CleanAcademicDatabase(this IServiceProvider services)
    {
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
}
