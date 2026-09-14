using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ResearcherAnalysisService.Products.Data;

namespace ServiceAcceptancePilot;

internal static class FinalAcceptanceDatabase
{
    private const string Prefix = "AcademicFinalServiceAcceptance_";

    public static string NewName() => Prefix + Guid.NewGuid().ToString("N");

    public static (string Master, string Database) Connections(string name)
    {
        ValidateName(name);
        SqlConnectionStringBuilder master = new()
        {
            DataSource = @"(localdb)\MSSQLLocalDB",
            InitialCatalog = "master",
            IntegratedSecurity = true,
            TrustServerCertificate = true,
            ConnectTimeout = 30
        };
        SqlConnectionStringBuilder database = new(master.ConnectionString) { InitialCatalog = name };
        return (master.ConnectionString, database.ConnectionString);
    }

    public static AcademicDbContext Open(string connection) => new(
        new DbContextOptionsBuilder<AcademicDbContext>().UseSqlServer(connection).Options);

    public static AnalysisDbContext OpenAnalysis(string connection) => new(
        new DbContextOptionsBuilder<AnalysisDbContext>().UseSqlServer(connection).Options);

    public static async Task CreateAsync(string masterConnection, string name)
    {
        ValidateName(name);
        await using SqlConnection connection = new(masterConnection);
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{name}]";
        await command.ExecuteNonQueryAsync();
    }

    public static async Task DropAsync(string masterConnection, string name)
    {
        ValidateName(name);
        await using SqlConnection connection = new(masterConnection);
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = $"IF DB_ID(N'{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END";
        await command.ExecuteNonQueryAsync();
    }

    public static void ValidateName(string name)
    {
        string suffix = name.StartsWith(Prefix, StringComparison.Ordinal) ? name[Prefix.Length..] : string.Empty;
        if (suffix.Length != 32 || suffix.Any(character => !Uri.IsHexDigit(character) || char.IsUpper(character)))
            throw new InvalidOperationException("The isolated final-acceptance database name is invalid.");
    }
}
