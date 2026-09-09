using FluentMigrator;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data.Migrations.Core;

[Migration(202609090001)]
public sealed class AddProviderStatusObservations : Migration
{
    public override void Up()
    {
        Create.Table("ProviderStatusObservations")
            .WithColumn("Provider").AsString(50).PrimaryKey()
            .WithColumn("ObservedAt").AsDateTime2().NotNullable()
            .WithColumn("ExpiresAt").AsDateTime2().NotNullable()
            .WithColumn("PayloadJson").AsString(int.MaxValue).NotNullable();
    }

    public override void Down() => Delete.Table("ProviderStatusObservations");
}
