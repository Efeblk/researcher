using FluentMigrator;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data.Migrations;

[Migration(202609100004, "Create academic work URL sources and remove superseded URL columns")]
public sealed class ConsolidateAcademicWorkUrls : Migration
{
    public override void Up()
    {
        Create.Table("AcademicWorkSources").InSchema("core")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("AcademicWorkId").AsInt32().NotNullable()
                .ForeignKey("FK_AcademicWorkSources_AcademicWorks_AcademicWorkId", "core", "AcademicWorks", "Id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("Url").AsString(2000).NotNullable()
            .WithColumn("Kind").AsString(20).NotNullable()
            .WithColumn("Origin").AsString(100).NotNullable()
            .WithColumn("IsOpenAccess").AsBoolean().Nullable();
        Create.Index("IX_AcademicWorkSources_AcademicWorkId").OnTable("AcademicWorkSources").InSchema("core").OnColumn("AcademicWorkId");

        Delete.Column("SourceUrl").FromTable("AcademicWorks").InSchema("core");
        Delete.Column("OpenAccessUrl").FromTable("AcademicWorks").InSchema("core");
    }

    public override void Down()
    {
        Alter.Table("AcademicWorks").InSchema("core").AddColumn("SourceUrl").AsString(2000).Nullable();
        Alter.Table("AcademicWorks").InSchema("core").AddColumn("OpenAccessUrl").AsString(2000).Nullable();
        Delete.Table("AcademicWorkSources").InSchema("core");
    }
}
