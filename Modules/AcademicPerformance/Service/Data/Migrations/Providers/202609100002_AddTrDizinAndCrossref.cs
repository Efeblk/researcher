using FluentMigrator;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data.Migrations.Providers;

[Migration(202609100002)]
public sealed class AddTrDizinAndCrossref : Migration
{
    public override void Up()
    {
        Create.Table("TrDizinProfiles")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("PersonelID").AsString(200).NotNullable()
                .ForeignKey("Researchers", "PersonelID").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("Orcid").AsString(19).NotNullable()
            .WithColumn("AuthorId").AsInt64().NotNullable()
            .WithColumn("DisplayName").AsString(500).Nullable()
            .WithColumn("PublicationCount").AsInt32().Nullable()
            .WithColumn("CitationCount").AsInt32().Nullable()
            .WithColumn("LastUpdatedAt").AsDateTime2().NotNullable()
            .WithColumn("RawAuthorJson").AsString(int.MaxValue).NotNullable()
            .WithColumn("RawPublicationsJson").AsString(int.MaxValue).NotNullable();
        Create.UniqueConstraint("UQ_TrDizinProfiles_PersonelID")
            .OnTable("TrDizinProfiles").Column("PersonelID");

        Create.Table("TrDizinWorks")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("TrDizinProfileId").AsInt32().NotNullable()
                .ForeignKey("TrDizinProfiles", "Id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("PublicationId").AsString(100).NotNullable()
            .WithColumn("Title").AsString(2000).Nullable()
            .WithColumn("Doi").AsString(500).Nullable()
            .WithColumn("PublicationYear").AsInt32().Nullable()
            .WithColumn("PublicationType").AsString(100).Nullable()
            .WithColumn("Authors").AsString(int.MaxValue).Nullable()
            .WithColumn("Journal").AsString(2000).Nullable()
            .WithColumn("CitationCount").AsInt32().Nullable()
            .WithColumn("RawDataJson").AsString(int.MaxValue).NotNullable();
        Create.UniqueConstraint("UQ_TrDizinWorks_Profile_Publication")
            .OnTable("TrDizinWorks").Columns("TrDizinProfileId", "PublicationId");

        Create.Table("CrossrefWorks")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("PersonelID").AsString(200).NotNullable()
                .ForeignKey("Researchers", "PersonelID").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("Doi").AsString(500).NotNullable()
            .WithColumn("Found").AsBoolean().NotNullable()
            .WithColumn("FetchedAt").AsDateTime2().NotNullable()
            .WithColumn("Title").AsString(2000).Nullable()
            .WithColumn("Authors").AsString(int.MaxValue).Nullable()
            .WithColumn("ContainerTitle").AsString(2000).Nullable()
            .WithColumn("Type").AsString(100).Nullable()
            .WithColumn("PublicationYear").AsInt32().Nullable()
            .WithColumn("PublicationDate").AsDateTime2().Nullable()
            .WithColumn("CitedByCount").AsInt32().Nullable()
            .WithColumn("Url").AsString(2000).Nullable()
            .WithColumn("RawDataJson").AsString(int.MaxValue).Nullable();
        Create.UniqueConstraint("UQ_CrossrefWorks_PersonelID_Doi")
            .OnTable("CrossrefWorks").Columns("PersonelID", "Doi");
    }
    public override void Down()
    {
        Delete.Table("CrossrefWorks");
        Delete.Table("TrDizinWorks");
        Delete.Table("TrDizinProfiles");
    }
}
