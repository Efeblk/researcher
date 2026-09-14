using FluentMigrator;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data.Migrations.Core;

[Migration(202609110003, "Add canonical academic works and current observations")]
public sealed class AddCanonicalAcademicData : Migration
{
    public override void Up()
    {
        Create.Table("CanonicalWorks").InSchema("core")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("NormalizedDoi").AsString(500).Nullable()
            .WithColumn("SourceScopedKey").AsString(64).Nullable()
            .WithColumn("HasRetractionObservation").AsBoolean().NotNullable()
            .WithColumn("CreatedAt").AsDateTime2().NotNullable()
            .WithColumn("UpdatedAt").AsDateTime2().NotNullable();

        Execute.Sql("""
            ALTER TABLE [core].[CanonicalWorks]
                ALTER COLUMN [NormalizedDoi] nvarchar(500) COLLATE Latin1_General_100_BIN2 NULL;
            ALTER TABLE [core].[CanonicalWorks]
            ADD CONSTRAINT [CK_CanonicalWorks_ExactlyOneIdentity]
            CHECK (([NormalizedDoi] IS NOT NULL AND [SourceScopedKey] IS NULL) OR
                   ([NormalizedDoi] IS NULL AND [SourceScopedKey] IS NOT NULL));
            CREATE UNIQUE INDEX [UX_CanonicalWorks_NormalizedDoi]
                ON [core].[CanonicalWorks]([NormalizedDoi])
                WHERE [NormalizedDoi] IS NOT NULL;
            CREATE UNIQUE INDEX [UX_CanonicalWorks_SourceScopedKey]
                ON [core].[CanonicalWorks]([SourceScopedKey])
                WHERE [SourceScopedKey] IS NOT NULL;
            """);

        Create.Table("CanonicalWorkObservations").InSchema("core")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("CanonicalWorkId").AsInt32().NotNullable()
            .WithColumn("AcademicWorkId").AsInt32().NotNullable()
            .WithColumn("PersonelID").AsString(200).NotNullable()
            .WithColumn("Provider").AsString(50).NotNullable()
            .WithColumn("ProviderWorkId").AsString(500).Nullable()
            .WithColumn("TitleObserved").AsString(2000).Nullable()
            .WithColumn("DoiObserved").AsString(500).Nullable()
            .WithColumn("PublicationYearObserved").AsInt32().Nullable()
            .WithColumn("PublicationDateObserved").AsDateTime2().Nullable()
            .WithColumn("CategoryObserved").AsString(50).NotNullable()
            .WithColumn("AuthorsObserved").AsString(int.MaxValue).Nullable()
            .WithColumn("PublicationObserved").AsString(2000).Nullable()
            .WithColumn("SourceId").AsString(500).Nullable()
            .WithColumn("SourceName").AsString(2000).Nullable()
            .WithColumn("SourceType").AsString(100).Nullable()
            .WithColumn("Link").AsString(2000).Nullable()
            .WithColumn("FullTextUrl").AsString(2000).Nullable()
            .WithColumn("License").AsString(100).Nullable()
            .WithColumn("Version").AsString(100).Nullable()
            .WithColumn("IsRetracted").AsBoolean().Nullable()
            .WithColumn("ObservedAt").AsDateTime2().NotNullable();

        Create.ForeignKey("FK_CanonicalWorkObservations_CanonicalWorks")
            .FromTable("CanonicalWorkObservations").InSchema("core")
            .ForeignColumn("CanonicalWorkId")
            .ToTable("CanonicalWorks").InSchema("core")
            .PrimaryColumn("Id").OnDelete(System.Data.Rule.Cascade);
        Create.ForeignKey("FK_CanonicalWorkObservations_AcademicWorks")
            .FromTable("CanonicalWorkObservations").InSchema("core")
            .ForeignColumn("AcademicWorkId")
            .ToTable("AcademicWorks").InSchema("core")
            .PrimaryColumn("Id").OnDelete(System.Data.Rule.Cascade);
        Create.ForeignKey("FK_CanonicalWorkObservations_Researchers")
            .FromTable("CanonicalWorkObservations").InSchema("core")
            .ForeignColumn("PersonelID")
            .ToTable("Researchers").InSchema("core")
            .PrimaryColumn("PersonelID");
        Create.Index("UX_CanonicalWorkObservations_AcademicWorkId")
            .OnTable("CanonicalWorkObservations").InSchema("core")
            .OnColumn("AcademicWorkId").Ascending().WithOptions().Unique();
        Create.Index("IX_CanonicalWorkObservations_CanonicalWorkId_PersonelID")
            .OnTable("CanonicalWorkObservations").InSchema("core")
            .OnColumn("CanonicalWorkId").Ascending()
            .OnColumn("PersonelID").Ascending();

        Create.Table("CanonicalResearcherWorks").InSchema("core")
            .WithColumn("CanonicalWorkId").AsInt32().NotNullable()
            .WithColumn("PersonelID").AsString(200).NotNullable()
            .WithColumn("LastObservedAt").AsDateTime2().NotNullable();
        Create.PrimaryKey("PK_CanonicalResearcherWorks")
            .OnTable("CanonicalResearcherWorks").WithSchema("core")
            .Columns("CanonicalWorkId", "PersonelID");
        Create.ForeignKey("FK_CanonicalResearcherWorks_CanonicalWorks")
            .FromTable("CanonicalResearcherWorks").InSchema("core")
            .ForeignColumn("CanonicalWorkId")
            .ToTable("CanonicalWorks").InSchema("core")
            .PrimaryColumn("Id").OnDelete(System.Data.Rule.Cascade);
        Create.ForeignKey("FK_CanonicalResearcherWorks_Researchers")
            .FromTable("CanonicalResearcherWorks").InSchema("core")
            .ForeignColumn("PersonelID")
            .ToTable("Researchers").InSchema("core")
            .PrimaryColumn("PersonelID").OnDelete(System.Data.Rule.Cascade);
        Create.Index("IX_CanonicalResearcherWorks_PersonelID_CanonicalWorkId")
            .OnTable("CanonicalResearcherWorks").InSchema("core")
            .OnColumn("PersonelID").Ascending()
            .OnColumn("CanonicalWorkId").Ascending();
    }

    public override void Down()
    {
        Delete.Table("CanonicalResearcherWorks").InSchema("core");
        Delete.Table("CanonicalWorkObservations").InSchema("core");
        Delete.Table("CanonicalWorks").InSchema("core");
    }
}
