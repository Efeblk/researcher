using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data;

public static class DatabaseConfiguration
{
    public const string SqliteProvider = "Sqlite";
    public const string SqlServerProvider = "SqlServer";

    public static string Configure(
        DbContextOptionsBuilder<AcademicDbContext> optionsBuilder,
        IConfiguration configuration,
        string? contentRootPath = null)
    {
        string? provider = null;
        string? connectionString = null;
        string? databaseRootPath = null;
        SqliteConnectionStringBuilder? sqliteConnection = null;

        provider = configuration["Database:Provider"];
        connectionString = configuration.GetConnectionString("AcademicDatabase");

        if (string.IsNullOrWhiteSpace(provider))
        {
            provider = SqliteProvider;
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Veritabanı bağlantı cümlesi bulunamadı.");
        }

        if (provider.Equals(SqliteProvider, StringComparison.OrdinalIgnoreCase))
        {
            sqliteConnection = new SqliteConnectionStringBuilder(connectionString);
            databaseRootPath = contentRootPath ?? Directory.GetCurrentDirectory();

            if (!string.IsNullOrWhiteSpace(sqliteConnection.DataSource) &&
                sqliteConnection.DataSource != ":memory:" &&
                sqliteConnection.Mode != SqliteOpenMode.Memory &&
                !Path.IsPathRooted(sqliteConnection.DataSource))
            {
                sqliteConnection.DataSource = Path.GetFullPath(
                    sqliteConnection.DataSource,
                    databaseRootPath);
            }

            optionsBuilder.UseSqlite(sqliteConnection.ConnectionString);
            return SqliteProvider;
        }

        if (provider.Equals(SqlServerProvider, StringComparison.OrdinalIgnoreCase))
        {
            optionsBuilder.UseSqlServer(connectionString);
            return SqlServerProvider;
        }

        throw new InvalidOperationException(
            $"Desteklenmeyen veritabanı sağlayıcısı: {provider}");
    }
}
