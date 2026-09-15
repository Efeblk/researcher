using FluentMigrator;

namespace ResearcherAnalysisService.Data.Migrations;

[Migration(202609140005)]
public sealed class OwnCanonicalArticleEvidence : Migration
{
    public override void Up()
    {
Create.Table("ArticleSourceSnapshots").InSchema("analysis")
            .WithColumn("Id").AsInt64().PrimaryKey().Identity()
            .WithColumn("CanonicalWorkId").AsInt32().NotNullable()
            .WithColumn("ExtractedTextHash").AsString(64).NotNullable()
            .WithColumn("SourceKind").AsString(20).NotNullable()
            .WithColumn("ExtractionVersion").AsString(100).NotNullable()
            .WithColumn("CreatedAt").AsDateTimeOffset().NotNullable();
        Execute.Sql("ALTER TABLE [analysis].[ArticleSourceSnapshots] ALTER COLUMN [ExtractedTextHash] nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL;");
        Execute.Sql("ALTER TABLE [analysis].[ArticleSourceSnapshots] ALTER COLUMN [SourceKind] nvarchar(20) COLLATE Latin1_General_100_BIN2 NOT NULL;");
        Execute.Sql("ALTER TABLE [analysis].[ArticleSourceSnapshots] ALTER COLUMN [ExtractionVersion] nvarchar(100) COLLATE Latin1_General_100_BIN2 NOT NULL;");
        Create.Index("UX_ArticleSourceSnapshots_Identity")
            .OnTable("ArticleSourceSnapshots").InSchema("analysis")
            .OnColumn("CanonicalWorkId").Ascending()
            .OnColumn("ExtractedTextHash").Ascending()
            .OnColumn("SourceKind").Ascending()
            .OnColumn("ExtractionVersion").Ascending()
            .WithOptions().Unique();

        Create.Table("ArticleSourcePages").InSchema("analysis")
            .WithColumn("Id").AsInt64().PrimaryKey().Identity()
            .WithColumn("ArticleSourceSnapshotId").AsInt64().NotNullable()
            .WithColumn("Ordinal").AsInt32().NotNullable()
            .WithColumn("PageNumber").AsInt32().Nullable()
            .WithColumn("Text").AsString(int.MaxValue).NotNullable();
        Create.ForeignKey("FK_ArticleSourcePages_ArticleSourceSnapshots")
            .FromTable("ArticleSourcePages").InSchema("analysis")
            .ForeignColumn("ArticleSourceSnapshotId")
            .ToTable("ArticleSourceSnapshots").InSchema("analysis")
            .PrimaryColumn("Id").OnDelete(System.Data.Rule.Cascade);
        Create.Index("UX_ArticleSourcePages_Snapshot_Ordinal")
            .OnTable("ArticleSourcePages").InSchema("analysis")
            .OnColumn("ArticleSourceSnapshotId").Ascending()
            .OnColumn("Ordinal").Ascending().WithOptions().Unique();

        Create.Table("ArticleSourceSpans").InSchema("analysis")
            .WithColumn("Id").AsInt64().PrimaryKey().Identity()
            .WithColumn("ArticleSourceSnapshotId").AsInt64().NotNullable()
            .WithColumn("SourceId").AsString(100).NotNullable()
            .WithColumn("Ordinal").AsInt32().NotNullable()
            .WithColumn("PageNumber").AsInt32().Nullable()
            .WithColumn("StartOffset").AsInt32().NotNullable()
            .WithColumn("EndOffset").AsInt32().NotNullable()
            .WithColumn("Text").AsString(550).NotNullable();
        Create.ForeignKey("FK_ArticleSourceSpans_ArticleSourceSnapshots")
            .FromTable("ArticleSourceSpans").InSchema("analysis")
            .ForeignColumn("ArticleSourceSnapshotId")
            .ToTable("ArticleSourceSnapshots").InSchema("analysis")
            .PrimaryColumn("Id").OnDelete(System.Data.Rule.Cascade);
        Create.Index("UX_ArticleSourceSpans_Snapshot_SourceId")
            .OnTable("ArticleSourceSpans").InSchema("analysis")
            .OnColumn("ArticleSourceSnapshotId").Ascending()
            .OnColumn("SourceId").Ascending().WithOptions().Unique();
        Create.Index("UX_ArticleSourceSpans_Snapshot_Ordinal")
            .OnTable("ArticleSourceSpans").InSchema("analysis")
            .OnColumn("ArticleSourceSnapshotId").Ascending()
            .OnColumn("Ordinal").Ascending().WithOptions().Unique();

        Create.Table("CanonicalArticleAnalysisRuns").InSchema("analysis")
            .WithColumn("Id").AsInt64().PrimaryKey().Identity()
            .WithColumn("CanonicalWorkId").AsInt32().NotNullable()
            .WithColumn("ArticleSourceSnapshotId").AsInt64().NotNullable()
            .WithColumn("SavedArticleSummaryId").AsInt64().NotNullable()
            .WithColumn("AnalyzedAt").AsDateTimeOffset().NotNullable()
            .WithColumn("SourceAcquiredAt").AsDateTimeOffset().NotNullable()
            .WithColumn("SourceUrl").AsString(2000).Nullable()
            .WithColumn("SourceOrigin").AsString(200).NotNullable()
            .WithColumn("Language").AsString(20).NotNullable()
            .WithColumn("Model").AsString(200).NotNullable()
            .WithColumn("PromptVersion").AsString(100).NotNullable()
            .WithColumn("ExtractionMethod").AsString(50).NotNullable()
            .WithColumn("ProcessedChunks").AsInt32().NotNullable()
            .WithColumn("TotalChunks").AsInt32().NotNullable()
            .WithColumn("ProcessedPages").AsInt32().NotNullable()
            .WithColumn("TextBearingPages").AsInt32().NotNullable()
            .WithColumn("TotalPages").AsInt32().NotNullable()
            .WithColumn("SelectedClaimsOmitted").AsInt32().NotNullable()
            .WithColumn("IsPartial").AsBoolean().NotNullable()
            .WithColumn("ScopeReason").AsString(4000).Nullable()
            .WithColumn("CandidateClaims").AsInt32().Nullable()
            .WithColumn("AutomaticallyCheckedClaims").AsInt32().Nullable()
            .WithColumn("SupportedClaims").AsInt32().Nullable()
            .WithColumn("UnsupportedClaims").AsInt32().Nullable()
            .WithColumn("UncertainClaims").AsInt32().Nullable()
            .WithColumn("DuplicateOrCappedClaims").AsInt32().Nullable()
            .WithColumn("BudgetUnverifiedClaims").AsInt32().Nullable()
            .WithColumn("OmissionReasonsJson").AsString(int.MaxValue).NotNullable()
            .WithColumn("VerificationStatus").AsString(50).NotNullable()
            .WithColumn("VerificationModel").AsString(200).NotNullable()
            .WithColumn("VerificationPromptVersion").AsString(100).NotNullable()
            .WithColumn("UsesSameModelFamily").AsBoolean().NotNullable()
            .WithColumn("VerificationLimitation").AsString(4000).Nullable();
        Create.ForeignKey("FK_CanonicalArticleAnalysisRuns_ArticleSourceSnapshots")
            .FromTable("CanonicalArticleAnalysisRuns").InSchema("analysis")
            .ForeignColumn("ArticleSourceSnapshotId")
            .ToTable("ArticleSourceSnapshots").InSchema("analysis")
            .PrimaryColumn("Id").OnDelete(System.Data.Rule.None);
        Create.ForeignKey("FK_CanonicalArticleAnalysisRuns_ArticleSummaries")
            .FromTable("CanonicalArticleAnalysisRuns").InSchema("analysis")
            .ForeignColumn("SavedArticleSummaryId")
            .ToTable("ArticleSummaries").InSchema("analysis")
            .PrimaryColumn("Id").OnDelete(System.Data.Rule.None);
        Create.Index("UX_CanonicalArticleAnalysisRuns_SavedArticleSummaryId")
            .OnTable("CanonicalArticleAnalysisRuns").InSchema("analysis")
            .OnColumn("SavedArticleSummaryId").Ascending().WithOptions().Unique();
        Create.Index("IX_CanonicalArticleAnalysisRuns_CanonicalWorkId_Id")
            .OnTable("CanonicalArticleAnalysisRuns").InSchema("analysis")
            .OnColumn("CanonicalWorkId").Ascending().OnColumn("Id").Descending();

        Create.Table("CanonicalArticleClaims").InSchema("analysis")
            .WithColumn("Id").AsInt64().PrimaryKey().Identity()
            .WithColumn("CanonicalArticleAnalysisRunId").AsInt64().NotNullable()
            .WithColumn("Section").AsString(20).NotNullable()
            .WithColumn("SectionOrder").AsInt32().NotNullable()
            .WithColumn("Ordinal").AsInt32().NotNullable()
            .WithColumn("ExternalClaimId").AsString(200).Nullable()
            .WithColumn("Text").AsString(1200).NotNullable();
        Create.ForeignKey("FK_CanonicalArticleClaims_AnalysisRuns")
            .FromTable("CanonicalArticleClaims").InSchema("analysis")
            .ForeignColumn("CanonicalArticleAnalysisRunId")
            .ToTable("CanonicalArticleAnalysisRuns").InSchema("analysis")
            .PrimaryColumn("Id").OnDelete(System.Data.Rule.Cascade);
        Create.Index("UX_CanonicalArticleClaims_Run_Section_Ordinal")
            .OnTable("CanonicalArticleClaims").InSchema("analysis")
            .OnColumn("CanonicalArticleAnalysisRunId").Ascending()
            .OnColumn("SectionOrder").Ascending()
            .OnColumn("Ordinal").Ascending().WithOptions().Unique();

        Create.Table("CanonicalArticleClaimEvidence").InSchema("analysis")
            .WithColumn("Id").AsInt64().PrimaryKey().Identity()
            .WithColumn("CanonicalArticleClaimId").AsInt64().NotNullable()
            .WithColumn("ArticleSourceSpanId").AsInt64().NotNullable()
            .WithColumn("Ordinal").AsInt32().NotNullable();
        Create.ForeignKey("FK_CanonicalArticleClaimEvidence_Claims")
            .FromTable("CanonicalArticleClaimEvidence").InSchema("analysis")
            .ForeignColumn("CanonicalArticleClaimId")
            .ToTable("CanonicalArticleClaims").InSchema("analysis")
            .PrimaryColumn("Id").OnDelete(System.Data.Rule.Cascade);
        Create.ForeignKey("FK_CanonicalArticleClaimEvidence_SourceSpans")
            .FromTable("CanonicalArticleClaimEvidence").InSchema("analysis")
            .ForeignColumn("ArticleSourceSpanId")
            .ToTable("ArticleSourceSpans").InSchema("analysis")
            .PrimaryColumn("Id").OnDelete(System.Data.Rule.None);
        Create.Index("UX_CanonicalArticleClaimEvidence_Claim_Ordinal")
            .OnTable("CanonicalArticleClaimEvidence").InSchema("analysis")
            .OnColumn("CanonicalArticleClaimId").Ascending()
            .OnColumn("Ordinal").Ascending().WithOptions().Unique();
    }

    public override void Down()
    {
        Delete.Table("CanonicalArticleClaimEvidence").InSchema("analysis");
        Delete.Table("CanonicalArticleClaims").InSchema("analysis");
        Delete.Table("CanonicalArticleAnalysisRuns").InSchema("analysis");
        Delete.Table("ArticleSourceSpans").InSchema("analysis");
        Delete.Table("ArticleSourcePages").InSchema("analysis");
        Delete.Table("ArticleSourceSnapshots").InSchema("analysis");
    }
}
