using FluentMigrator;
using System.Data;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data.Migrations;

[Migration(202608270001, "Add Google Scholar profiles and works")]
public sealed class AddGoogleScholar : Migration
{
    public override void Up()
    {
        Alter.Table("Researchers")
            .AddColumn("GoogleScholarId").AsString(32).Nullable();

        Create.Table("GoogleScholarProfiles")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("ResearcherId").AsInt32().NotNullable()
                .ForeignKey(
                    "FK_GoogleScholarProfiles_Researchers_ResearcherId",
                    "Researchers",
                    "Id")
                .OnDelete(Rule.Cascade)
            .WithColumn("DisplayName").AsString(500).Nullable()
            .WithColumn("Affiliations").AsString(2000).Nullable()
            .WithColumn("University").AsString(1000).Nullable()
            .WithColumn("VerifiedEmail").AsString(500).Nullable()
            .WithColumn("ProfileUrl").AsString(2000).Nullable()
            .WithColumn("CitationCount").AsInt32().Nullable()
            .WithColumn("CitationCountRecent").AsInt32().Nullable()
            .WithColumn("HIndex").AsInt32().Nullable()
            .WithColumn("HIndexRecent").AsInt32().Nullable()
            .WithColumn("I10Index").AsInt32().Nullable()
            .WithColumn("I10IndexRecent").AsInt32().Nullable()
            .WithColumn("MetricsSinceYear").AsInt32().Nullable()
            .WithColumn("DocumentsCount").AsInt32().NotNullable()
            .WithColumn("LastUpdatedAt").AsDateTime().NotNullable()
            .WithColumn("InterestsJson").AsString(int.MaxValue).Nullable()
            .WithColumn("CitationHistogramJson").AsString(int.MaxValue).Nullable()
            .WithColumn("RawDataJson").AsString(int.MaxValue).Nullable();

        Create.Table("GoogleScholarWorks")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("GoogleScholarProfileId").AsInt32().NotNullable()
                .ForeignKey(
                    "FK_GoogleScholarWorks_GoogleScholarProfiles_GoogleScholarProfileId",
                    "GoogleScholarProfiles",
                    "Id")
                .OnDelete(Rule.Cascade)
            .WithColumn("CitationId").AsString(200).NotNullable()
            .WithColumn("Title").AsString(2000).Nullable()
            .WithColumn("Authors").AsString(4000).Nullable()
            .WithColumn("Publication").AsString(2000).Nullable()
            .WithColumn("PublicationYear").AsInt32().Nullable()
            .WithColumn("CitedByCount").AsInt32().Nullable()
            .WithColumn("Url").AsString(2000).Nullable()
            .WithColumn("RawDataJson").AsString(int.MaxValue).Nullable();

        Execute.Sql(
            "CREATE UNIQUE INDEX [IX_Researchers_GoogleScholarId] " +
            "ON [Researchers] ([GoogleScholarId]) " +
            "WHERE [GoogleScholarId] IS NOT NULL;");

        Create.Index("IX_GoogleScholarProfiles_ResearcherId")
            .OnTable("GoogleScholarProfiles")
            .OnColumn("ResearcherId")
            .Ascending()
            .WithOptions()
            .Unique();

        Create.Index("IX_GoogleScholarWorks_ProfileId_CitationId")
            .OnTable("GoogleScholarWorks")
            .OnColumn("GoogleScholarProfileId")
            .Ascending()
            .OnColumn("CitationId")
            .Ascending()
            .WithOptions()
            .Unique();
    }

    public override void Down()
    {
        Delete.Table("GoogleScholarWorks");
        Delete.Table("GoogleScholarProfiles");
        Delete.Index("IX_Researchers_GoogleScholarId").OnTable("Researchers");
        Delete.Column("GoogleScholarId").FromTable("Researchers");
    }
}
