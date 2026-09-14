using FluentMigrator;

namespace ResearcherAnalysisService.Data.Migrations;

[Migration(202609140001, "Own the Gemini usage ledger")]
public sealed class OwnGeminiUsageAttempts : Migration
{
    public override void Up() => Execute.Sql("""
        IF OBJECT_ID(N'[dbo].[GeminiUsageAttempts]', N'U') IS NOT NULL
           AND OBJECT_ID(N'[analysis].[GeminiUsageAttempts]', N'U') IS NOT NULL
        BEGIN
            THROW 51000, 'GeminiUsageAttempts exists in both dbo and analysis; migration stopped without changing either table.', 1;
        END;

        IF SCHEMA_ID(N'analysis') IS NULL
            EXEC(N'CREATE SCHEMA [analysis] AUTHORIZATION [dbo]');

        IF OBJECT_ID(N'[analysis].[GeminiUsageAttempts]', N'U') IS NULL
        BEGIN
            IF OBJECT_ID(N'[dbo].[GeminiUsageAttempts]', N'U') IS NOT NULL
                ALTER SCHEMA [analysis] TRANSFER [dbo].[GeminiUsageAttempts];
            ELSE
            BEGIN
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
            END;
        END;
        """);

    public override void Down() => Execute.Sql("""
        IF OBJECT_ID(N'[analysis].[GeminiUsageAttempts]', N'U') IS NOT NULL
            DROP TABLE [analysis].[GeminiUsageAttempts];
        """);
}
