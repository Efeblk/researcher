using FluentMigrator;

namespace ResearcherAnalysisService.Data.Migrations;

[Migration(202609140011, "Add immutable reference-population manifests")]
public sealed class OwnReferencePopulationManifests : Migration
{
    public override void Up()
    {
Create.Table("ReferencePopulationManifests").InSchema("analysis")
            .WithColumn("Id").AsInt64().PrimaryKey().Identity()
            .WithColumn("ManifestVersion").AsString(100).NotNullable()
            .WithColumn("Fingerprint").AsString(64).NotNullable()
            .WithColumn("CohortDefinition").AsString(4000).NotNullable()
            .WithColumn("EligibilityPolicyVersion").AsString(100).NotNullable()
            .WithColumn("Provenance").AsString(4000).NotNullable()
            .WithColumn("SamplingAndCoverage").AsString(4000).NotNullable()
            .WithColumn("MemberCount").AsInt32().NotNullable()
            .WithColumn("ImportedAt").AsDateTimeOffset().NotNullable()
            .WithColumn("ImportedByActorAuditId").AsString(200).NotNullable()
            .WithColumn("Reviewer").AsString(200).Nullable()
            .WithColumn("ReviewedAt").AsDateTimeOffset().Nullable()
            .WithColumn("ReviewMethod").AsString(4000).Nullable()
            .WithColumn("ApprovedForInternalNormalization").AsBoolean().NotNullable();
        Execute.Sql("ALTER TABLE [analysis].[ReferencePopulationManifests] ALTER COLUMN [ManifestVersion] nvarchar(100) COLLATE Latin1_General_100_BIN2 NOT NULL; ALTER TABLE [analysis].[ReferencePopulationManifests] ALTER COLUMN [Fingerprint] nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL;");
        Create.Index("UX_ReferencePopulationManifests_Version")
            .OnTable("ReferencePopulationManifests").InSchema("analysis")
            .OnColumn("ManifestVersion").Ascending().WithOptions().Unique();

        Create.Table("ReferencePopulationMembers").InSchema("analysis")
            .WithColumn("Id").AsInt64().PrimaryKey().Identity()
            .WithColumn("ReferencePopulationManifestId").AsInt64().NotNullable()
            .WithColumn("StableMemberId").AsString(200).NotNullable()
            .WithColumn("ClassificationId").AsString(200).NotNullable()
            .WithColumn("PublicationYear").AsInt32().NotNullable()
            .WithColumn("WorkType").AsString(100).NotNullable()
            .WithColumn("Category").AsString(100).NotNullable()
            .WithColumn("CitationCount").AsInt32().NotNullable();
        Create.ForeignKey("FK_ReferencePopulationMembers_Manifests")
            .FromTable("ReferencePopulationMembers").InSchema("analysis")
            .ForeignColumn("ReferencePopulationManifestId")
            .ToTable("ReferencePopulationManifests").InSchema("analysis")
            .PrimaryColumn("Id").OnDelete(System.Data.Rule.Cascade);
        Create.Index("UX_ReferencePopulationMembers_Manifest_Member")
            .OnTable("ReferencePopulationMembers").InSchema("analysis")
            .OnColumn("ReferencePopulationManifestId").Ascending()
            .OnColumn("StableMemberId").Ascending().WithOptions().Unique();
    }

    public override void Down()
    {
        Delete.Table("ReferencePopulationMembers").InSchema("analysis");
        Delete.Table("ReferencePopulationManifests").InSchema("analysis");
    }
}
