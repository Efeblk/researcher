using FluentMigrator;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data.Migrations.Core;

[Migration(202609120013, "Persist article evaluation authorization grants")]
public sealed class AuthorizeArticleEvaluations : Migration
{
    public override void Up()
    {
        Alter.Table("ArticleEvaluationRuns").InSchema("analysis")
            .AddColumn("ActorAuditId").AsString(200).Nullable()
            .AddColumn("AuthorizationGrantId").AsString(400).Nullable();
        Execute.Sql("""
            UPDATE [analysis].[ArticleEvaluationRuns]
            SET [ActorAuditId] = N'legacy-unowned',
                [AuthorizationGrantId] = N'legacy-unowned'
            WHERE [ActorAuditId] IS NULL OR [AuthorizationGrantId] IS NULL;
            ALTER TABLE [analysis].[ArticleEvaluationRuns] ALTER COLUMN [ActorAuditId] nvarchar(200) NOT NULL;
            ALTER TABLE [analysis].[ArticleEvaluationRuns] ALTER COLUMN [AuthorizationGrantId] nvarchar(400) NOT NULL;
            """);
    }

    public override void Down()
    {
        Delete.Column("AuthorizationGrantId").FromTable("ArticleEvaluationRuns").InSchema("analysis");
        Delete.Column("ActorAuditId").FromTable("ArticleEvaluationRuns").InSchema("analysis");
    }
}
