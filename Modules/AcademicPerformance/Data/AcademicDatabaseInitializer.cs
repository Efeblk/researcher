using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data;

public sealed class AcademicDatabaseInitializer
{
    private readonly AcademicDbContext _dbContext;
    private readonly IConfiguration _configuration;

    public AcademicDatabaseInitializer(
        AcademicDbContext dbContext,
        IConfiguration configuration)
    {
        _dbContext = dbContext;
        _configuration = configuration;
    }

    public async Task EnsureReadyAsync()
    {
        string? provider = null;

        await _dbContext.Database.EnsureCreatedAsync();

        provider = _configuration["Database:Provider"]
            ?? DatabaseConfiguration.SqliteProvider;

        if (provider.Equals(
            DatabaseConfiguration.SqliteProvider,
            StringComparison.OrdinalIgnoreCase))
        {
            await AddSqliteColumnIfMissingAsync("OpenAlexProfiles", "LastUpdatedAt");
            await AddSqliteColumnIfMissingAsync("GoogleScholarProfiles", "LastUpdatedAt");
            await AddSqliteColumnIfMissingAsync(
                "GoogleScholarProfiles",
                "CitationCount",
                "INTEGER");
            await AddSqliteColumnIfMissingAsync(
                "GoogleScholarProfiles",
                "HIndex",
                "INTEGER");
            await AddSqliteColumnIfMissingAsync(
                "GoogleScholarProfiles",
                "I10Index",
                "INTEGER");
            await AddSqliteColumnIfMissingAsync("ScopusProfiles", "LastUpdatedAt");
            await AddSqliteColumnIfMissingAsync("WebOfScienceProfiles", "LastUpdatedAt");
            return;
        }

        if (provider.Equals(
            DatabaseConfiguration.SqlServerProvider,
            StringComparison.OrdinalIgnoreCase))
        {
            await AddSqlServerColumnIfMissingAsync("OpenAlexProfiles", "LastUpdatedAt");
            await AddSqlServerColumnIfMissingAsync("GoogleScholarProfiles", "LastUpdatedAt");
            await AddSqlServerIntegerColumnIfMissingAsync(
                "GoogleScholarProfiles",
                "CitationCount");
            await AddSqlServerIntegerColumnIfMissingAsync("GoogleScholarProfiles", "HIndex");
            await AddSqlServerIntegerColumnIfMissingAsync("GoogleScholarProfiles", "I10Index");
            await AddSqlServerColumnIfMissingAsync("ScopusProfiles", "LastUpdatedAt");
            await AddSqlServerColumnIfMissingAsync("WebOfScienceProfiles", "LastUpdatedAt");
        }
    }

    private async Task AddSqliteColumnIfMissingAsync(
        string tableName,
        string columnName,
        string columnType = "TEXT")
    {
        DbConnection? connection = null;
        DbCommand? command = null;
        object? result = null;
        bool shouldCloseConnection = false;
        int columnCount = 0;

        connection = _dbContext.Database.GetDbConnection();
        shouldCloseConnection = connection.State != ConnectionState.Open;

        if (shouldCloseConnection)
        {
            await connection.OpenAsync();
        }

        try
        {
            command = connection.CreateCommand();
            command.CommandText =
                $"SELECT COUNT(*) FROM pragma_table_info('{tableName}') " +
                $"WHERE name = '{columnName}';";
            result = await command.ExecuteScalarAsync();
            columnCount = Convert.ToInt32(result);

            if (columnCount > 0)
            {
                return;
            }

            command.CommandText =
                $"ALTER TABLE \"{tableName}\" " +
                $"ADD COLUMN \"{columnName}\" {columnType} NULL;";
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            command?.Dispose();

            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task AddSqlServerColumnIfMissingAsync(
        string tableName,
        string columnName)
    {
        string? sql = null;

        sql =
            $"IF COL_LENGTH('{tableName}', '{columnName}') IS NULL " +
            $"ALTER TABLE [{tableName}] ADD [{columnName}] datetime2 NULL;";

        await _dbContext.Database.ExecuteSqlRawAsync(sql);
    }

    private async Task AddSqlServerIntegerColumnIfMissingAsync(
        string tableName,
        string columnName)
    {
        string? sql = null;

        sql =
            $"IF COL_LENGTH('{tableName}', '{columnName}') IS NULL " +
            $"ALTER TABLE [{tableName}] ADD [{columnName}] int NULL;";

        await _dbContext.Database.ExecuteSqlRawAsync(sql);
    }
}
