using ResearcherAnalysisService.Products.Data;
using ResearcherAnalysisService.Products.FacultyAssistant;
using ResearcherAnalysisService.Products.HrDossiers;
using ResearcherAnalysisService.Tests.Infrastructure;
using FluentMigrator.Runner;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ResearcherAnalysisService.Tests;

[Collection("Analysis Product SQL Server")]
public sealed class AcademicAiProductMigrationTests(AnalysisProductSqlServerFixture fixture)
{
    [Fact]
    public async Task Migrations_CreateSeparatedHrAndFacultyTablesWithMappedEntities()
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        AnalysisDbContext database = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
        AssertEntity(database, typeof(HrEvidenceDossier), "hr", "EvidenceDossiers");
        AssertEntity(database, typeof(HrDossierReviewAction), "hr", "DossierReviewActions");
        AssertEntity(database, typeof(FacultyAssistantContextVersion), "faculty", "AssistantContextVersions");
        AssertEntity(database, typeof(FacultyAssistantRun), "faculty", "AssistantRuns");

        await using SqlConnection connection = new(fixture.ConnectionString);
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
            WHERE (s.name=N'hr' AND t.name IN (N'EvidenceDossiers',N'DossierReviewActions'))
               OR (s.name=N'faculty' AND t.name IN (N'AssistantContextVersions',N'AssistantRuns'));
            """;
        Assert.Equal(4, Convert.ToInt32(await command.ExecuteScalarAsync()));
    }

    private static void AssertEntity(AnalysisDbContext database, Type type, string schema, string table)
    {
        IEntityType entity = database.Model.FindEntityType(type)!;
        Assert.Equal(schema, entity.GetSchema());
        Assert.Equal(table, entity.GetTableName());
    }
}
