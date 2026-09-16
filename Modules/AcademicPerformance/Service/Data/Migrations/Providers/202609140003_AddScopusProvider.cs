using System.Data;
using FluentMigrator;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data.Migrations.Providers;

[Migration(202609140003, "Add Scopus author profiles and works")]
public sealed class AddScopusProvider : Migration
{
    public override void Up()
    {
        Execute.Sql("IF SCHEMA_ID(N'scopus') IS NULL EXEC(N'CREATE SCHEMA [scopus] AUTHORIZATION [dbo]');");
        Alter.Table("Researchers").InSchema("core")
            .AddColumn("ScopusCitationCount").AsInt32().Nullable()
            .AddColumn("ScopusHIndex").AsInt32().Nullable()
            .AddColumn("ScopusDocumentsCount").AsInt32().Nullable()
            .AddColumn("ScopusMetricsUpdatedAt").AsDateTime2().Nullable();

        Create.Table("ScopusProfiles").InSchema("scopus")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("PersonelID").AsString(200).NotNullable()
                .ForeignKey("FK_ScopusProfiles_Researchers_PersonelID", "core", "Researchers", "PersonelID")
                .OnDelete(Rule.Cascade)
            .WithColumn("ScopusAuthorId").AsString(20).NotNullable()
            .WithColumn("DisplayName").AsString(500).Nullable()
            .WithColumn("CurrentAffiliation").AsString(1000).Nullable()
            .WithColumn("DocumentsCount").AsInt32().Nullable()
            .WithColumn("CitationCount").AsInt32().Nullable()
            .WithColumn("CitedByCount").AsInt32().Nullable()
            .WithColumn("HIndex").AsInt32().Nullable()
            .WithColumn("LastUpdatedAt").AsDateTime2().NotNullable()
            .WithColumn("RawDataJson").AsString(int.MaxValue).Nullable()
            .WithColumn("SearchPagesJson").AsString(int.MaxValue).Nullable();
        Create.Index("UX_ScopusProfiles_PersonelID").OnTable("ScopusProfiles").InSchema("scopus")
            .OnColumn("PersonelID").Ascending().WithOptions().Unique();
        Create.Index("UX_ScopusProfiles_AuthorId").OnTable("ScopusProfiles").InSchema("scopus")
            .OnColumn("ScopusAuthorId").Ascending().WithOptions().Unique();

        Create.Table("ScopusWorks").InSchema("scopus")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("ScopusProfileId").AsInt32().NotNullable()
                .ForeignKey("FK_ScopusWorks_ScopusProfiles_ScopusProfileId", "scopus", "ScopusProfiles", "Id")
                .OnDelete(Rule.Cascade)
            .WithColumn("ScopusWorkId").AsString(100).NotNullable()
            .WithColumn("Eid").AsString(100).Nullable()
            .WithColumn("Title").AsString(2000).Nullable()
            .WithColumn("PublicationYear").AsInt32().Nullable()
            .WithColumn("PublicationDate").AsDateTime2().Nullable()
            .WithColumn("Doi").AsString(500).Nullable()
            .WithColumn("WorkType").AsString(200).Nullable()
            .WithColumn("CitedByCount").AsInt32().Nullable()
            .WithColumn("Authors").AsString(int.MaxValue).Nullable()
            .WithColumn("SourceName").AsString(2000).Nullable()
            .WithColumn("Url").AsString(2000).Nullable()
            .WithColumn("IsOpenAccess").AsBoolean().Nullable()
            .WithColumn("RawDataJson").AsString(int.MaxValue).Nullable();
        Create.Index("UX_ScopusWorks_Profile_Work").OnTable("ScopusWorks").InSchema("scopus")
            .OnColumn("ScopusProfileId").Ascending().OnColumn("ScopusWorkId").Ascending()
            .WithOptions().Unique();
    }

    public override void Down()
    {
        Delete.Table("ScopusWorks").InSchema("scopus");
        Delete.Table("ScopusProfiles").InSchema("scopus");
        Delete.Column("ScopusMetricsUpdatedAt").FromTable("Researchers").InSchema("core");
        Delete.Column("ScopusDocumentsCount").FromTable("Researchers").InSchema("core");
        Delete.Column("ScopusHIndex").FromTable("Researchers").InSchema("core");
        Delete.Column("ScopusCitationCount").FromTable("Researchers").InSchema("core");
        Execute.Sql("IF SCHEMA_ID(N'scopus') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.objects WHERE schema_id=SCHEMA_ID(N'scopus')) DROP SCHEMA [scopus];");
    }
}
