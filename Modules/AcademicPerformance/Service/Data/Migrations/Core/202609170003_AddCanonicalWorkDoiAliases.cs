using FluentMigrator;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data.Migrations.Core;

[Migration(202609170003, "Add canonical work DOI aliases")]
public sealed class AddCanonicalWorkDoiAliases : Migration
{
    public override void Up()
    {
        Create.Table("CanonicalWorkDoiAliases").InSchema("core")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("NormalizedDoi").AsString(500).NotNullable()
            .WithColumn("CanonicalWorkId").AsInt32().NotNullable();
        Execute.Sql("""
            ALTER TABLE [core].[CanonicalWorkDoiAliases]
                ALTER COLUMN [NormalizedDoi] nvarchar(500) COLLATE Latin1_General_100_BIN2 NOT NULL;
            """);
        Create.Index("UX_CanonicalWorkDoiAliases_NormalizedDoi")
            .OnTable("CanonicalWorkDoiAliases").InSchema("core")
            .OnColumn("NormalizedDoi").Ascending().WithOptions().Unique();
        Create.ForeignKey("FK_CanonicalWorkDoiAliases_CanonicalWorks")
            .FromTable("CanonicalWorkDoiAliases").InSchema("core").ForeignColumn("CanonicalWorkId")
            .ToTable("CanonicalWorks").InSchema("core").PrimaryColumn("Id")
            .OnDelete(System.Data.Rule.Cascade);
        Create.Index("IX_CanonicalWorkDoiAliases_CanonicalWorkId")
            .OnTable("CanonicalWorkDoiAliases").InSchema("core")
            .OnColumn("CanonicalWorkId").Ascending();

        Create.Table("CanonicalWorkDoiRelations").InSchema("core")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("RelationKey").AsString(64).NotNullable()
            .WithColumn("SourceDoi").AsString(500).NotNullable()
            .WithColumn("TargetDoi").AsString(500).NotNullable()
            .WithColumn("Kind").AsString(30).NotNullable();
        Execute.Sql("""
            ALTER TABLE [core].[CanonicalWorkDoiRelations]
                ALTER COLUMN [SourceDoi] nvarchar(500) COLLATE Latin1_General_100_BIN2 NOT NULL;
            ALTER TABLE [core].[CanonicalWorkDoiRelations]
                ALTER COLUMN [TargetDoi] nvarchar(500) COLLATE Latin1_General_100_BIN2 NOT NULL;
            """);
        Create.Index("UX_CanonicalWorkDoiRelations_RelationKey")
            .OnTable("CanonicalWorkDoiRelations").InSchema("core")
            .OnColumn("RelationKey").Ascending().WithOptions().Unique();
        Create.Index("IX_CanonicalWorkDoiRelations_SourceDoi")
            .OnTable("CanonicalWorkDoiRelations").InSchema("core").OnColumn("SourceDoi");
        Create.Index("IX_CanonicalWorkDoiRelations_TargetDoi")
            .OnTable("CanonicalWorkDoiRelations").InSchema("core").OnColumn("TargetDoi");
    }

    public override void Down()
    {
        Delete.Table("CanonicalWorkDoiRelations").InSchema("core");
        Delete.Table("CanonicalWorkDoiAliases").InSchema("core");
    }
}
