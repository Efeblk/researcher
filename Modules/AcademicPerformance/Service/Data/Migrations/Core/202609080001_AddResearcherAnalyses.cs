using FluentMigrator;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data.Migrations.Core;

[Migration(202609080001)]
public sealed class AddResearcherAnalyses : Migration
{
    public override void Up()
    {
        Create.Table("ResearcherAnalyses")
            .WithColumn("Id").AsInt64().PrimaryKey().Identity()
            .WithColumn("ResearcherId").AsInt32().NotNullable().ForeignKey("Researchers", "Id")
            .WithColumn("SavedAt").AsDateTimeOffset().NotNullable()
            .WithColumn("SnapshotJson").AsString(int.MaxValue).NotNullable()
            .WithColumn("ReportJson").AsString(int.MaxValue).NotNullable();
        Create.Index("IX_ResearcherAnalyses_ResearcherId_Id").OnTable("ResearcherAnalyses")
            .OnColumn("ResearcherId").Ascending().OnColumn("Id").Descending();
    }

    public override void Down() => Delete.Table("ResearcherAnalyses");
}
