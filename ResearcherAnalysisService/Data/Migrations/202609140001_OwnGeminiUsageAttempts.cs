using FluentMigrator;

namespace ResearcherAnalysisService.Data.Migrations;

[Migration(202609140001, "Create the analysis-owned Gemini usage ledger")]
public sealed class OwnGeminiUsageAttempts : Migration
{
    public override void Up() => Execute.Sql("""
        IF SCHEMA_ID(N'analysis') IS NULL EXEC(N'CREATE SCHEMA [analysis] AUTHORIZATION [dbo]');
        CREATE TABLE [analysis].[GeminiUsageAttempts]
        (
            [AttemptId] uniqueidentifier NOT NULL,
            [StartedAt] datetime2 NOT NULL,
            [CompletedAt] datetime2 NULL,
            [RequestedModel] nvarchar(200) NOT NULL,
            [ReturnedModel] nvarchar(200) NULL,
            [Outcome] nvarchar(40) NOT NULL,
            [HttpStatus] int NULL,
            [PromptTokenCount] bigint NULL,
            [CachedTokenCount] bigint NULL,
            [CandidateTokenCount] bigint NULL,
            [ThoughtTokenCount] bigint NULL,
            [TotalTokenCount] bigint NULL,
            [PricingVersion] nvarchar(80) NULL,
            [EstimatedUsd] decimal(19, 9) NULL,
            CONSTRAINT [PK_GeminiUsageAttempts] PRIMARY KEY ([AttemptId])
        );
        CREATE INDEX [IX_GeminiUsageAttempts_StartedAt_AttemptId]
            ON [analysis].[GeminiUsageAttempts] ([StartedAt] DESC, [AttemptId] DESC);
        """);

    public override void Down() => Delete.Table("GeminiUsageAttempts").InSchema("analysis");
}
