using AcademicCollectorDemo.Tests.Infrastructure;
using FluentMigrator.Runner;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class TableSchemaGroupingMigrationTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Migration_ExistingRelatedRows_PreservesDataAndForeignKeyBehavior()
    {
        string personelId = "schema-migration-" + Guid.NewGuid().ToString("N");
        int workId;
        MigrateDown();
        try
        {
            await using (SqlConnection connection = new(fixture.ConnectionString))
            {
                await connection.OpenAsync();
                await using SqlCommand seed = connection.CreateCommand();
                seed.CommandText = """
                    INSERT INTO [dbo].[Researchers] ([PersonelID], [FirstName])
                    VALUES (@personelId, N'Before migration');
                    INSERT INTO [dbo].[AcademicWorks]
                        ([PersonelID], [Provider], [Title], [Category], [CategorySource], [SyncedAt])
                    OUTPUT INSERTED.[Id]
                    VALUES (@personelId, N'Orcid', N'Preserved work', N'Article', N'Provider', SYSUTCDATETIME());
                    """;
                seed.Parameters.AddWithValue("@personelId", personelId);
                workId = Convert.ToInt32(await seed.ExecuteScalarAsync());
            }

            MigrateUp();

            await using SqlConnection migrated = new(fixture.ConnectionString);
            await migrated.OpenAsync();
            await AssertExpectedTablesAsync(migrated);
            await using (SqlCommand data = migrated.CreateCommand())
            {
                data.CommandText = """
                    SELECT COUNT(*)
                    FROM [core].[Researchers] r
                    JOIN [core].[AcademicWorks] w ON w.[PersonelID] = r.[PersonelID]
                    WHERE r.[PersonelID] = @personelId AND w.[Id] = @workId
                        AND r.[FirstName] = N'Before migration' AND w.[Title] = N'Preserved work';
                    """;
                data.Parameters.AddWithValue("@personelId", personelId);
                data.Parameters.AddWithValue("@workId", workId);
                Assert.Equal(1, Convert.ToInt32(await data.ExecuteScalarAsync()));
            }

            await using (SqlCommand foreignKey = migrated.CreateCommand())
            {
                foreignKey.CommandText = """
                    SELECT COUNT(*)
                    FROM sys.foreign_keys fk
                    WHERE fk.parent_object_id = OBJECT_ID(N'[core].[AcademicWorks]')
                        AND fk.referenced_object_id = OBJECT_ID(N'[core].[Researchers]')
                        AND fk.delete_referential_action = 1;
                    """;
                Assert.Equal(1, Convert.ToInt32(await foreignKey.ExecuteScalarAsync()));
            }

            await using (SqlCommand invalidChild = migrated.CreateCommand())
            {
                invalidChild.CommandText = """
                    INSERT INTO [core].[AcademicWorks]
                        ([PersonelID], [Provider], [Category], [CategorySource], [SyncedAt])
                    VALUES (N'missing-parent', N'Orcid', N'Article', N'Provider', SYSUTCDATETIME());
                    """;
                SqlException exception = await Assert.ThrowsAsync<SqlException>(
                    () => invalidChild.ExecuteNonQueryAsync());
                Assert.Equal(547, exception.Number);
            }

            await using (SqlCommand cascade = migrated.CreateCommand())
            {
                cascade.CommandText = """
                    DELETE FROM [core].[Researchers] WHERE [PersonelID] = @personelId;
                    SELECT COUNT(*) FROM [core].[AcademicWorks] WHERE [Id] = @workId;
                    """;
                cascade.Parameters.AddWithValue("@personelId", personelId);
                cascade.Parameters.AddWithValue("@workId", workId);
                Assert.Equal(0, Convert.ToInt32(await cascade.ExecuteScalarAsync()));
            }
        }
        finally
        {
            MigrateUp();
        }
    }

    private void MigrateDown()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateDown(202609100005);
    }

    private void MigrateUp()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
    }

    private static async Task AssertExpectedTablesAsync(SqlConnection connection)
    {
        string[] expectedApplicationTables =
        [
            "analysis.ArticleSummaries", "analysis.ResearcherAnalyses",
            "bulk.BulkCollectionBatches", "bulk.BulkCollectionJobs",
            "core.AcademicWorks", "core.AcademicWorkSources", "core.PublicationDisplayApprovals",
            "core.PublicationSummaries", "core.Researchers", "crossref.CrossrefWorks",
            "googlescholar.GoogleScholarProfiles", "googlescholar.GoogleScholarWorks",
            "integrations.ProviderRequestBudgets", "integrations.ProviderStatusObservations",
            "openalex.OpenAlexProfiles", "openalex.OpenAlexWorks", "orcid.OrcidProfiles",
            "orcid.OrcidWorks", "semanticscholar.SemanticScholarCitationContexts",
            "semanticscholar.SemanticScholarCitations", "semanticscholar.SemanticScholarPapers",
            "trdizin.TrDizinProfiles", "trdizin.TrDizinWorks", "wos.WebOfSciencePeerReviews",
            "wos.WebOfScienceProfiles", "wos.WebOfScienceWorks", "yoksis.YoksisRecords"
        ];
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.[name] + N'.' + t.[name]
            FROM sys.tables t
            JOIN sys.schemas s ON s.[schema_id] = t.[schema_id]
            ORDER BY s.[name], t.[name];
            """;
        List<string> actual = [];
        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            actual.Add(reader.GetString(0));

        Assert.Equal(["dbo.VersionInfo"], actual.Where(table => table.StartsWith("dbo.")));
        Assert.Equal(expectedApplicationTables, actual.Where(table => !table.StartsWith("dbo.")));
    }
}
