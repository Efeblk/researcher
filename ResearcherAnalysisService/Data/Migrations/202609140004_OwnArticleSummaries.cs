using FluentMigrator;

namespace ResearcherAnalysisService.Data.Migrations;

[Migration(202609140004)]
public sealed class OwnArticleSummaries : Migration
{
    public override void Up()
    {
        if (Schema.Schema("analysis").Table("ArticleSummaries").Exists())
            return;

        Create.Table("ArticleSummaries").InSchema("analysis")
            .WithColumn("Id").AsInt64().PrimaryKey().Identity()
            .WithColumn("AcademicWorkId").AsInt32().Nullable()
            .WithColumn("OriginalAcademicWorkId").AsInt32().NotNullable()
            .WithColumn("PersonelID").AsString(200).NotNullable()
            .WithColumn("SavedAt").AsDateTimeOffset().NotNullable()
            .WithColumn("SourceUrl").AsString(2000).Nullable()
            .WithColumn("SourceHash").AsString(64).NotNullable()
            .WithColumn("SourceKind").AsString(20).NotNullable()
            .WithColumn("ExtractionVersion").AsString(100).NotNullable()
            .WithColumn("SnapshotJson").AsString(int.MaxValue).NotNullable()
            .WithColumn("ReportJson").AsString(int.MaxValue).NotNullable();
        Create.Index("IX_ArticleSummaries_PersonelID_OriginalAcademicWorkId_Id").OnTable("ArticleSummaries").InSchema("analysis")
            .OnColumn("PersonelID").Ascending().OnColumn("OriginalAcademicWorkId").Ascending().OnColumn("Id").Descending();
    }
    public override void Down() => Delete.Table("ArticleSummaries").InSchema("analysis");
}
