using FluentMigrator;

namespace ResearcherAnalysisService.Data.Migrations;

[Migration(202609140017, "Track stable source identity on canonical article analysis runs")]
public sealed class AddCanonicalSourceIdentityHash : Migration
{
    public override void Up()
    {
        if (Schema.Schema("analysis").Table("CanonicalArticleAnalysisRuns")
            .Column("SourceIdentityHash").Exists())
            return;

        Alter.Table("CanonicalArticleAnalysisRuns").InSchema("analysis")
            .AddColumn("SourceIdentityHash").AsString(64).Nullable();
        Execute.Sql("""
            ALTER TABLE [analysis].[CanonicalArticleAnalysisRuns]
                ALTER COLUMN [SourceIdentityHash]
                    nvarchar(64) COLLATE Latin1_General_100_BIN2 NULL;
            """);
    }

    public override void Down() =>
        Delete.Column("SourceIdentityHash").FromTable("CanonicalArticleAnalysisRuns").InSchema("analysis");
}
