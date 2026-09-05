using FluentMigrator;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data.Migrations.Core;

[Migration(202609060001)]
public sealed class AddBulkCollection : Migration
{
    public override void Up()
    {
        Create.Table("BulkCollectionBatches")
            .WithColumn("Id").AsGuid().PrimaryKey()
            .WithColumn("InputHash").AsString(64).NotNullable()
            .WithColumn("CreatedAt").AsDateTime2().NotNullable();
        Create.Table("BulkCollectionJobs")
            .WithColumn("Id").AsInt64().PrimaryKey().Identity()
            .WithColumn("BatchId").AsGuid().NotNullable()
                .ForeignKey("BulkCollectionBatches", "Id")
            .WithColumn("SourceResearcherId").AsString(200).NotNullable()
            .WithColumn("InputJson").AsString(int.MaxValue).NotNullable()
            .WithColumn("Status").AsString(20).NotNullable()
            .WithColumn("Attempts").AsInt32().NotNullable()
            .WithColumn("NextAttemptAt").AsDateTime2().NotNullable()
            .WithColumn("StartedAt").AsDateTime2().Nullable()
            .WithColumn("CompletedAt").AsDateTime2().Nullable()
            .WithColumn("ResearcherId").AsInt32().Nullable()
            .WithColumn("ResultMessage").AsString(1000).Nullable();
        Create.Index("IX_BulkCollectionJobs_Queue").OnTable("BulkCollectionJobs")
            .OnColumn("Status").Ascending().OnColumn("NextAttemptAt").Ascending();
        Create.Index("IX_BulkCollectionJobs_Batch").OnTable("BulkCollectionJobs")
            .OnColumn("BatchId").Ascending().OnColumn("Id").Ascending();
        Create.Table("ProviderRequestBudgets")
            .WithColumn("Provider").AsString(40).PrimaryKey()
            .WithColumn("NextAllowedAt").AsDateTime2().NotNullable()
            .WithColumn("BudgetDate").AsDate().NotNullable()
            .WithColumn("RequestsToday").AsInt32().NotNullable();
    }

    public override void Down()
    {
        Delete.Table("ProviderRequestBudgets");
        Delete.Table("BulkCollectionJobs");
        Delete.Table("BulkCollectionBatches");
    }
}
