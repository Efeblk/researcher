using FluentMigrator;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data.Migrations.Core;

[Migration(202609110007, "Add provider bibliometrics to publication metric snapshots")]
public sealed class AddPublicationMetricProviderSnapshots : Migration
{
    public override void Up()
    {
        Alter.Column("WorksCount").OnTable("OpenAlexProfiles").InSchema("openalex")
            .AsInt32().Nullable();
        Alter.Column("CitedByCount").OnTable("OpenAlexProfiles").InSchema("openalex")
            .AsInt32().Nullable();

        Create.Table("PublicationMetricProviderSnapshots").InSchema("analysis")
            .WithColumn("Id").AsInt64().PrimaryKey().Identity()
            .WithColumn("SnapshotId").AsInt64().NotNullable()
            .WithColumn("Provider").AsString(50).NotNullable()
            .WithColumn("SourceUpdatedAt").AsDateTime2().Nullable()
            .WithColumn("CitationCount").AsInt32().Nullable()
            .WithColumn("HIndex").AsInt32().Nullable()
            .WithColumn("DocumentCount").AsInt32().Nullable()
            .WithColumn("I10Index").AsInt32().Nullable()
            .WithColumn("CitationCountRecent").AsInt32().Nullable()
            .WithColumn("HIndexRecent").AsInt32().Nullable()
            .WithColumn("I10IndexRecent").AsInt32().Nullable()
            .WithColumn("MetricsSinceYear").AsInt32().Nullable()
            .WithColumn("TwoYearMeanCitedness").AsDecimal(18, 4).Nullable()
            .WithColumn("HasInvalidValues").AsBoolean().NotNullable()
            .WithColumn("FieldMetadataJson").AsString(int.MaxValue).NotNullable();

        Execute.Sql("""
            ALTER TABLE [analysis].[PublicationMetricProviderSnapshots]
                ALTER COLUMN [Provider] nvarchar(50) COLLATE Latin1_General_100_BIN2 NOT NULL;
            """);
        Create.ForeignKey("FK_PublicationMetricProviderSnapshots_Snapshots")
            .FromTable("PublicationMetricProviderSnapshots").InSchema("analysis")
            .ForeignColumn("SnapshotId")
            .ToTable("PublicationMetricSnapshots").InSchema("analysis")
            .PrimaryColumn("Id").OnDelete(System.Data.Rule.Cascade);
        Create.Index("UX_PublicationMetricProviderSnapshots_Snapshot_Provider")
            .OnTable("PublicationMetricProviderSnapshots").InSchema("analysis")
            .OnColumn("SnapshotId").Ascending()
            .OnColumn("Provider").Ascending()
            .WithOptions().Unique();
    }

    public override void Down()
    {
        Delete.Table("PublicationMetricProviderSnapshots").InSchema("analysis");
        Execute.Sql("""
            UPDATE [openalex].[OpenAlexProfiles]
            SET [WorksCount] = COALESCE([WorksCount], 0),
                [CitedByCount] = COALESCE([CitedByCount], 0);
            """);
        Alter.Column("WorksCount").OnTable("OpenAlexProfiles").InSchema("openalex")
            .AsInt32().NotNullable();
        Alter.Column("CitedByCount").OnTable("OpenAlexProfiles").InSchema("openalex")
            .AsInt32().NotNullable();
    }
}
