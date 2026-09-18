using FluentMigrator;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data.Migrations.Core;

[Migration(202609180001)]
public sealed class WidenPublicationSummaryVenue : Migration
{
    public override void Up()
    {
        Alter.Column("Publication").OnTable("PublicationSummaries").InSchema("core")
            .AsString(int.MaxValue).Nullable();
    }

    public override void Down()
    {
        Alter.Column("Publication").OnTable("PublicationSummaries").InSchema("core")
            .AsString(2000).Nullable();
    }
}
