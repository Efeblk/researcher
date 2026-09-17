using FluentMigrator;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data.Migrations.Providers;

[Migration(202609170001)]
public sealed class AddTrDizinProjects : Migration
{
    public override void Up()
    {
        Alter.Table("TrDizinProfiles").InSchema("trdizin")
            .AddColumn("ProjectCandidateCount").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("ProjectMatchedCount").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("ProjectUnmatchedCount").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("ProjectSearchComplete").AsBoolean().NotNullable().WithDefaultValue(false)
            .AddColumn("RawProjectsJson").AsString(int.MaxValue).NotNullable().WithDefaultValue("[]");

        Create.Table("TrDizinProjects").InSchema("trdizin")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("TrDizinProfileId").AsInt32().NotNullable()
                .ForeignKey("FK_TrDizinProjects_TrDizinProfiles_TrDizinProfileId",
                    "trdizin", "TrDizinProfiles", "Id")
                .OnDelete(System.Data.Rule.Cascade)
            .WithColumn("ProjectId").AsString(100).NotNullable()
            .WithColumn("ProjectNumber").AsString(250).Nullable()
            .WithColumn("Title").AsString(2000).Nullable()
            .WithColumn("StartedDate").AsString(100).Nullable()
            .WithColumn("EndDate").AsString(100).Nullable()
            .WithColumn("ProjectGroup").AsString(1000).Nullable()
            .WithColumn("ResearchersJson").AsString(int.MaxValue).Nullable()
            .WithColumn("Duty").AsString(1000).Nullable()
            .WithColumn("AbstractsJson").AsString(int.MaxValue).Nullable()
            .WithColumn("KeywordsJson").AsString(int.MaxValue).Nullable()
            .WithColumn("OutputsJson").AsString(int.MaxValue).Nullable()
            .WithColumn("AttachmentsJson").AsString(int.MaxValue).Nullable()
            .WithColumn("RawDataJson").AsString(int.MaxValue).NotNullable();
        Create.UniqueConstraint("UQ_TrDizinProjects_Profile_Project")
            .OnTable("TrDizinProjects").WithSchema("trdizin")
            .Columns("TrDizinProfileId", "ProjectId");
    }

    public override void Down()
    {
        Delete.Table("TrDizinProjects").InSchema("trdizin");
        Delete.Column("RawProjectsJson").FromTable("TrDizinProfiles").InSchema("trdizin");
        Delete.Column("ProjectSearchComplete").FromTable("TrDizinProfiles").InSchema("trdizin");
        Delete.Column("ProjectUnmatchedCount").FromTable("TrDizinProfiles").InSchema("trdizin");
        Delete.Column("ProjectMatchedCount").FromTable("TrDizinProfiles").InSchema("trdizin");
        Delete.Column("ProjectCandidateCount").FromTable("TrDizinProfiles").InSchema("trdizin");
    }
}
