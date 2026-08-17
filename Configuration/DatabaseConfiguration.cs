using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

public static class DatabaseConfiguration
{
    public const string SqliteProvider = "Sqlite";
    public const string SqlServerProvider = "SqlServer";

    public static string Configure(
        DbContextOptionsBuilder<AcademicDbContext> optionsBuilder,
        IConfiguration configuration)
    {
        string? provider = null;
        string? connectionString = null;

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
            optionsBuilder.UseSqlite(connectionString);
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
