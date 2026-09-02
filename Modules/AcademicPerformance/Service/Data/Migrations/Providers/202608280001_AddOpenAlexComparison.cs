using FluentMigrator;
using System.Data;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data.Migrations;

[Migration(202608280001, "Add separate OpenAlex comparison data")]
public sealed class AddOpenAlexComparison : Migration
{
    public override void Up()
    {
        Create.Table("OpenAlexProfiles")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("ResearcherId").AsInt32().NotNullable()
                .ForeignKey(
                    "FK_OpenAlexProfiles_Researchers_ResearcherId",
                    "Researchers",
                    "Id")
                .OnDelete(Rule.Cascade)
            .WithColumn("OpenAlexAuthorId").AsString(100).NotNullable()
            .WithColumn("DisplayName").AsString(500).Nullable()
            .WithColumn("LastKnownInstitution").AsString(1000).Nullable()
            .WithColumn("WorksCount").AsInt32().NotNullable()
            .WithColumn("CitedByCount").AsInt32().NotNullable()
            .WithColumn("HIndex").AsInt32().Nullable()
            .WithColumn("I10Index").AsInt32().Nullable()
            .WithColumn("TwoYearMeanCitedness").AsDecimal(18, 4).Nullable()
            .WithColumn("LastUpdatedAt").AsDateTime().NotNullable()
            .WithColumn("CountsByYearJson").AsString(int.MaxValue).Nullable()
            .WithColumn("RawDataJson").AsString(int.MaxValue).Nullable()
            .WithColumn("WorksPagesJson").AsString(int.MaxValue).Nullable();

        Create.Table("OpenAlexWorks")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("OpenAlexProfileId").AsInt32().NotNullable()
                .ForeignKey(
                    "FK_OpenAlexWorks_OpenAlexProfiles_OpenAlexProfileId",
                    "OpenAlexProfiles",
                    "Id")
                .OnDelete(Rule.Cascade)
            .WithColumn("OpenAlexWorkId").AsString(100).NotNullable()
            .WithColumn("Title").AsString(2000).Nullable()
            .WithColumn("PublicationYear").AsInt32().Nullable()
            .WithColumn("PublicationDate").AsDateTime().Nullable()
            .WithColumn("Doi").AsString(500).Nullable()
            .WithColumn("WorkType").AsString(100).Nullable()
            .WithColumn("CitedByCount").AsInt32().NotNullable()
            .WithColumn("Authors").AsString(4000).Nullable()
            .WithColumn("SourceName").AsString(2000).Nullable()
            .WithColumn("Url").AsString(2000).Nullable()
            .WithColumn("OpenAccessUrl").AsString(2000).Nullable()
            .WithColumn("RawDataJson").AsString(int.MaxValue).Nullable();

        Create.Index("IX_OpenAlexProfiles_ResearcherId")
            .OnTable("OpenAlexProfiles")
            .OnColumn("ResearcherId")
            .Ascending()
            .WithOptions()
            .Unique();

        Create.Index("IX_OpenAlexProfiles_OpenAlexAuthorId")
            .OnTable("OpenAlexProfiles")
            .OnColumn("OpenAlexAuthorId")
            .Ascending()
            .WithOptions()
            .Unique();

        Create.Index("IX_OpenAlexWorks_ProfileId_WorkId")
            .OnTable("OpenAlexWorks")
            .OnColumn("OpenAlexProfileId")
            .Ascending()
            .OnColumn("OpenAlexWorkId")
            .Ascending()
            .WithOptions()
            .Unique();
    }

    public override void Down()
    {
        Delete.Table("OpenAlexWorks");
        Delete.Table("OpenAlexProfiles");
    }
}
