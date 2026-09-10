using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using AcademicCollectorDemo.Tests.Infrastructure;
using FluentMigrator.Runner;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class AcademicWorkSourceUrlMigrationTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Migration_DifferentLegacyUrls_BackfillsAndRollbackReconstructs()
    {
        int workId;
        string personelId = "url-migration-" + Guid.NewGuid().ToString("N");
        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            Researcher researcher = new() { PersonelId = personelId };
            AcademicWork work = new()
            {
                PersonelId = personelId, Provider = AcademicWorkProvider.Orcid,
                Link = "https://example.test/Path", FullTextUrl = "https://example.test/Paper.pdf",
                SyncedAt = DateTime.UtcNow
            };
            researcher.AcademicWorks = [work];
            database.Researchers.Add(researcher);
            await database.SaveChangesAsync();
            workId = work.Id;
        }

        try
        {
            MigrateDown();
            await ExecuteAsync("UPDATE AcademicWorks SET SourceUrl=@source, OpenAccessUrl=@oa WHERE Id=@id",
                ("@source", "https://example.test/path"), ("@oa", "https://oa.test/article"), ("@id", workId));
            MigrateUp();

            await using SqlConnection connection = new(fixture.ConnectionString);
            await connection.OpenAsync();
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT Origin, Url FROM AcademicWorkSources WHERE AcademicWorkId=@id ORDER BY Origin";
            command.Parameters.AddWithValue("@id", workId);
            Dictionary<string, string> sources = [];
            await using (SqlDataReader reader = await command.ExecuteReaderAsync())
                while (await reader.ReadAsync()) sources.Add(reader.GetString(0), reader.GetString(1));
            Assert.Equal("https://example.test/Path", sources["Legacy.Link"]);
            Assert.Equal("https://example.test/path", sources["Legacy.SourceUrl"]);
            Assert.Equal("https://oa.test/article", sources["Legacy.OpenAccessUrl"]);
            Assert.Equal("https://example.test/Paper.pdf", sources["Legacy.FullTextUrl"]);

            MigrateDown();
            Assert.Equal("https://example.test/path", await ScalarAsync<string>("SELECT SourceUrl FROM AcademicWorks WHERE Id=@id", workId));
            Assert.Equal("https://oa.test/article", await ScalarAsync<string>("SELECT OpenAccessUrl FROM AcademicWorks WHERE Id=@id", workId));
        }
        finally { MigrateUp(); }
    }

    private void MigrateDown()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateDown(202609100003);
    }
    private void MigrateUp()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
    }
    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using SqlConnection connection = new(fixture.ConnectionString); await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand(); command.CommandText = sql;
        foreach ((string name, object value) in parameters) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }
    private async Task<T> ScalarAsync<T>(string sql, int id)
    {
        await using SqlConnection connection = new(fixture.ConnectionString); await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand(); command.CommandText = sql; command.Parameters.AddWithValue("@id", id);
        return (T)(await command.ExecuteScalarAsync())!;
    }
}
