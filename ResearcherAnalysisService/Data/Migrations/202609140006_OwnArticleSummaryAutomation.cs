using FluentMigrator;

namespace ResearcherAnalysisService.Data.Migrations;

[Migration(202609140006, "Add durable automatic article summary queue")]
public sealed class OwnArticleSummaryAutomation : Migration
{
    public override void Up()
    {
        if (AnalysisMigrationGuard.IsCompleteOrAbsent("article summary automation",
            Schema.Schema("analysis").Table("ArticleSummaryAutomationJobs").Exists(),
            Schema.Schema("analysis").Table("CanonicalArticleAnalysisRuns")
                .Column("PolicyVersion").Exists()))
            return;

        Alter.Table("CanonicalArticleAnalysisRuns").InSchema("analysis")
            .AddColumn("PolicyVersion").AsString(100).Nullable();

        Create.Table("ArticleSummaryAutomationJobs").InSchema("analysis")
            .WithColumn("Id").AsInt64().PrimaryKey().Identity()
            .WithColumn("CanonicalWorkId").AsInt32().NotNullable()
            .WithColumn("Language").AsString(20).NotNullable()
            .WithColumn("Status").AsString(20).NotNullable()
            .WithColumn("DesiredInputHash").AsString(64).NotNullable()
            .WithColumn("DesiredPolicyVersion").AsString(100).NotNullable()
            .WithColumn("RunningInputHash").AsString(64).Nullable()
            .WithColumn("RunningPolicyVersion").AsString(100).Nullable()
            .WithColumn("ProcessedInputHash").AsString(64).Nullable()
            .WithColumn("ProcessedPolicyVersion").AsString(100).Nullable()
            .WithColumn("ExecutionToken").AsGuid().Nullable()
            .WithColumn("AttemptGeneration").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("Attempts").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("NextAttemptAt").AsDateTime2().NotNullable()
            .WithColumn("StartedAt").AsDateTime2().Nullable()
            .WithColumn("CompletedAt").AsDateTime2().Nullable()
            .WithColumn("UpdatedAt").AsDateTime2().NotNullable()
            .WithColumn("LastOutcomeCode").AsString(50).Nullable()
            .WithColumn("LastOutcomeMessage").AsString(500).Nullable()
            .WithColumn("LastSuccessfulAnalysisRunId").AsInt64().Nullable();

        Execute.Sql("""
            ALTER TABLE [analysis].[ArticleSummaryAutomationJobs]
                ALTER COLUMN [Language] nvarchar(20) COLLATE Latin1_General_100_BIN2 NOT NULL;
            """);
        Create.ForeignKey("FK_ArticleSummaryAutomationJobs_LastSuccessfulRun")
            .FromTable("ArticleSummaryAutomationJobs").InSchema("analysis")
            .ForeignColumn("LastSuccessfulAnalysisRunId")
            .ToTable("CanonicalArticleAnalysisRuns").InSchema("analysis")
            .PrimaryColumn("Id");
        Create.Index("UX_ArticleSummaryAutomationJobs_Work_Language")
            .OnTable("ArticleSummaryAutomationJobs").InSchema("analysis")
            .OnColumn("CanonicalWorkId").Ascending()
            .OnColumn("Language").Ascending().WithOptions().Unique();
        Create.Index("IX_ArticleSummaryAutomationJobs_Status_NextAttemptAt")
            .OnTable("ArticleSummaryAutomationJobs").InSchema("analysis")
            .OnColumn("Status").Ascending()
            .OnColumn("NextAttemptAt").Ascending()
            .OnColumn("Id").Ascending();
    }

    public override void Down()
    {
        Delete.Table("ArticleSummaryAutomationJobs").InSchema("analysis");
        Delete.Column("PolicyVersion").FromTable("CanonicalArticleAnalysisRuns").InSchema("analysis");
    }
}
