using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleReviews;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Tests.Infrastructure;
using FluentMigrator.Runner;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class CanonicalArticleReviewMigrationTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Migration_UpCreatesTypedReviewTablesAndDownRemovesOnlyReviewTables()
    {
        MigrateDown();
        try
        {
            Assert.Equal(0, await CountReviewTablesAsync());
            MigrateUp();
            Assert.Equal(3, await CountReviewTablesAsync());

            using IServiceScope scope = fixture.Services.CreateScope();
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            IEntityType run = database.Model.FindEntityType(typeof(CanonicalArticleReviewRun))!;
            Assert.Equal("CanonicalArticleReviewRuns", run.GetTableName());
            Assert.Equal("analysis", run.GetSchema());
            Assert.Equal("CanonicalArticleReviewFindings",
                database.Model.FindEntityType(typeof(CanonicalArticleReviewFinding))!.GetTableName());
            Assert.Equal("CanonicalArticleReviewEvidence",
                database.Model.FindEntityType(typeof(CanonicalArticleReviewEvidence))!.GetTableName());
        }
        finally
        {
            MigrateUp();
        }
    }

    private async Task<int> CountReviewTablesAsync()
    {
        await using SqlConnection connection = new(fixture.ConnectionString);
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM sys.tables t
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE s.name = N'analysis' AND t.name IN
                (N'CanonicalArticleReviewRuns', N'CanonicalArticleReviewFindings', N'CanonicalArticleReviewEvidence');
            """;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private void MigrateDown()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateDown(202609110008);
    }

    private void MigrateUp()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
    }
}
