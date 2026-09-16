using FluentMigrator;

namespace ResearcherAnalysisService.Data.Migrations;

[Migration(202609140014, "Add durable staged article review checkpoints")]
public sealed class OwnArticleReviewCheckpoints : Migration
{
    public override void Up()
    {
Alter.Table("CanonicalArticleReviewRuns").InSchema("analysis")
            .AddColumn("SettingsFingerprint").AsString(64).Nullable();
        Execute.Sql("ALTER TABLE [analysis].[CanonicalArticleReviewRuns] ALTER COLUMN [SettingsFingerprint] nvarchar(64) COLLATE Latin1_General_100_BIN2 NULL;");

        Create.Table("ArticleReviewWorkItems").InSchema("analysis")
            .WithColumn("Id").AsInt64().PrimaryKey().Identity()
            .WithColumn("WorkKey").AsString(64).NotNullable()
            .WithColumn("CanonicalWorkId").AsInt32().NotNullable()
            .WithColumn("BaseAnalysisRunId").AsInt64().NotNullable()
            .WithColumn("ArticleSourceSnapshotId").AsInt64().NotNullable()
            .WithColumn("Language").AsString(20).NotNullable()
            .WithColumn("PolicyVersion").AsString(100).NotNullable()
            .WithColumn("SettingsFingerprint").AsString(64).NotNullable()
            .WithColumn("GenerationNonce").AsGuid().NotNullable()
            .WithColumn("Status").AsString(40).NotNullable()
            .WithColumn("MaximumCalls").AsInt32().NotNullable()
            .WithColumn("MaximumSpendUsd").AsDecimal(19, 9).NotNullable()
            .WithColumn("ConfigurationJson").AsString(4000).NotNullable()
            .WithColumn("SourceSnapshotJson").AsString(int.MaxValue).NotNullable()
            .WithColumn("CreatedAt").AsDateTimeOffset().NotNullable()
            .WithColumn("UpdatedAt").AsDateTimeOffset().NotNullable();
        Execute.Sql("ALTER TABLE [analysis].[ArticleReviewWorkItems] ALTER COLUMN [WorkKey] nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL; ALTER TABLE [analysis].[ArticleReviewWorkItems] ALTER COLUMN [Language] nvarchar(20) COLLATE Latin1_General_100_BIN2 NOT NULL; ALTER TABLE [analysis].[ArticleReviewWorkItems] ALTER COLUMN [PolicyVersion] nvarchar(100) COLLATE Latin1_General_100_BIN2 NOT NULL; ALTER TABLE [analysis].[ArticleReviewWorkItems] ALTER COLUMN [SettingsFingerprint] nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL;");
        Create.ForeignKey("FK_ArticleReviewWorkItems_BaseRuns").FromTable("ArticleReviewWorkItems").InSchema("analysis")
            .ForeignColumn("BaseAnalysisRunId").ToTable("CanonicalArticleAnalysisRuns").InSchema("analysis").PrimaryColumn("Id");
        Create.ForeignKey("FK_ArticleReviewWorkItems_Sources").FromTable("ArticleReviewWorkItems").InSchema("analysis")
            .ForeignColumn("ArticleSourceSnapshotId").ToTable("ArticleSourceSnapshots").InSchema("analysis").PrimaryColumn("Id");
        Create.Index("UX_ArticleReviewWorkItems_WorkKey").OnTable("ArticleReviewWorkItems").InSchema("analysis")
            .OnColumn("WorkKey").Ascending().WithOptions().Unique();
        Create.Index("IX_ArticleReviewWorkItems_Resume").OnTable("ArticleReviewWorkItems").InSchema("analysis")
            .OnColumn("BaseAnalysisRunId").Ascending().OnColumn("Language").Ascending()
            .OnColumn("PolicyVersion").Ascending().OnColumn("SettingsFingerprint").Ascending()
            .OnColumn("Id").Descending();

        Create.Table("ArticleReviewStageCheckpoints").InSchema("analysis")
            .WithColumn("Id").AsInt64().PrimaryKey().Identity()
            .WithColumn("ArticleReviewWorkItemId").AsInt64().NotNullable()
            .WithColumn("Stage").AsString(30).NotNullable()
            .WithColumn("Role").AsString(30).NotNullable()
            .WithColumn("BatchKey").AsString(64).NotNullable()
            .WithColumn("ParentBatchKey").AsString(64).Nullable()
            .WithColumn("Ordinal").AsInt32().NotNullable()
            .WithColumn("Status").AsString(40).NotNullable()
            .WithColumn("RequestJson").AsString(int.MaxValue).NotNullable()
            .WithColumn("ResultJson").AsString(int.MaxValue).Nullable()
            .WithColumn("AttemptId").AsGuid().Nullable()
            .WithColumn("RequestFingerprint").AsString(64).Nullable()
            .WithColumn("ReservedCostUsd").AsDecimal(19, 9).Nullable()
            .WithColumn("ActualCostUsd").AsDecimal(19, 9).Nullable()
            .WithColumn("PricingVersion").AsString(100).Nullable()
            .WithColumn("ErrorCode").AsString(100).Nullable()
            .WithColumn("CreatedAt").AsDateTimeOffset().NotNullable()
            .WithColumn("UpdatedAt").AsDateTimeOffset().NotNullable();
        Execute.Sql("ALTER TABLE [analysis].[ArticleReviewStageCheckpoints] ALTER COLUMN [BatchKey] nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL; ALTER TABLE [analysis].[ArticleReviewStageCheckpoints] ALTER COLUMN [ParentBatchKey] nvarchar(64) COLLATE Latin1_General_100_BIN2 NULL; ALTER TABLE [analysis].[ArticleReviewStageCheckpoints] ALTER COLUMN [RequestFingerprint] nvarchar(64) COLLATE Latin1_General_100_BIN2 NULL;");
        Create.ForeignKey("FK_ArticleReviewStageCheckpoints_WorkItems")
            .FromTable("ArticleReviewStageCheckpoints").InSchema("analysis").ForeignColumn("ArticleReviewWorkItemId")
            .ToTable("ArticleReviewWorkItems").InSchema("analysis").PrimaryColumn("Id").OnDelete(System.Data.Rule.Cascade);
        Create.Index("UX_ArticleReviewStageCheckpoints_Unit").OnTable("ArticleReviewStageCheckpoints").InSchema("analysis")
            .OnColumn("ArticleReviewWorkItemId").Ascending().OnColumn("Stage").Ascending()
            .OnColumn("Role").Ascending().OnColumn("BatchKey").Ascending().WithOptions().Unique();
        Execute.Sql("CREATE UNIQUE INDEX [UX_ArticleReviewStageCheckpoints_AttemptId] ON [analysis].[ArticleReviewStageCheckpoints] ([AttemptId]) WHERE [AttemptId] IS NOT NULL;");
    }

    public override void Down()
    {
        Delete.Table("ArticleReviewStageCheckpoints").InSchema("analysis");
        Delete.Table("ArticleReviewWorkItems").InSchema("analysis");
        Delete.Column("SettingsFingerprint").FromTable("CanonicalArticleReviewRuns").InSchema("analysis");
    }
}
