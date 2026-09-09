using FluentMigrator;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data.Migrations.Core;

[Migration(202609090002)]
public sealed class WidenPublicationAuthors : Migration
{
    public override void Up()
    {
        Alter.Column("Authors").OnTable("OrcidWorks").AsString(int.MaxValue).Nullable();
        Alter.Column("Authors").OnTable("AcademicWorks").AsString(int.MaxValue).Nullable();
        Alter.Column("Authors").OnTable("PublicationSummaries").AsString(int.MaxValue).Nullable();
    }

    public override void Down()
    {
        Alter.Column("Authors").OnTable("OrcidWorks").AsString(4000).Nullable();
        Alter.Column("Authors").OnTable("AcademicWorks").AsString(4000).Nullable();
        Alter.Column("Authors").OnTable("PublicationSummaries").AsString(4000).Nullable();
    }
}
