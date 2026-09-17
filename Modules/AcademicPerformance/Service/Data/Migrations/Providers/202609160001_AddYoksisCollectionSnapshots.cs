using System.Data;
using FluentMigrator;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data.Migrations.Providers;

[Migration(202609160001, "Add durable YOKSIS successful collection snapshots")]
public sealed class AddYoksisCollectionSnapshots : Migration
{
    public override void Up()
    {
        Create.Table("YoksisCollectionSnapshots").InSchema("yoksis")
            .WithColumn("PersonelID").AsString(200).PrimaryKey().NotNullable()
                .ForeignKey(
                    "FK_YoksisCollectionSnapshots_Researchers_PersonelID",
                    "core",
                    "Researchers",
                    "PersonelID")
                .OnDelete(Rule.Cascade)
            .WithColumn("TcKimlikNoHash").AsString(64).NotNullable()
            .WithColumn("CompletedAtUtc").AsDateTime2().NotNullable()
            .WithColumn("ResponseJson").AsString(int.MaxValue).NotNullable();
    }

    public override void Down()
    {
        Delete.Table("YoksisCollectionSnapshots").InSchema("yoksis");
    }
}
