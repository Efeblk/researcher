using FluentMigrator;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data.Migrations.Core;

[Migration(202609090002)]
public sealed class WidenPublicationAuthors : Migration
{
    public override void Up()
    {
        Alter.Column("Authors").OnTable("OrcidWorks").InSchema("orcid").AsString(int.MaxValue).Nullable();
        Alter.Column("Authors").OnTable("AcademicWorks").InSchema("core").AsString(int.MaxValue).Nullable();
        Alter.Column("Authors").OnTable("PublicationSummaries").InSchema("core").AsString(int.MaxValue).Nullable();
    }

    public override void Down()
    {
        Alter.Column("Authors").OnTable("OrcidWorks").InSchema("orcid").AsString(4000).Nullable();
        Alter.Column("Authors").OnTable("AcademicWorks").InSchema("core").AsString(4000).Nullable();
        Alter.Column("Authors").OnTable("PublicationSummaries").InSchema("core").AsString(4000).Nullable();
    }
}
