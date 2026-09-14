using FluentMigrator;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data.Migrations.Core;

[Migration(202609110008, "Add normalized OpenAlex work research contexts")]
public sealed class AddAcademicWorkResearchContexts : Migration
{
    public override void Up()
    {
        Create.Table("AcademicWorkResearchContexts").InSchema("core")
            .WithColumn("AcademicWorkId").AsInt32().PrimaryKey().NotNullable()
            .WithColumn("Provider").AsString(50).NotNullable()
            .WithColumn("SourceWorkId").AsString(500).Nullable()
            .WithColumn("ParserVersion").AsString(100).NotNullable()
            .WithColumn("PayloadFingerprint").AsString(64).NotNullable()
            .WithColumn("SourceSyncedAt").AsDateTime2().NotNullable()
            .WithColumn("ProviderUpdatedAt").AsDateTime2().Nullable()
            .WithColumn("SourcePublicationYear").AsInt32().Nullable()
            .WithColumn("RawType").AsString(100).Nullable()
            .WithColumn("PrimarySourceType").AsString(100).Nullable()
            .WithColumn("Fwci").AsDecimal(18, 6).Nullable()
            .WithColumn("CitationNormalizedPercentile").AsDecimal(18, 9).Nullable()
            .WithColumn("IsInTopOnePercent").AsBoolean().Nullable()
            .WithColumn("IsInTopTenPercent").AsBoolean().Nullable()
            .WithColumn("ParseQuality").AsString(30).NotNullable()
            .WithColumn("ParseQualityReason").AsString(500).Nullable()
            .WithColumn("PrimaryTopicQuality").AsString(30).NotNullable()
            .WithColumn("PrimaryTopicQualityReason").AsString(500).Nullable()
            .WithColumn("ValueQualityJson").AsString(int.MaxValue).NotNullable();
        Create.ForeignKey("FK_AcademicWorkResearchContexts_AcademicWorks")
            .FromTable("AcademicWorkResearchContexts").InSchema("core")
            .ForeignColumn("AcademicWorkId")
            .ToTable("AcademicWorks").InSchema("core")
            .PrimaryColumn("Id").OnDelete(System.Data.Rule.Cascade);

        Create.Table("AcademicWorkTopics").InSchema("core")
            .WithColumn("Id").AsInt64().PrimaryKey().Identity()
            .WithColumn("AcademicWorkId").AsInt32().NotNullable()
            .WithColumn("TopicId").AsString(200).NotNullable()
            .WithColumn("TopicName").AsString(1000).Nullable()
            .WithColumn("SubfieldId").AsString(200).Nullable()
            .WithColumn("SubfieldName").AsString(1000).Nullable()
            .WithColumn("FieldId").AsString(200).Nullable()
            .WithColumn("FieldName").AsString(1000).Nullable()
            .WithColumn("DomainId").AsString(200).Nullable()
            .WithColumn("DomainName").AsString(1000).Nullable()
            .WithColumn("OriginalRank").AsInt32().NotNullable()
            .WithColumn("AssignmentScore").AsDouble().Nullable()
            .WithColumn("ScoreQuality").AsString(30).NotNullable()
            .WithColumn("ScoreQualityReason").AsString(500).Nullable()
            .WithColumn("IsPrimary").AsBoolean().NotNullable();
        Create.ForeignKey("FK_AcademicWorkTopics_ResearchContexts")
            .FromTable("AcademicWorkTopics").InSchema("core")
            .ForeignColumn("AcademicWorkId")
            .ToTable("AcademicWorkResearchContexts").InSchema("core")
            .PrimaryColumn("AcademicWorkId").OnDelete(System.Data.Rule.Cascade);
        Execute.Sql("""
            ALTER TABLE [core].[AcademicWorkResearchContexts]
                ALTER COLUMN [PayloadFingerprint] nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL;
            ALTER TABLE [core].[AcademicWorkTopics]
                ALTER COLUMN [TopicId] nvarchar(200) COLLATE Latin1_General_100_BIN2 NOT NULL;
            """);
        Create.Index("UX_AcademicWorkTopics_Work_Topic")
            .OnTable("AcademicWorkTopics").InSchema("core")
            .OnColumn("AcademicWorkId").Ascending()
            .OnColumn("TopicId").Ascending().WithOptions().Unique();
        Create.Index("IX_AcademicWorkTopics_Work_Primary")
            .OnTable("AcademicWorkTopics").InSchema("core")
            .OnColumn("AcademicWorkId").Ascending()
            .OnColumn("IsPrimary").Ascending();
    }

    public override void Down()
    {
        Delete.Table("AcademicWorkTopics").InSchema("core");
        Delete.Table("AcademicWorkResearchContexts").InSchema("core");
    }
}
