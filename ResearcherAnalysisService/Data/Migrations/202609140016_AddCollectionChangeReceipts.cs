using FluentMigrator;

namespace ResearcherAnalysisService.Data.Migrations;

[Migration(202609140016, "Add analysis-owned collection change receipts")]
public sealed class AddCollectionChangeReceipts : Migration
{
    public override void Up()
    {
        if (Schema.Schema("analysis").Table("CollectionChangeReceipts").Exists())
            return;

        Create.Table("CollectionChangeReceipts").InSchema("analysis")
            .WithColumn("EventId").AsGuid().PrimaryKey()
            .WithColumn("ReceivedAtUtc").AsDateTime2().NotNullable()
            .WithColumn("ScheduledAtUtc").AsDateTime2().Nullable();
    }

    public override void Down() => Delete.Table("CollectionChangeReceipts").InSchema("analysis");
}
