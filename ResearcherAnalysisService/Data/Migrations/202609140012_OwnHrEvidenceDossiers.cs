using FluentMigrator;

namespace ResearcherAnalysisService.Data.Migrations;

[Migration(202609140012)]
public sealed class OwnHrEvidenceDossiers : Migration
{
    public override void Up()
    {
Execute.Sql("IF SCHEMA_ID(N'hr') IS NULL EXEC(N'CREATE SCHEMA [hr]');");
        Create.Table("EvidenceDossiers").InSchema("hr")
            .WithColumn("Id").AsInt64().PrimaryKey().Identity()
            .WithColumn("PersonelID").AsString(200).NotNullable()
            .WithColumn("CreatedByActorId").AsString(200).NotNullable()
            .WithColumn("CreatedAt").AsDateTimeOffset().NotNullable()
            .WithColumn("PolicyVersion").AsString(100).NotNullable()
            .WithColumn("PublicationMetricSnapshotId").AsInt64().Nullable()
            .WithColumn("InputFingerprint").AsString(64).NotNullable()
            .WithColumn("InputManifestJson").AsString(int.MaxValue).NotNullable()
            .WithColumn("DossierJson").AsString(int.MaxValue).NotNullable();
        Create.ForeignKey("FK_HrEvidenceDossiers_MetricSnapshots").FromTable("EvidenceDossiers").InSchema("hr")
            .ForeignColumn("PublicationMetricSnapshotId").ToTable("PublicationMetricSnapshots").InSchema("analysis").PrimaryColumn("Id");
        Create.Index("IX_HrEvidenceDossiers_PersonelID_Id").OnTable("EvidenceDossiers").InSchema("hr")
            .OnColumn("PersonelID").Ascending().OnColumn("Id").Descending();
        Create.Table("DossierReviewActions").InSchema("hr")
            .WithColumn("Id").AsInt64().PrimaryKey().Identity()
            .WithColumn("DossierId").AsInt64().NotNullable()
            .WithColumn("ActorAuditId").AsString(200).NotNullable()
            .WithColumn("ClientRequestId").AsGuid().NotNullable()
            .WithColumn("ActionType").AsString(40).NotNullable()
            .WithColumn("EvidenceReference").AsString(200).Nullable()
            .WithColumn("Note").AsString(4000).Nullable()
            .WithColumn("RecordedAt").AsDateTimeOffset().NotNullable();
        Create.ForeignKey("FK_HrDossierReviewActions_Dossier").FromTable("DossierReviewActions").InSchema("hr")
            .ForeignColumn("DossierId").ToTable("EvidenceDossiers").InSchema("hr").PrimaryColumn("Id").OnDelete(System.Data.Rule.Cascade);
        Create.Index("UX_HrDossierReviewActions_Request").OnTable("DossierReviewActions").InSchema("hr")
            .OnColumn("DossierId").Ascending().OnColumn("ActorAuditId").Ascending().OnColumn("ClientRequestId").Ascending().WithOptions().Unique();
    }

    public override void Down()
    {
        Delete.Table("DossierReviewActions").InSchema("hr");
        Delete.Table("EvidenceDossiers").InSchema("hr");
    }
}
