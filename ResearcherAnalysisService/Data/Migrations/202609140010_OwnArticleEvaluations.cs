using FluentMigrator;

namespace ResearcherAnalysisService.Data.Migrations;

[Migration(202609140010)]
public sealed class OwnArticleEvaluations : Migration
{
    public override void Up()
    {
        if (AnalysisMigrationGuard.IsCompleteOrAbsent("article evaluations",
            Schema.Schema("analysis").Table("ArticleEvaluationRuns").Exists(),
            Schema.Schema("analysis").Table("ArticleEvaluationCases").Exists(),
            Schema.Schema("analysis").Table("ArticleEvaluationWorkItems").Exists(),
            Schema.Schema("analysis").Table("ArticleEvaluationAttempts").Exists(),
            Schema.Schema("analysis").Table("ArticleEvaluationResults").Exists()))
            return;

        Create.Table("ArticleEvaluationRuns").InSchema("analysis")
            .WithColumn("Id").AsInt64().PrimaryKey().Identity()
            .WithColumn("RunId").AsGuid().NotNullable()
            .WithColumn("OwnerPersonelID").AsString(200).Nullable()
            .WithColumn("Status").AsString(40).NotNullable()
            .WithColumn("DatasetVersion").AsString(100).NotNullable()
            .WithColumn("EvaluatorVersion").AsString(100).NotNullable()
            .WithColumn("PolicyVersion").AsString(100).NotNullable()
            .WithColumn("ProfilesJson").AsString(int.MaxValue).NotNullable()
            .WithColumn("IncludesRealCases").AsBoolean().NotNullable()
            .WithColumn("TotalCases").AsInt32().NotNullable()
            .WithColumn("TotalWorkItems").AsInt32().NotNullable()
            .WithColumn("WorstCaseModelCalls").AsInt32().NotNullable()
            .WithColumn("CompletedWorkItems").AsInt32().NotNullable()
            .WithColumn("FailedWorkItems").AsInt32().NotNullable()
            .WithColumn("SkippedWorkItems").AsInt32().NotNullable()
            .WithColumn("CreatedAt").AsDateTimeOffset().NotNullable()
            .WithColumn("UpdatedAt").AsDateTimeOffset().NotNullable()
            .WithColumn("StartedAt").AsDateTimeOffset().Nullable()
            .WithColumn("CompletedAt").AsDateTimeOffset().Nullable();
        Execute.Sql("ALTER TABLE [analysis].[ArticleEvaluationRuns] ALTER COLUMN [OwnerPersonelID] nvarchar(200) COLLATE Latin1_General_100_BIN2 NULL;");
        Execute.Sql("ALTER TABLE [analysis].[ArticleEvaluationRuns] ALTER COLUMN [DatasetVersion] nvarchar(100) COLLATE Latin1_General_100_BIN2 NOT NULL;");
        Execute.Sql("ALTER TABLE [analysis].[ArticleEvaluationRuns] ALTER COLUMN [EvaluatorVersion] nvarchar(100) COLLATE Latin1_General_100_BIN2 NOT NULL;");
        Execute.Sql("ALTER TABLE [analysis].[ArticleEvaluationRuns] ALTER COLUMN [PolicyVersion] nvarchar(100) COLLATE Latin1_General_100_BIN2 NOT NULL;");
        Create.Index("UX_ArticleEvaluationRuns_RunId").OnTable("ArticleEvaluationRuns").InSchema("analysis")
            .OnColumn("RunId").Ascending().WithOptions().Unique();
        Create.Index("IX_ArticleEvaluationRuns_Status_CreatedAt").OnTable("ArticleEvaluationRuns").InSchema("analysis")
            .OnColumn("Status").Ascending().OnColumn("CreatedAt").Ascending();
        Create.Index("IX_ArticleEvaluationRuns_Owner_RunId").OnTable("ArticleEvaluationRuns").InSchema("analysis")
            .OnColumn("OwnerPersonelID").Ascending().OnColumn("RunId").Ascending();

        Create.Table("ArticleEvaluationCases").InSchema("analysis")
            .WithColumn("Id").AsInt64().PrimaryKey().Identity()
            .WithColumn("ArticleEvaluationRunId").AsInt64().NotNullable()
            .WithColumn("Ordinal").AsInt32().NotNullable()
            .WithColumn("CaseId").AsString(120).NotNullable()
            .WithColumn("Kind").AsString(30).NotNullable()
            .WithColumn("CanonicalWorkId").AsInt32().Nullable()
            .WithColumn("BaseAnalysisRunId").AsInt64().Nullable()
            .WithColumn("Language").AsString(20).NotNullable()
            .WithColumn("SourceHash").AsString(64).NotNullable()
            .WithColumn("SourceSnapshotJson").AsString(int.MaxValue).NotNullable()
            .WithColumn("ReferenceJson").AsString(int.MaxValue).NotNullable()
            .WithColumn("ExpectedVerdictsJson").AsString(int.MaxValue).Nullable()
            .WithColumn("RequestPayloadJson").AsString(int.MaxValue).NotNullable();
        Create.ForeignKey("FK_ArticleEvaluationCases_Runs")
            .FromTable("ArticleEvaluationCases").InSchema("analysis").ForeignColumn("ArticleEvaluationRunId")
            .ToTable("ArticleEvaluationRuns").InSchema("analysis").PrimaryColumn("Id").OnDelete(System.Data.Rule.Cascade);
        Create.ForeignKey("FK_ArticleEvaluationCases_BaseRuns")
            .FromTable("ArticleEvaluationCases").InSchema("analysis").ForeignColumn("BaseAnalysisRunId")
            .ToTable("CanonicalArticleAnalysisRuns").InSchema("analysis").PrimaryColumn("Id").OnDelete(System.Data.Rule.None);
        Execute.Sql("ALTER TABLE [analysis].[ArticleEvaluationCases] ALTER COLUMN [CaseId] nvarchar(120) COLLATE Latin1_General_100_BIN2 NOT NULL;");
        Execute.Sql("ALTER TABLE [analysis].[ArticleEvaluationCases] ALTER COLUMN [SourceHash] nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL;");
        Create.Index("UX_ArticleEvaluationCases_Run_Ordinal").OnTable("ArticleEvaluationCases").InSchema("analysis")
            .OnColumn("ArticleEvaluationRunId").Ascending().OnColumn("Ordinal").Ascending().WithOptions().Unique();
        Create.Index("UX_ArticleEvaluationCases_Run_CaseId").OnTable("ArticleEvaluationCases").InSchema("analysis")
            .OnColumn("ArticleEvaluationRunId").Ascending().OnColumn("CaseId").Ascending().WithOptions().Unique();

        Create.Table("ArticleEvaluationWorkItems").InSchema("analysis")
            .WithColumn("Id").AsInt64().PrimaryKey().Identity()
            .WithColumn("ArticleEvaluationCaseId").AsInt64().NotNullable()
            .WithColumn("DependsOnWorkItemId").AsInt64().Nullable()
            .WithColumn("Ordinal").AsInt32().NotNullable()
            .WithColumn("Phase").AsString(30).NotNullable()
            .WithColumn("ProfileId").AsString(100).NotNullable()
            .WithColumn("ProfileFingerprint").AsString(64).NotNullable()
            .WithColumn("ProfileSnapshotJson").AsString(4000).NotNullable()
            .WithColumn("Status").AsString(40).NotNullable()
            .WithColumn("AttemptCount").AsInt32().NotNullable()
            .WithColumn("MaximumAttempts").AsInt32().NotNullable()
            .WithColumn("ExecutionToken").AsGuid().Nullable()
            .WithColumn("StartedAt").AsDateTimeOffset().Nullable()
            .WithColumn("CompletedAt").AsDateTimeOffset().Nullable()
            .WithColumn("OutcomeCode").AsString(100).Nullable()
            .WithColumn("OutcomeMessage").AsString(1000).Nullable();
        Create.ForeignKey("FK_ArticleEvaluationWorkItems_Cases")
            .FromTable("ArticleEvaluationWorkItems").InSchema("analysis").ForeignColumn("ArticleEvaluationCaseId")
            .ToTable("ArticleEvaluationCases").InSchema("analysis").PrimaryColumn("Id").OnDelete(System.Data.Rule.Cascade);
        Create.ForeignKey("FK_ArticleEvaluationWorkItems_Dependency")
            .FromTable("ArticleEvaluationWorkItems").InSchema("analysis").ForeignColumn("DependsOnWorkItemId")
            .ToTable("ArticleEvaluationWorkItems").InSchema("analysis").PrimaryColumn("Id").OnDelete(System.Data.Rule.None);
        Execute.Sql("ALTER TABLE [analysis].[ArticleEvaluationWorkItems] ALTER COLUMN [ProfileId] nvarchar(100) COLLATE Latin1_General_100_BIN2 NOT NULL;");
        Execute.Sql("ALTER TABLE [analysis].[ArticleEvaluationWorkItems] ALTER COLUMN [ProfileFingerprint] nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL;");
        Create.Index("UX_ArticleEvaluationWorkItems_Case_Ordinal").OnTable("ArticleEvaluationWorkItems").InSchema("analysis")
            .OnColumn("ArticleEvaluationCaseId").Ascending().OnColumn("Ordinal").Ascending().WithOptions().Unique();
        Create.Index("IX_ArticleEvaluationWorkItems_Status_Id").OnTable("ArticleEvaluationWorkItems").InSchema("analysis")
            .OnColumn("Status").Ascending().OnColumn("Id").Ascending();

        Create.Table("ArticleEvaluationAttempts").InSchema("analysis")
            .WithColumn("Id").AsInt64().PrimaryKey().Identity()
            .WithColumn("ArticleEvaluationWorkItemId").AsInt64().NotNullable()
            .WithColumn("AttemptNumber").AsInt32().NotNullable()
            .WithColumn("ExecutionToken").AsGuid().NotNullable()
            .WithColumn("Status").AsString(40).NotNullable()
            .WithColumn("RequestJson").AsString(int.MaxValue).NotNullable()
            .WithColumn("RequestHash").AsString(64).NotNullable()
            .WithColumn("ResponseJson").AsString(int.MaxValue).Nullable()
            .WithColumn("ReturnedModelIdentity").AsString(500).Nullable()
            .WithColumn("TelemetryJson").AsString(int.MaxValue).Nullable()
            .WithColumn("EstimatedCostUsd").AsDecimal(18, 8).Nullable()
            .WithColumn("CostStatus").AsString(30).NotNullable()
            .WithColumn("StartedAt").AsDateTimeOffset().NotNullable()
            .WithColumn("CompletedAt").AsDateTimeOffset().Nullable()
            .WithColumn("ErrorCode").AsString(100).Nullable()
            .WithColumn("ErrorMessage").AsString(1000).Nullable();
        Create.ForeignKey("FK_ArticleEvaluationAttempts_WorkItems")
            .FromTable("ArticleEvaluationAttempts").InSchema("analysis").ForeignColumn("ArticleEvaluationWorkItemId")
            .ToTable("ArticleEvaluationWorkItems").InSchema("analysis").PrimaryColumn("Id").OnDelete(System.Data.Rule.Cascade);
        Create.Index("UX_ArticleEvaluationAttempts_Item_Number").OnTable("ArticleEvaluationAttempts").InSchema("analysis")
            .OnColumn("ArticleEvaluationWorkItemId").Ascending().OnColumn("AttemptNumber").Ascending().WithOptions().Unique();
        Create.Index("UX_ArticleEvaluationAttempts_Token").OnTable("ArticleEvaluationAttempts").InSchema("analysis")
            .OnColumn("ExecutionToken").Ascending().WithOptions().Unique();

        Create.Table("ArticleEvaluationResults").InSchema("analysis")
            .WithColumn("Id").AsInt64().PrimaryKey().Identity()
            .WithColumn("ArticleEvaluationWorkItemId").AsInt64().NotNullable()
            .WithColumn("ActualModelIdentity").AsString(500).NotNullable()
            .WithColumn("MetricsJson").AsString(int.MaxValue).NotNullable()
            .WithColumn("ResultJson").AsString(int.MaxValue).NotNullable()
            .WithColumn("CreatedAt").AsDateTimeOffset().NotNullable();
        Create.ForeignKey("FK_ArticleEvaluationResults_WorkItems")
            .FromTable("ArticleEvaluationResults").InSchema("analysis").ForeignColumn("ArticleEvaluationWorkItemId")
            .ToTable("ArticleEvaluationWorkItems").InSchema("analysis").PrimaryColumn("Id").OnDelete(System.Data.Rule.Cascade);
        Create.Index("UX_ArticleEvaluationResults_WorkItem").OnTable("ArticleEvaluationResults").InSchema("analysis")
            .OnColumn("ArticleEvaluationWorkItemId").Ascending().WithOptions().Unique();
    }

    public override void Down()
    {
        Delete.Table("ArticleEvaluationResults").InSchema("analysis");
        Delete.Table("ArticleEvaluationAttempts").InSchema("analysis");
        Delete.Table("ArticleEvaluationWorkItems").InSchema("analysis");
        Delete.Table("ArticleEvaluationCases").InSchema("analysis");
        Delete.Table("ArticleEvaluationRuns").InSchema("analysis");
    }
}
