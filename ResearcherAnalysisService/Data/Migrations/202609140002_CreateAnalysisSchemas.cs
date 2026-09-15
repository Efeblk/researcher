using FluentMigrator;

namespace ResearcherAnalysisService.Data.Migrations;

[Migration(202609140002, "Create analysis-owned schemas")]
public sealed class CreateAnalysisSchemas : Migration
{
    public override void Up() => Execute.Sql("""
        IF SCHEMA_ID(N'analysis') IS NULL EXEC(N'CREATE SCHEMA [analysis] AUTHORIZATION [dbo]');
        IF SCHEMA_ID(N'hr') IS NULL EXEC(N'CREATE SCHEMA [hr] AUTHORIZATION [dbo]');
        IF SCHEMA_ID(N'faculty') IS NULL EXEC(N'CREATE SCHEMA [faculty] AUTHORIZATION [dbo]');
        """);

    public override void Down() { }
}

