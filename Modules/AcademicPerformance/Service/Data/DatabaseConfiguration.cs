using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data;

public static class DatabaseConfiguration
{
    public const string SqlServerProvider = "SqlServer";

    public static void Configure(
        DbContextOptionsBuilder optionsBuilder,
        IConfiguration configuration)
    {
        optionsBuilder.UseSqlServer(GetConnectionString(configuration));
    }

    public static string GetConnectionString(IConfiguration configuration)
    {
        string? connectionString = configuration.GetConnectionString(
            "AcademicDatabase");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Veritabanı bağlantı cümlesi bulunamadı.");
        }

        return connectionString;
    }
}
