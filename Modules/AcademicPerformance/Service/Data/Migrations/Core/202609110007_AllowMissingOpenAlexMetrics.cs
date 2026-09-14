using FluentMigrator;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data.Migrations.Core;

[Migration(202609110007, "Allow unavailable OpenAlex profile metrics")]
public sealed class AllowMissingOpenAlexMetrics : Migration
{
    public override void Up()
    {
        Alter.Column("WorksCount").OnTable("OpenAlexProfiles").InSchema("openalex")
            .AsInt32().Nullable();
        Alter.Column("CitedByCount").OnTable("OpenAlexProfiles").InSchema("openalex")
            .AsInt32().Nullable();
    }

    public override void Down()
    {
        Execute.Sql("""
            UPDATE [openalex].[OpenAlexProfiles]
            SET [WorksCount] = COALESCE([WorksCount], 0),
                [CitedByCount] = COALESCE([CitedByCount], 0);
            """);
        Alter.Column("WorksCount").OnTable("OpenAlexProfiles").InSchema("openalex")
            .AsInt32().NotNullable();
        Alter.Column("CitedByCount").OnTable("OpenAlexProfiles").InSchema("openalex")
            .AsInt32().NotNullable();
    }
}
