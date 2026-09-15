using FluentMigrator;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data.Migrations.Core;

[Migration(202609140002, "Add the collector-owned normalized-data change feed")]
public sealed class AddCollectionChanges : Migration
{
    public override void Up()
    {
        Create.Table("CollectionChanges").InSchema("core")
            .WithColumn("EventId").AsGuid().PrimaryKey()
            .WithColumn("ChangeKind").AsString(40).NotNullable()
            .WithColumn("PersonelID").AsString(200).Nullable()
            .WithColumn("CanonicalWorkId").AsInt32().Nullable()
            .WithColumn("AcademicWorkId").AsInt32().Nullable()
            .WithColumn("OccurredAtUtc").AsDateTime2().NotNullable()
            .WithColumn("PayloadVersion").AsInt32().NotNullable().WithDefaultValue(1);

        Create.Index("IX_CollectionChanges_OccurredAtUtc_EventId")
            .OnTable("CollectionChanges").InSchema("core")
            .OnColumn("OccurredAtUtc").Ascending()
            .OnColumn("EventId").Ascending();
        Create.Index("IX_CollectionChanges_PersonelID_OccurredAtUtc")
            .OnTable("CollectionChanges").InSchema("core")
            .OnColumn("PersonelID").Ascending()
            .OnColumn("OccurredAtUtc").Ascending();

    }

    public override void Down() => Delete.Table("CollectionChanges").InSchema("core");
}
