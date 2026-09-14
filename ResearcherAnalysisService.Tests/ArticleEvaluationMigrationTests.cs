using ResearcherAnalysisService.Products.Data;
using ResearcherAnalysisService.Products.Evaluations;
using ResearcherAnalysisService.Tests.Infrastructure;
using FluentMigrator.Runner;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ResearcherAnalysisService.Tests;

[Collection("Analysis Product SQL Server")]
public sealed class ArticleEvaluationMigrationTests(AnalysisProductSqlServerFixture fixture)
{
    [Fact]
    public async Task Migration_UpCreatesEvaluationTablesAndDownPreservesEarlierAnalysisTables()
    {
        MigrateDown();
        try
        {
            Assert.Equal(0, await CountEvaluationTablesAsync());
            Assert.Equal(1, await CountTableAsync("CanonicalArticleReviewRuns"));
            MigrateUp();
            Assert.Equal(5, await CountEvaluationTablesAsync());

            using IServiceScope scope = fixture.Services.CreateScope();
            AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
            Assert.Equal("analysis", database.Model.FindEntityType(typeof(ArticleEvaluationRun))!.GetSchema());
            Assert.Equal("ArticleEvaluationResults",
                database.Model.FindEntityType(typeof(ArticleEvaluationResult))!.GetTableName());
        }
        finally
        {
            MigrateUp();
        }
    }

    private async Task<int> CountEvaluationTablesAsync()
    {
        await using SqlConnection connection = new(fixture.ConnectionString);
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE s.name = N'analysis' AND t.name LIKE N'ArticleEvaluation%';
            """;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<int> CountTableAsync(string table)
    {
        await using SqlConnection connection = new(fixture.ConnectionString);
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id WHERE s.name=N'analysis' AND t.name=@table;";
        command.Parameters.AddWithValue("@table", table);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private void MigrateDown()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateDown(202609140009);
    }

    private void MigrateUp()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
    }
}
