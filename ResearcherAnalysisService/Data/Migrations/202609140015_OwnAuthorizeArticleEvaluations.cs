using FluentMigrator;

namespace ResearcherAnalysisService.Data.Migrations;

[Migration(202609140015, "Persist article evaluation authorization grants")]
public sealed class OwnAuthorizeArticleEvaluations : Migration
{
    public override void Up()
    {
Alter.Table("ArticleEvaluationRuns").InSchema("analysis")
            .AddColumn("ActorAuditId").AsString(200).NotNullable()
            .AddColumn("AuthorizationGrantId").AsString(400).NotNullable();
    }

    public override void Down()
    {
        Delete.Column("AuthorizationGrantId").FromTable("ArticleEvaluationRuns").InSchema("analysis");
        Delete.Column("ActorAuditId").FromTable("ArticleEvaluationRuns").InSchema("analysis");
    }
}
