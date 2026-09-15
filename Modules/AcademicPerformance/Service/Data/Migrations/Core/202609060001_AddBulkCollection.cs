using FluentMigrator;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data.Migrations.Core;

[Migration(202609060001)]
public sealed class AddBulkCollection : Migration
{
    public override void Up()
    {
        Create.Table("BulkCollectionBatches").InSchema("bulk")
            .WithColumn("Id").AsGuid().PrimaryKey()
            .WithColumn("InputHash").AsString(64).NotNullable()
            .WithColumn("CreatedAt").AsDateTime2().NotNullable();
        Create.Table("BulkCollectionJobs").InSchema("bulk")
            .WithColumn("Id").AsInt64().PrimaryKey().Identity()
            .WithColumn("BatchId").AsGuid().NotNullable()
                .ForeignKey("FK_BulkCollectionItems_BulkCollectionBatches_BatchId", "bulk", "BulkCollectionBatches", "Id")
            .WithColumn("PersonelID").AsString(200).NotNullable()
            .WithColumn("InputJson").AsString(int.MaxValue).NotNullable()
            .WithColumn("Status").AsString(20).NotNullable()
            .WithColumn("Attempts").AsInt32().NotNullable()
            .WithColumn("NextAttemptAt").AsDateTime2().NotNullable()
            .WithColumn("StartedAt").AsDateTime2().Nullable()
            .WithColumn("CompletedAt").AsDateTime2().Nullable()
            .WithColumn("ResultMessage").AsString(1000).Nullable();
        Create.Index("IX_BulkCollectionJobs_Queue").OnTable("BulkCollectionJobs").InSchema("bulk")
            .OnColumn("Status").Ascending().OnColumn("NextAttemptAt").Ascending();
        Create.Index("IX_BulkCollectionJobs_Batch").OnTable("BulkCollectionJobs").InSchema("bulk")
            .OnColumn("BatchId").Ascending().OnColumn("Id").Ascending();
        Create.Table("ProviderRequestBudgets").InSchema("integrations")
            .WithColumn("Provider").AsString(40).PrimaryKey()
            .WithColumn("NextAllowedAt").AsDateTime2().NotNullable()
            .WithColumn("BudgetDate").AsDate().NotNullable()
            .WithColumn("RequestsToday").AsInt32().NotNullable();
    }

    public override void Down()
    {
        Delete.Table("ProviderRequestBudgets").InSchema("integrations");
        Delete.Table("BulkCollectionJobs").InSchema("bulk");
        Delete.Table("BulkCollectionBatches").InSchema("bulk");
    }
}
