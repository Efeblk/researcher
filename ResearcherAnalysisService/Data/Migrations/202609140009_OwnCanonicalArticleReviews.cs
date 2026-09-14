using FluentMigrator;

namespace ResearcherAnalysisService.Data.Migrations;

[Migration(202609140009)]
public sealed class OwnCanonicalArticleReviews : Migration
{
    public override void Up()
    {
        if (AnalysisMigrationGuard.IsCompleteOrAbsent("canonical article reviews",
            Schema.Schema("analysis").Table("CanonicalArticleReviewRuns").Exists(),
            Schema.Schema("analysis").Table("CanonicalArticleReviewFindings").Exists(),
            Schema.Schema("analysis").Table("CanonicalArticleReviewEvidence").Exists()))
            return;

        Create.Table("CanonicalArticleReviewRuns").InSchema("analysis")
            .WithColumn("Id").AsInt64().PrimaryKey().Identity()
            .WithColumn("CanonicalWorkId").AsInt32().NotNullable()
            .WithColumn("BaseAnalysisRunId").AsInt64().NotNullable()
            .WithColumn("ArticleSourceSnapshotId").AsInt64().NotNullable()
            .WithColumn("ReviewedAt").AsDateTimeOffset().NotNullable()
            .WithColumn("Language").AsString(20).NotNullable()
            .WithColumn("PolicyVersion").AsString(100).NotNullable()
            .WithColumn("Model").AsString(200).NotNullable()
            .WithColumn("PromptVersion").AsString(100).NotNullable()
            .WithColumn("Outcome").AsString(50).NotNullable()
            .WithColumn("VerificationStatus").AsString(50).NotNullable()
            .WithColumn("VerificationModel").AsString(200).NotNullable()
            .WithColumn("VerificationPromptVersion").AsString(100).NotNullable()
            .WithColumn("UsesSameModelFamily").AsBoolean().NotNullable()
            .WithColumn("VerificationLimitation").AsString(1000).Nullable()
            .WithColumn("ProcessedPages").AsInt32().NotNullable()
            .WithColumn("TextBearingPages").AsInt32().NotNullable()
            .WithColumn("TotalPages").AsInt32().NotNullable()
            .WithColumn("IsPartial").AsBoolean().NotNullable()
            .WithColumn("ScopeReason").AsString(4000).Nullable()
            .WithColumn("ProcessedRoles").AsInt32().NotNullable()
            .WithColumn("TotalRoles").AsInt32().NotNullable()
            .WithColumn("CandidateFindings").AsInt32().NotNullable()
            .WithColumn("AutomaticallyCheckedFindings").AsInt32().NotNullable()
            .WithColumn("SupportedFindings").AsInt32().NotNullable()
            .WithColumn("UnsupportedFindings").AsInt32().NotNullable()
            .WithColumn("UncertainFindings").AsInt32().NotNullable()
            .WithColumn("OmittedFindings").AsInt32().NotNullable()
            .WithColumn("OmissionReasonsJson").AsString(int.MaxValue).NotNullable()
            .WithColumn("ReportJson").AsString(int.MaxValue).NotNullable();
        Create.ForeignKey("FK_CanonicalArticleReviewRuns_BaseAnalysisRuns")
            .FromTable("CanonicalArticleReviewRuns").InSchema("analysis").ForeignColumn("BaseAnalysisRunId")
            .ToTable("CanonicalArticleAnalysisRuns").InSchema("analysis").PrimaryColumn("Id").OnDelete(System.Data.Rule.None);
        Create.ForeignKey("FK_CanonicalArticleReviewRuns_SourceSnapshots")
            .FromTable("CanonicalArticleReviewRuns").InSchema("analysis").ForeignColumn("ArticleSourceSnapshotId")
            .ToTable("ArticleSourceSnapshots").InSchema("analysis").PrimaryColumn("Id").OnDelete(System.Data.Rule.None);
        Execute.Sql("ALTER TABLE [analysis].[CanonicalArticleReviewRuns] ALTER COLUMN [Language] nvarchar(20) COLLATE Latin1_General_100_BIN2 NOT NULL;");
        Execute.Sql("ALTER TABLE [analysis].[CanonicalArticleReviewRuns] ALTER COLUMN [PolicyVersion] nvarchar(100) COLLATE Latin1_General_100_BIN2 NOT NULL;");
        Create.Index("IX_CanonicalArticleReviewRuns_Cache")
            .OnTable("CanonicalArticleReviewRuns").InSchema("analysis")
            .OnColumn("BaseAnalysisRunId").Ascending().OnColumn("Language").Ascending()
            .OnColumn("PolicyVersion").Ascending().OnColumn("Id").Descending();
        Create.Index("IX_CanonicalArticleReviewRuns_Work_Language_Id")
            .OnTable("CanonicalArticleReviewRuns").InSchema("analysis")
            .OnColumn("CanonicalWorkId").Ascending().OnColumn("Language").Ascending().OnColumn("Id").Descending();

        Create.Table("CanonicalArticleReviewFindings").InSchema("analysis")
            .WithColumn("Id").AsInt64().PrimaryKey().Identity()
            .WithColumn("CanonicalArticleReviewRunId").AsInt64().NotNullable()
            .WithColumn("Ordinal").AsInt32().NotNullable()
            .WithColumn("ExternalFindingId").AsString(120).NotNullable()
            .WithColumn("Role").AsString(30).NotNullable()
            .WithColumn("Kind").AsString(30).NotNullable()
            .WithColumn("Basis").AsString(1200).NotNullable()
            .WithColumn("Suggestion").AsString(1200).Nullable();
        Create.ForeignKey("FK_CanonicalArticleReviewFindings_Runs")
            .FromTable("CanonicalArticleReviewFindings").InSchema("analysis").ForeignColumn("CanonicalArticleReviewRunId")
            .ToTable("CanonicalArticleReviewRuns").InSchema("analysis").PrimaryColumn("Id").OnDelete(System.Data.Rule.Cascade);
        Execute.Sql("ALTER TABLE [analysis].[CanonicalArticleReviewFindings] ALTER COLUMN [ExternalFindingId] nvarchar(120) COLLATE Latin1_General_100_BIN2 NOT NULL;");
        Create.Index("UX_CanonicalArticleReviewFindings_Run_Ordinal")
            .OnTable("CanonicalArticleReviewFindings").InSchema("analysis")
            .OnColumn("CanonicalArticleReviewRunId").Ascending().OnColumn("Ordinal").Ascending().WithOptions().Unique();
        Create.Index("UX_CanonicalArticleReviewFindings_Run_ExternalId")
            .OnTable("CanonicalArticleReviewFindings").InSchema("analysis")
            .OnColumn("CanonicalArticleReviewRunId").Ascending().OnColumn("ExternalFindingId").Ascending().WithOptions().Unique();

        Create.Table("CanonicalArticleReviewEvidence").InSchema("analysis")
            .WithColumn("Id").AsInt64().PrimaryKey().Identity()
            .WithColumn("CanonicalArticleReviewFindingId").AsInt64().NotNullable()
            .WithColumn("ArticleSourceSpanId").AsInt64().NotNullable()
            .WithColumn("Ordinal").AsInt32().NotNullable();
        Create.ForeignKey("FK_CanonicalArticleReviewEvidence_Findings")
            .FromTable("CanonicalArticleReviewEvidence").InSchema("analysis").ForeignColumn("CanonicalArticleReviewFindingId")
            .ToTable("CanonicalArticleReviewFindings").InSchema("analysis").PrimaryColumn("Id").OnDelete(System.Data.Rule.Cascade);
        Create.ForeignKey("FK_CanonicalArticleReviewEvidence_SourceSpans")
            .FromTable("CanonicalArticleReviewEvidence").InSchema("analysis").ForeignColumn("ArticleSourceSpanId")
            .ToTable("ArticleSourceSpans").InSchema("analysis").PrimaryColumn("Id").OnDelete(System.Data.Rule.None);
        Create.Index("UX_CanonicalArticleReviewEvidence_Finding_Ordinal")
            .OnTable("CanonicalArticleReviewEvidence").InSchema("analysis")
            .OnColumn("CanonicalArticleReviewFindingId").Ascending().OnColumn("Ordinal").Ascending().WithOptions().Unique();
        Create.Index("UX_CanonicalArticleReviewEvidence_Finding_Span")
            .OnTable("CanonicalArticleReviewEvidence").InSchema("analysis")
            .OnColumn("CanonicalArticleReviewFindingId").Ascending().OnColumn("ArticleSourceSpanId").Ascending().WithOptions().Unique();
    }

    public override void Down()
    {
        Delete.Table("CanonicalArticleReviewEvidence").InSchema("analysis");
        Delete.Table("CanonicalArticleReviewFindings").InSchema("analysis");
        Delete.Table("CanonicalArticleReviewRuns").InSchema("analysis");
    }
}
