using FluentMigrator;

namespace ResearcherAnalysisService.Data.Migrations;

[Migration(202609140013)]
public sealed class OwnFacultyAssistant : Migration
{
    public override void Up()
    {
Execute.Sql("IF SCHEMA_ID(N'faculty') IS NULL EXEC(N'CREATE SCHEMA [faculty]');");
        Create.Table("AssistantContextVersions").InSchema("faculty")
            .WithColumn("Id").AsInt64().PrimaryKey().Identity()
            .WithColumn("PersonelID").AsString(200).NotNullable()
            .WithColumn("Version").AsInt32().NotNullable()
            .WithColumn("ContextJson").AsString(int.MaxValue).NotNullable()
            .WithColumn("ContextFingerprint").AsString(64).NotNullable()
            .WithColumn("CreatedByActorId").AsString(200).NotNullable()
            .WithColumn("CreatedAt").AsDateTimeOffset().NotNullable();
        Create.Index("UX_FacultyContexts_PersonelID_Version").OnTable("AssistantContextVersions").InSchema("faculty")
            .OnColumn("PersonelID").Ascending().OnColumn("Version").Ascending().WithOptions().Unique();
        Create.Table("AssistantRuns").InSchema("faculty")
            .WithColumn("Id").AsInt64().PrimaryKey().Identity()
            .WithColumn("RunId").AsGuid().NotNullable()
            .WithColumn("PersonelID").AsString(200).NotNullable()
            .WithColumn("ActorAuditId").AsString(200).NotNullable()
            .WithColumn("AuthorizationGrantId").AsString(400).NotNullable()
            .WithColumn("ClientRequestId").AsGuid().NotNullable()
            .WithColumn("Mode").AsString(40).NotNullable()
            .WithColumn("Language").AsString(2).NotNullable()
            .WithColumn("ContextVersionId").AsInt64().Nullable()
            .WithColumn("RetrievalPolicyVersion").AsString(100).NotNullable()
            .WithColumn("Status").AsString(20).NotNullable()
            .WithColumn("CreatedAt").AsDateTimeOffset().NotNullable()
            .WithColumn("UpdatedAt").AsDateTimeOffset().NotNullable()
            .WithColumn("AttemptCount").AsInt32().NotNullable()
            .WithColumn("AttemptToken").AsGuid().Nullable()
            .WithColumn("AttemptStartedAt").AsDateTimeOffset().Nullable()
            .WithColumn("RequestJson").AsString(int.MaxValue).NotNullable()
            .WithColumn("RetrievalManifestJson").AsString(int.MaxValue).NotNullable()
            .WithColumn("AuthorizedInputJson").AsString(int.MaxValue).Nullable()
            .WithColumn("InputFingerprint").AsString(64).Nullable()
            .WithColumn("ReportJson").AsString(int.MaxValue).Nullable()
            .WithColumn("ErrorCode").AsString(80).Nullable()
            .WithColumn("ErrorMessage").AsString(1000).Nullable();
        Create.ForeignKey("FK_FacultyRuns_Context").FromTable("AssistantRuns").InSchema("faculty")
            .ForeignColumn("ContextVersionId").ToTable("AssistantContextVersions").InSchema("faculty").PrimaryColumn("Id");
        Create.Index("UX_FacultyRuns_RunId").OnTable("AssistantRuns").InSchema("faculty").OnColumn("RunId").Ascending().WithOptions().Unique();
        Create.Index("UX_FacultyRuns_ClientRequest").OnTable("AssistantRuns").InSchema("faculty")
            .OnColumn("PersonelID").Ascending().OnColumn("ActorAuditId").Ascending().OnColumn("ClientRequestId").Ascending().WithOptions().Unique();
        Create.Index("IX_FacultyRuns_Status_Id").OnTable("AssistantRuns").InSchema("faculty")
            .OnColumn("Status").Ascending().OnColumn("Id").Ascending();
    }
    public override void Down()
    {
        Delete.Table("AssistantRuns").InSchema("faculty");
        Delete.Table("AssistantContextVersions").InSchema("faculty");
    }
}
