using FluentMigrator;

namespace ResearcherAnalysisService.Data.Migrations;

[Migration(202609140002, "Adopt the analysis, HR, and faculty schemas")]
public sealed class AdoptAnalysisSchemas : Migration
{
    public override void Up() => Execute.Sql("""
        IF OBJECT_ID(N'[dbo].[ResearcherAnalyses]', N'U') IS NOT NULL
           AND OBJECT_ID(N'[analysis].[ResearcherAnalyses]', N'U') IS NOT NULL
            THROW 51000, 'ResearcherAnalyses exists in both dbo and analysis; migration stopped without changing either table.', 1;

        IF OBJECT_ID(N'[dbo].[ArticleSummaries]', N'U') IS NOT NULL
           AND OBJECT_ID(N'[analysis].[ArticleSummaries]', N'U') IS NOT NULL
            THROW 51000, 'ArticleSummaries exists in both dbo and analysis; migration stopped without changing either table.', 1;

        IF SCHEMA_ID(N'analysis') IS NULL
            EXEC(N'CREATE SCHEMA [analysis] AUTHORIZATION [dbo]');
        IF SCHEMA_ID(N'hr') IS NULL
            EXEC(N'CREATE SCHEMA [hr] AUTHORIZATION [dbo]');
        IF SCHEMA_ID(N'faculty') IS NULL
            EXEC(N'CREATE SCHEMA [faculty] AUTHORIZATION [dbo]');

        IF OBJECT_ID(N'[dbo].[ResearcherAnalyses]', N'U') IS NOT NULL
            ALTER SCHEMA [analysis] TRANSFER [dbo].[ResearcherAnalyses];
        IF OBJECT_ID(N'[dbo].[ArticleSummaries]', N'U') IS NOT NULL
            ALTER SCHEMA [analysis] TRANSFER [dbo].[ArticleSummaries];

        DECLARE @dropForeignKeys nvarchar(max) = N'';
        SELECT @dropForeignKeys +=
            N'ALTER TABLE ' + QUOTENAME(parentSchema.[name]) + N'.' + QUOTENAME(parentTable.[name]) +
            N' DROP CONSTRAINT ' + QUOTENAME(foreignKey.[name]) + N';'
        FROM sys.foreign_keys foreignKey
        JOIN sys.tables parentTable ON parentTable.[object_id] = foreignKey.[parent_object_id]
        JOIN sys.schemas parentSchema ON parentSchema.[schema_id] = parentTable.[schema_id]
        JOIN sys.tables referencedTable ON referencedTable.[object_id] = foreignKey.[referenced_object_id]
        JOIN sys.schemas referencedSchema ON referencedSchema.[schema_id] = referencedTable.[schema_id]
        WHERE
          (
            (parentSchema.[name] = N'analysis' AND parentTable.[name] IN
                (N'ResearcherAnalyses', N'ArticleSummaries', N'ArticleSourceSnapshots',
                 N'CanonicalArticleAnalysisRuns', N'ArticleSummaryAutomationJobs',
                 N'PublicationMetricSnapshots', N'PublicationMetricsRefreshStates',
                 N'CanonicalArticleReviewRuns', N'ArticleEvaluationCases',
                 N'ArticleReviewWorkItems'))
            OR (parentSchema.[name] = N'hr' AND parentTable.[name] = N'EvidenceDossiers')
            OR (parentSchema.[name] = N'faculty' AND parentTable.[name] IN
                (N'AssistantContextVersions', N'AssistantRuns'))
          )
          AND referencedSchema.[name] NOT IN (N'analysis', N'hr', N'faculty');

        IF @dropForeignKeys <> N''
            EXEC sys.sp_executesql @dropForeignKeys;
        """);

    public override void Down() { }
}
