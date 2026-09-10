using FluentMigrator;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data.Migrations;

[Migration(202609100004, "Preserve academic work URL sources and consolidate canonical URLs")]
public sealed class ConsolidateAcademicWorkUrls : Migration
{
    public override void Up()
    {
        Create.Table("AcademicWorkSources")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("AcademicWorkId").AsInt32().NotNullable()
                .ForeignKey("FK_AcademicWorkSources_AcademicWorks_AcademicWorkId", "AcademicWorks", "Id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("Url").AsString(2000).NotNullable()
            .WithColumn("Kind").AsString(20).NotNullable()
            .WithColumn("Origin").AsString(100).NotNullable()
            .WithColumn("IsOpenAccess").AsBoolean().Nullable();
        Create.Index("IX_AcademicWorkSources_AcademicWorkId").OnTable("AcademicWorkSources").OnColumn("AcademicWorkId");

        Preserve("Link", "Legacy.Link", "Landing", "NULL");
        Preserve("SourceUrl", "Legacy.SourceUrl", "Unknown", "NULL");
        Preserve("OpenAccessUrl", "Legacy.OpenAccessUrl", "Unknown", "1");
        Preserve("FullTextUrl", "Legacy.FullTextUrl", "Pdf", "NULL");
        Execute.Sql("UPDATE AcademicWorks SET Link = SourceUrl WHERE NULLIF(LTRIM(RTRIM(Link)), '') IS NULL AND NULLIF(LTRIM(RTRIM(SourceUrl)), '') IS NOT NULL;");
        Delete.Column("SourceUrl").FromTable("AcademicWorks");
        Delete.Column("OpenAccessUrl").FromTable("AcademicWorks");
    }

    public override void Down()
    {
        Alter.Table("AcademicWorks").AddColumn("SourceUrl").AsString(2000).Nullable();
        Alter.Table("AcademicWorks").AddColumn("OpenAccessUrl").AsString(2000).Nullable();
        Execute.Sql("UPDATE w SET SourceUrl = s.Url FROM AcademicWorks w OUTER APPLY (SELECT TOP (1) Url FROM AcademicWorkSources WHERE AcademicWorkId=w.Id AND Origin='Legacy.SourceUrl' ORDER BY Id) s;");
        Execute.Sql("UPDATE w SET OpenAccessUrl = s.Url FROM AcademicWorks w OUTER APPLY (SELECT TOP (1) Url FROM AcademicWorkSources WHERE AcademicWorkId=w.Id AND Origin='Legacy.OpenAccessUrl' ORDER BY Id) s;");
        Delete.Table("AcademicWorkSources");
    }

    private void Preserve(string column, string origin, string kind, string openAccess) => Execute.Sql(
        $"INSERT INTO AcademicWorkSources (AcademicWorkId, Url, Kind, Origin, IsOpenAccess) " +
        $"SELECT Id, [{column}], '{kind}', '{origin}', {openAccess} FROM AcademicWorks " +
        $"WHERE NULLIF(LTRIM(RTRIM([{column}])), '') IS NOT NULL;");
}
