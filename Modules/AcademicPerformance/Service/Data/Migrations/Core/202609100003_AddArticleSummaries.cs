using FluentMigrator;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data.Migrations.Core;

[Migration(202609100003)]
public sealed class AddArticleSummaries : Migration
{
    public override void Up()
    {
        Create.Table("ArticleSummaries")
            .WithColumn("Id").AsInt64().PrimaryKey().Identity()
            .WithColumn("AcademicWorkId").AsInt32().Nullable()
            .WithColumn("OriginalAcademicWorkId").AsInt32().NotNullable()
            .WithColumn("PersonelID").AsString(200).NotNullable().ForeignKey("Researchers", "PersonelID")
            .WithColumn("SavedAt").AsDateTimeOffset().NotNullable()
            .WithColumn("SourceUrl").AsString(2000).Nullable()
            .WithColumn("SourceHash").AsString(64).NotNullable()
            .WithColumn("SourceKind").AsString(20).NotNullable()
            .WithColumn("ExtractionVersion").AsString(100).NotNullable()
            .WithColumn("SnapshotJson").AsString(int.MaxValue).NotNullable()
            .WithColumn("ReportJson").AsString(int.MaxValue).NotNullable();
        Create.ForeignKey("FK_ArticleSummaries_AcademicWorks_AcademicWorkId").FromTable("ArticleSummaries")
            .ForeignColumn("AcademicWorkId").ToTable("AcademicWorks").PrimaryColumn("Id").OnDelete(System.Data.Rule.SetNull);
        Create.Index("IX_ArticleSummaries_PersonelID_OriginalAcademicWorkId_Id").OnTable("ArticleSummaries")
            .OnColumn("PersonelID").Ascending().OnColumn("OriginalAcademicWorkId").Ascending().OnColumn("Id").Descending();
    }
    public override void Down() => Delete.Table("ArticleSummaries");
}
