using ResearcherAnalysisService.Products.ArticleReviews;
using ResearcherAnalysisService.Products.Data;
using ResearcherAnalysisService.Tests.Infrastructure;
using FluentMigrator.Runner;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ResearcherAnalysisService.Tests;

[Collection("Analysis Product SQL Server")]
public sealed class CanonicalArticleReviewMigrationTests(AnalysisProductSqlServerFixture fixture)
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
            AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
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
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateDown(202609140008);
    }

    private void MigrateUp()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
    }
}
