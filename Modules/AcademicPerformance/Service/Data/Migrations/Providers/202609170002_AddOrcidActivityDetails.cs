using FluentMigrator;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data.Migrations.Providers;

[Migration(202609170002, "Add ORCID activity detail snapshot")]
public sealed class AddOrcidActivityDetails : Migration
{
    public override void Up()
    {
        Alter.Table("OrcidProfiles").InSchema("orcid")
            .AddColumn("ActivitiesDetailsJson").AsString(int.MaxValue).Nullable();
    }

    public override void Down()
    {
        Delete.Column("ActivitiesDetailsJson")
            .FromTable("OrcidProfiles").InSchema("orcid");
    }
}
