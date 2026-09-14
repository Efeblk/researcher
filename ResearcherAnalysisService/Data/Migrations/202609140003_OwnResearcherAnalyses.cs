using FluentMigrator;

namespace ResearcherAnalysisService.Data.Migrations;

[Migration(202609140003)]
public sealed class OwnResearcherAnalyses : Migration
{
    public override void Up()
    {
        if (Schema.Schema("analysis").Table("ResearcherAnalyses").Exists())
            return;

        Create.Table("ResearcherAnalyses").InSchema("analysis")
            .WithColumn("Id").AsInt64().PrimaryKey().Identity()
            .WithColumn("PersonelID").AsString(200).NotNullable()
            .WithColumn("SavedAt").AsDateTimeOffset().NotNullable()
            .WithColumn("SnapshotJson").AsString(int.MaxValue).NotNullable()
            .WithColumn("ReportJson").AsString(int.MaxValue).NotNullable();
        Create.Index("IX_ResearcherAnalyses_PersonelID_Id").OnTable("ResearcherAnalyses").InSchema("analysis")
            .OnColumn("PersonelID").Ascending().OnColumn("Id").Descending();
    }

    public override void Down() => Delete.Table("ResearcherAnalyses").InSchema("analysis");
}
