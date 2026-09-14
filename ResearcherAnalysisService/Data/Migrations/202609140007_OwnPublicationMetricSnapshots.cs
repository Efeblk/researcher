using FluentMigrator;

namespace ResearcherAnalysisService.Data.Migrations;

[Migration(202609140007, "Add durable publication metric snapshots")]
public sealed class OwnPublicationMetricSnapshots : Migration
{
    public override void Up()
    {
        if (AnalysisMigrationGuard.IsCompleteOrAbsent("publication metric snapshots",
            Schema.Schema("analysis").Table("PublicationMetricSnapshots").Exists(),
            Schema.Schema("analysis").Table("PublicationMetricsRefreshStates").Exists()))
            return;

        Create.Table("PublicationMetricSnapshots").InSchema("analysis")
            .WithColumn("Id").AsInt64().PrimaryKey().Identity()
            .WithColumn("PersonelID").AsString(200).NotNullable()
            .WithColumn("CatalogVersion").AsString(100).NotNullable()
            .WithColumn("SourceRevision").AsInt64().NotNullable()
            .WithColumn("ComputationYear").AsInt32().NotNullable()
            .WithColumn("ComputedAt").AsDateTime2().NotNullable()
            .WithColumn("ResultJson").AsString(int.MaxValue).NotNullable()
            .WithColumn("CanonicalWorkCount").AsInt32().NotNullable()
            .WithColumn("ProviderObservationCount").AsInt32().NotNullable()
            .WithColumn("UnmappedAcademicWorkCount").AsInt32().NotNullable();

        Execute.Sql("""
            ALTER TABLE [analysis].[PublicationMetricSnapshots]
                ALTER COLUMN [CatalogVersion] nvarchar(100) COLLATE Latin1_General_100_BIN2 NOT NULL;
            """);

        Create.Index("UX_PublicationMetricSnapshots_Identity")
            .OnTable("PublicationMetricSnapshots").InSchema("analysis")
            .OnColumn("PersonelID").Ascending()
            .OnColumn("CatalogVersion").Ascending()
            .OnColumn("SourceRevision").Ascending()
            .OnColumn("ComputationYear").Ascending()
            .WithOptions().Unique();
        Create.Index("IX_PublicationMetricSnapshots_PersonelID_Id")
            .OnTable("PublicationMetricSnapshots").InSchema("analysis")
            .OnColumn("PersonelID").Ascending()
            .OnColumn("Id").Descending();

        Create.Table("PublicationMetricsRefreshStates").InSchema("analysis")
            .WithColumn("PersonelID").AsString(200).PrimaryKey()
            .WithColumn("RequestedRevision").AsInt64().NotNullable()
            .WithColumn("ComputedRevision").AsInt64().NotNullable()
            .WithColumn("RequestedCatalogVersion").AsString(100).NotNullable()
            .WithColumn("RequestedComputationYear").AsInt32().NotNullable()
            .WithColumn("LastSuccessfulSnapshotId").AsInt64().Nullable()
            .WithColumn("LastSuccessAt").AsDateTime2().Nullable()
            .WithColumn("NextAttemptAt").AsDateTime2().NotNullable()
            .WithColumn("Attempts").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("UpdatedAt").AsDateTime2().NotNullable()
            .WithColumn("LastOutcomeCode").AsString(50).Nullable()
            .WithColumn("LastOutcomeMessage").AsString(500).Nullable();

        Execute.Sql("""
            ALTER TABLE [analysis].[PublicationMetricsRefreshStates]
                ALTER COLUMN [RequestedCatalogVersion] nvarchar(100) COLLATE Latin1_General_100_BIN2 NOT NULL;
            """);

        Create.ForeignKey("FK_PublicationMetricsRefreshStates_LastSnapshot")
            .FromTable("PublicationMetricsRefreshStates").InSchema("analysis")
            .ForeignColumn("LastSuccessfulSnapshotId")
            .ToTable("PublicationMetricSnapshots").InSchema("analysis")
            .PrimaryColumn("Id");
        Create.Index("IX_PublicationMetricsRefreshStates_NextAttemptAt")
            .OnTable("PublicationMetricsRefreshStates").InSchema("analysis")
            .OnColumn("NextAttemptAt").Ascending()
            .OnColumn("PersonelID").Ascending();
    }

    public override void Down()
    {
        Delete.Table("PublicationMetricsRefreshStates").InSchema("analysis");
        Delete.Table("PublicationMetricSnapshots").InSchema("analysis");
    }
}
