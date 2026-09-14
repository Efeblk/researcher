using FluentMigrator.Runner;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ResearcherAnalysisService.Data;

namespace ResearcherAnalysisService.Tests;

public sealed class FullSchemaMigrationTransitionTests
{
    private static readonly string[] ExpectedTables =
    [
        "analysis.ArticleEvaluationAttempts", "analysis.ArticleEvaluationCases",
        "analysis.ArticleEvaluationResults", "analysis.ArticleEvaluationRuns",
        "analysis.ArticleEvaluationWorkItems", "analysis.ArticleReviewStageCheckpoints",
        "analysis.ArticleReviewWorkItems", "analysis.ArticleSourcePages",
        "analysis.ArticleSourceSnapshots", "analysis.ArticleSourceSpans",
        "analysis.ArticleSummaries", "analysis.ArticleSummaryAutomationJobs",
        "analysis.CanonicalArticleAnalysisRuns", "analysis.CanonicalArticleClaimEvidence",
        "analysis.CanonicalArticleClaims", "analysis.CanonicalArticleReviewEvidence",
        "analysis.CanonicalArticleReviewFindings", "analysis.CanonicalArticleReviewRuns",
        "analysis.CollectionChangeReceipts", "analysis.GeminiUsageAttempts",
        "analysis.PublicationMetricProviderSnapshots", "analysis.PublicationMetricSnapshots",
        "analysis.PublicationMetricsRefreshStates", "analysis.ReferencePopulationManifests",
        "analysis.ReferencePopulationMembers", "analysis.ResearcherAnalyses",
        "dbo.ResearcherAnalysisVersionInfo", "faculty.AssistantContextVersions",
        "faculty.AssistantRuns", "hr.DossierReviewActions", "hr.EvidenceDossiers"
    ];

    [Fact]
    public async Task Migration_FreshDatabase_CreatesFullOwnedSchemaWithoutSourceTablesOrForeignKeys()
    {
        await using TemporaryDatabase database = await TemporaryDatabase.CreateAsync();
        await using ServiceProvider services = CreateServices(database.ConnectionString);

        services.MigrateAnalysisDatabase();
        services.MigrateAnalysisDatabase();

        string[] actual = await database.QueryStringsAsync("""
            SELECT CONCAT(SCHEMA_NAME(schema_id), '.', name)
            FROM sys.tables
            ORDER BY 1;
            """);
        Assert.Equal(ExpectedTables, actual);
        Assert.Equal(0, await database.ScalarAsync<int>("""
            SELECT COUNT(*)
            FROM sys.foreign_keys foreignKey
            JOIN sys.tables parentTable ON parentTable.[object_id] = foreignKey.[parent_object_id]
            JOIN sys.schemas parentSchema ON parentSchema.[schema_id] = parentTable.[schema_id]
            JOIN sys.tables referencedTable ON referencedTable.[object_id] = foreignKey.[referenced_object_id]
            JOIN sys.schemas referencedSchema ON referencedSchema.[schema_id] = referencedTable.[schema_id]
            WHERE parentSchema.[name] IN (N'analysis', N'hr', N'faculty')
              AND referencedSchema.[name] NOT IN (N'analysis', N'hr', N'faculty');
            """));
        Assert.Equal(17, await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [dbo].[ResearcherAnalysisVersionInfo]"));
    }

    [Fact]
    public async Task Migration_ExistingFullSchema_AdoptsRowsAndDropsOnlyCrossSourceForeignKeys()
    {
        await using TemporaryDatabase database = await TemporaryDatabase.CreateAsync();
        await using ServiceProvider services = CreateServices(database.ConnectionString);
        services.MigrateAnalysisDatabase();
        await database.ExecuteAsync("""
            EXEC(N'CREATE SCHEMA [core]');
            CREATE TABLE [core].[Researchers]
            (
                [PersonelID] nvarchar(200) NOT NULL PRIMARY KEY
            );
            INSERT INTO [core].[Researchers] VALUES (N'adoption-sentinel');
            INSERT INTO [analysis].[ResearcherAnalyses]
                ([PersonelID], [SavedAt], [SnapshotJson], [ReportJson])
            VALUES (N'adoption-sentinel', SYSDATETIMEOFFSET(), N'{}', N'{}');
            ALTER TABLE [analysis].[ResearcherAnalyses]
                ADD CONSTRAINT [FK_LegacyResearcherAnalyses_Researchers]
                FOREIGN KEY ([PersonelID]) REFERENCES [core].[Researchers] ([PersonelID]);
            CREATE TABLE [hr].[UnrelatedStaff]
            (
                [Id] int NOT NULL PRIMARY KEY,
                [PersonelID] nvarchar(200) NOT NULL
            );
            INSERT INTO [hr].[UnrelatedStaff] VALUES (1, N'adoption-sentinel');
            ALTER TABLE [hr].[UnrelatedStaff]
                ADD CONSTRAINT [FK_UnrelatedStaff_Researchers]
                FOREIGN KEY ([PersonelID]) REFERENCES [core].[Researchers] ([PersonelID]);

            SET IDENTITY_INSERT [analysis].[PublicationMetricSnapshots] ON;
            INSERT INTO [analysis].[PublicationMetricSnapshots]
                ([Id], [PersonelID], [CatalogVersion], [SourceRevision], [ComputationYear],
                 [ComputedAt], [ResultJson], [CanonicalWorkCount], [ProviderObservationCount],
                 [UnmappedAcademicWorkCount])
            VALUES
                (7001, N'adoption-sentinel', N'catalog-v1', 42, 2026,
                 '2026-09-14T06:00:00', N'{"score":7}', 3, 4, 0);
            SET IDENTITY_INSERT [analysis].[PublicationMetricSnapshots] OFF;

            SET IDENTITY_INSERT [hr].[EvidenceDossiers] ON;
            INSERT INTO [hr].[EvidenceDossiers]
                ([Id], [PersonelID], [CreatedByActorId], [CreatedAt], [PolicyVersion],
                 [PublicationMetricSnapshotId], [InputFingerprint], [InputManifestJson], [DossierJson])
            VALUES
                (7101, N'adoption-sentinel', N'hr-actor', '2026-09-14T09:00:00+03:00',
                 N'hr-policy-v1', 7001, REPLICATE(N'a', 64), N'{"input":1}', N'{"dossier":1}');
            SET IDENTITY_INSERT [hr].[EvidenceDossiers] OFF;

            SET IDENTITY_INSERT [hr].[DossierReviewActions] ON;
            INSERT INTO [hr].[DossierReviewActions]
                ([Id], [DossierId], [ActorAuditId], [ClientRequestId], [ActionType],
                 [EvidenceReference], [Note], [RecordedAt])
            VALUES
                (7102, 7101, N'reviewer-audit', '11111111-1111-1111-1111-111111111111',
                 N'Approve', N'evidence:42', N'preserve review audit',
                 '2026-09-14T09:05:00+03:00');
            SET IDENTITY_INSERT [hr].[DossierReviewActions] OFF;

            SET IDENTITY_INSERT [faculty].[AssistantContextVersions] ON;
            INSERT INTO [faculty].[AssistantContextVersions]
                ([Id], [PersonelID], [Version], [ContextJson], [ContextFingerprint],
                 [CreatedByActorId], [CreatedAt])
            VALUES
                (7201, N'adoption-sentinel', 9, N'{"context":1}', REPLICATE(N'b', 64),
                 N'faculty-context-actor', '2026-09-14T09:10:00+03:00');
            SET IDENTITY_INSERT [faculty].[AssistantContextVersions] OFF;

            SET IDENTITY_INSERT [faculty].[AssistantRuns] ON;
            INSERT INTO [faculty].[AssistantRuns]
                ([Id], [RunId], [PersonelID], [ActorAuditId], [AuthorizationGrantId],
                 [ClientRequestId], [Mode], [Language], [ContextVersionId],
                 [RetrievalPolicyVersion], [Status], [CreatedAt], [UpdatedAt], [AttemptCount],
                 [AttemptToken], [AttemptStartedAt], [RequestJson], [RetrievalManifestJson],
                 [AuthorizedInputJson], [InputFingerprint], [ReportJson], [ErrorCode], [ErrorMessage])
            VALUES
                (7202, '22222222-2222-2222-2222-222222222222', N'adoption-sentinel',
                 N'faculty-audit', N'faculty-grant', '33333333-3333-3333-3333-333333333333',
                 N'Assistant', N'en', 7201, N'retrieval-v1', N'Succeeded',
                 '2026-09-14T09:15:00+03:00', '2026-09-14T09:16:00+03:00', 1,
                 '44444444-4444-4444-4444-444444444444', '2026-09-14T09:15:30+03:00',
                 N'{"request":1}', N'{"retrieval":1}', N'{"authorized":1}',
                 REPLICATE(N'c', 64), N'{"report":1}', NULL, NULL);
            SET IDENTITY_INSERT [faculty].[AssistantRuns] OFF;

            DELETE FROM [dbo].[ResearcherAnalysisVersionInfo] WHERE [Version] > 202609140001;
            """);

        services.MigrateAnalysisDatabase();

        Assert.Equal(1, await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [analysis].[ResearcherAnalyses] WHERE [PersonelID]=N'adoption-sentinel'"));
        Assert.Equal(0, await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.foreign_keys WHERE [name]=N'FK_LegacyResearcherAnalyses_Researchers'"));
        Assert.Equal(1, await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.foreign_keys WHERE [name]=N'FK_UnrelatedStaff_Researchers'"));
        Assert.Equal(1, await database.ScalarAsync<int>("""
            SELECT COUNT(*) FROM sys.foreign_keys
            WHERE [name]=N'FK_CanonicalArticleClaims_AnalysisRuns';
            """));
        Assert.Equal(3, await database.ScalarAsync<int>("""
            SELECT COUNT(*) FROM sys.foreign_keys
            WHERE [name] IN
                (N'FK_HrEvidenceDossiers_MetricSnapshots',
                 N'FK_HrDossierReviewActions_Dossier', N'FK_FacultyRuns_Context');
            """));
        Assert.Equal(1, await database.ScalarAsync<int>("""
            SELECT COUNT(*)
            FROM [analysis].[PublicationMetricSnapshots] metric
            JOIN [hr].[EvidenceDossiers] dossier
              ON dossier.[PublicationMetricSnapshotId]=metric.[Id]
            JOIN [hr].[DossierReviewActions] review ON review.[DossierId]=dossier.[Id]
            WHERE metric.[Id]=7001 AND metric.[SourceRevision]=42
              AND metric.[ResultJson]=N'{"score":7}'
              AND dossier.[Id]=7101 AND dossier.[CreatedByActorId]=N'hr-actor'
              AND dossier.[InputFingerprint]=REPLICATE(N'a', 64)
              AND review.[Id]=7102 AND review.[ActorAuditId]=N'reviewer-audit'
              AND review.[ClientRequestId]='11111111-1111-1111-1111-111111111111'
              AND review.[EvidenceReference]=N'evidence:42'
              AND review.[Note]=N'preserve review audit';
            """));
        Assert.Equal(1, await database.ScalarAsync<int>("""
            SELECT COUNT(*)
            FROM [faculty].[AssistantContextVersions] context
            JOIN [faculty].[AssistantRuns] run ON run.[ContextVersionId]=context.[Id]
            WHERE context.[Id]=7201 AND context.[Version]=9
              AND context.[CreatedByActorId]=N'faculty-context-actor'
              AND run.[Id]=7202 AND run.[RunId]='22222222-2222-2222-2222-222222222222'
              AND run.[ActorAuditId]=N'faculty-audit'
              AND run.[AuthorizationGrantId]=N'faculty-grant'
              AND run.[ClientRequestId]='33333333-3333-3333-3333-333333333333'
              AND run.[AttemptToken]='44444444-4444-4444-4444-444444444444'
              AND run.[ReportJson]=N'{"report":1}';
            """));
        Assert.Equal(7001L, await database.ScalarAsync<long>(
            "SELECT CONVERT(bigint, IDENT_CURRENT(N'analysis.PublicationMetricSnapshots'))"));
        Assert.Equal(7101L, await database.ScalarAsync<long>(
            "SELECT CONVERT(bigint, IDENT_CURRENT(N'hr.EvidenceDossiers'))"));
        Assert.Equal(7102L, await database.ScalarAsync<long>(
            "SELECT CONVERT(bigint, IDENT_CURRENT(N'hr.DossierReviewActions'))"));
        Assert.Equal(7201L, await database.ScalarAsync<long>(
            "SELECT CONVERT(bigint, IDENT_CURRENT(N'faculty.AssistantContextVersions'))"));
        Assert.Equal(7202L, await database.ScalarAsync<long>(
            "SELECT CONVERT(bigint, IDENT_CURRENT(N'faculty.AssistantRuns'))"));
        Assert.Equal(17, await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [dbo].[ResearcherAnalysisVersionInfo]"));
    }

    [Fact]
    public async Task Migration_ExistingAnalysisRun_AddsNullableSourceIdentityWithoutBackfill()
    {
        await using TemporaryDatabase database = await TemporaryDatabase.CreateAsync();
        await using ServiceProvider services = CreateServices(database.ConnectionString);
        using (IServiceScope scope = services.CreateScope())
            scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp(202609140016);
        await database.ExecuteAsync("""
            INSERT INTO [analysis].[ArticleSummaries]
                ([AcademicWorkId], [OriginalAcademicWorkId], [PersonelID], [SavedAt],
                 [SourceUrl], [SourceHash], [SourceKind], [ExtractionVersion],
                 [SnapshotJson], [ReportJson])
            VALUES
                (NULL, 77, N'source-identity-legacy', '2026-09-14T07:00:00+00:00', NULL,
                 REPLICATE(N'e', 64), N'Html', N'legacy-v1', N'{}', N'{}');
            DECLARE @summaryId bigint = SCOPE_IDENTITY();
            INSERT INTO [analysis].[ArticleSourceSnapshots]
                ([CanonicalWorkId], [ExtractedTextHash], [SourceKind], [ExtractionVersion], [CreatedAt])
            VALUES (77, REPLICATE(N'f', 64), N'Html', N'legacy-v1',
                    '2026-09-14T07:01:00+00:00');
            DECLARE @snapshotId bigint = SCOPE_IDENTITY();
            INSERT INTO [analysis].[CanonicalArticleAnalysisRuns]
                ([CanonicalWorkId], [ArticleSourceSnapshotId], [SavedArticleSummaryId],
                 [AnalyzedAt], [SourceAcquiredAt], [SourceOrigin], [Language], [Model],
                 [PromptVersion], [ExtractionMethod], [ProcessedChunks], [TotalChunks],
                 [ProcessedPages], [TextBearingPages], [TotalPages], [SelectedClaimsOmitted],
                 [IsPartial], [OmissionReasonsJson], [VerificationStatus], [VerificationModel],
                 [VerificationPromptVersion], [UsesSameModelFamily])
            VALUES
                (77, @snapshotId, @summaryId, '2026-09-14T07:02:00+00:00',
                 '2026-09-14T07:01:00+00:00', N'legacy-source', N'en', N'legacy-model',
                 N'prompt-v1', N'Html', 1, 1, 1, 1, 1, 0, 0, N'[]', N'Complete',
                 N'legacy-verifier', N'verifier-v1', 0);
            """);

        services.MigrateAnalysisDatabase();

        Assert.Equal(1, await database.ScalarAsync<int>("""
            SELECT COUNT(*) FROM [analysis].[CanonicalArticleAnalysisRuns]
            WHERE [CanonicalWorkId]=77 AND [SourceIdentityHash] IS NULL;
            """));
        Assert.Equal(1, await database.ScalarAsync<int>("""
            SELECT COUNT(*)
            FROM sys.columns columnDefinition
            WHERE columnDefinition.[object_id]=OBJECT_ID(N'analysis.CanonicalArticleAnalysisRuns')
              AND columnDefinition.[name]=N'SourceIdentityHash'
              AND columnDefinition.[max_length]=128
              AND columnDefinition.[is_nullable]=1
              AND columnDefinition.[collation_name]=N'Latin1_General_100_BIN2';
            """));
        Assert.Equal(1, await database.ScalarAsync<int>("""
            SELECT COUNT(*) FROM [dbo].[ResearcherAnalysisVersionInfo]
            WHERE [Version]=202609140017;
            """));
    }

    [Fact]
    public async Task Migration_LegacyDboOwnedTables_TransfersRowsAndForeignKeysSafely()
    {
        await using TemporaryDatabase database = await TemporaryDatabase.CreateAsync();
        await database.ExecuteAsync("""
            CREATE TABLE [dbo].[Researchers]
            (
                [PersonelID] nvarchar(200) NOT NULL PRIMARY KEY
            );
            CREATE TABLE [dbo].[AcademicWorks]
            (
                [Id] int NOT NULL PRIMARY KEY
            );
            INSERT INTO [dbo].[Researchers] VALUES (N'legacy-transfer');
            INSERT INTO [dbo].[AcademicWorks] VALUES (91);

            CREATE TABLE [dbo].[ResearcherAnalyses]
            (
                [Id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ResearcherAnalyses] PRIMARY KEY,
                [PersonelID] nvarchar(200) NOT NULL,
                [SavedAt] datetimeoffset NOT NULL,
                [SnapshotJson] nvarchar(max) NOT NULL,
                [ReportJson] nvarchar(max) NOT NULL,
                CONSTRAINT [FK_ResearcherAnalyses_Researchers]
                    FOREIGN KEY ([PersonelID]) REFERENCES [dbo].[Researchers] ([PersonelID])
            );
            CREATE INDEX [IX_ResearcherAnalyses_PersonelID_Id]
                ON [dbo].[ResearcherAnalyses] ([PersonelID], [Id] DESC);

            CREATE TABLE [dbo].[ArticleSummaries]
            (
                [Id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ArticleSummaries] PRIMARY KEY,
                [AcademicWorkId] int NULL,
                [OriginalAcademicWorkId] int NOT NULL,
                [PersonelID] nvarchar(200) NOT NULL,
                [SavedAt] datetimeoffset NOT NULL,
                [SourceUrl] nvarchar(2000) NULL,
                [SourceHash] nvarchar(64) NOT NULL,
                [SourceKind] nvarchar(20) NOT NULL,
                [ExtractionVersion] nvarchar(100) NOT NULL,
                [SnapshotJson] nvarchar(max) NOT NULL,
                [ReportJson] nvarchar(max) NOT NULL,
                CONSTRAINT [FK_ArticleSummaries_Researchers]
                    FOREIGN KEY ([PersonelID]) REFERENCES [dbo].[Researchers] ([PersonelID]),
                CONSTRAINT [FK_ArticleSummaries_AcademicWorks_AcademicWorkId]
                    FOREIGN KEY ([AcademicWorkId]) REFERENCES [dbo].[AcademicWorks] ([Id])
                    ON DELETE SET NULL
            );
            CREATE INDEX [IX_ArticleSummaries_PersonelID_OriginalAcademicWorkId_Id]
                ON [dbo].[ArticleSummaries] ([PersonelID], [OriginalAcademicWorkId], [Id] DESC);

            SET IDENTITY_INSERT [dbo].[ResearcherAnalyses] ON;
            INSERT INTO [dbo].[ResearcherAnalyses]
                ([Id], [PersonelID], [SavedAt], [SnapshotJson], [ReportJson])
            VALUES (8101, N'legacy-transfer', '2026-09-14T07:00:00+00:00',
                    N'{"legacySnapshot":1}', N'{"legacyReport":1}');
            SET IDENTITY_INSERT [dbo].[ResearcherAnalyses] OFF;
            SET IDENTITY_INSERT [dbo].[ArticleSummaries] ON;
            INSERT INTO [dbo].[ArticleSummaries]
                ([Id], [AcademicWorkId], [OriginalAcademicWorkId], [PersonelID], [SavedAt],
                 [SourceUrl], [SourceHash], [SourceKind], [ExtractionVersion],
                 [SnapshotJson], [ReportJson])
            VALUES (8201, 91, 91, N'legacy-transfer', '2026-09-14T07:05:00+00:00',
                    N'https://example.test/legacy', REPLICATE(N'd', 64), N'Html', N'legacy-v1',
                    N'{"articleSnapshot":1}', N'{"articleReport":1}');
            SET IDENTITY_INSERT [dbo].[ArticleSummaries] OFF;
            """);
        await using ServiceProvider services = CreateServices(database.ConnectionString);

        services.MigrateAnalysisDatabase();

        Assert.Equal(0, await database.ScalarAsync<int>("""
            SELECT COUNT(*) FROM sys.tables
            WHERE schema_id=SCHEMA_ID('dbo')
              AND name IN (N'ResearcherAnalyses', N'ArticleSummaries');
            """));
        Assert.Equal(1, await database.ScalarAsync<int>("""
            SELECT COUNT(*) FROM [analysis].[ResearcherAnalyses]
            WHERE [Id]=8101 AND [PersonelID]=N'legacy-transfer'
              AND [SnapshotJson]=N'{"legacySnapshot":1}' AND [ReportJson]=N'{"legacyReport":1}';
            """));
        Assert.Equal(1, await database.ScalarAsync<int>("""
            SELECT COUNT(*) FROM [analysis].[ArticleSummaries]
            WHERE [Id]=8201 AND [AcademicWorkId]=91 AND [OriginalAcademicWorkId]=91
              AND [PersonelID]=N'legacy-transfer' AND [SourceHash]=REPLICATE(N'd', 64)
              AND [SnapshotJson]=N'{"articleSnapshot":1}' AND [ReportJson]=N'{"articleReport":1}';
            """));
        Assert.Equal(0, await database.ScalarAsync<int>("""
            SELECT COUNT(*) FROM sys.foreign_keys foreignKey
            WHERE foreignKey.[parent_object_id] IN
                (OBJECT_ID(N'analysis.ResearcherAnalyses'), OBJECT_ID(N'analysis.ArticleSummaries'))
              AND foreignKey.[referenced_object_id] IN
                (OBJECT_ID(N'dbo.Researchers'), OBJECT_ID(N'dbo.AcademicWorks'));
            """));
        Assert.Equal(8101L, await database.ScalarAsync<long>(
            "SELECT CONVERT(bigint, IDENT_CURRENT(N'analysis.ResearcherAnalyses'))"));
        Assert.Equal(8201L, await database.ScalarAsync<long>(
            "SELECT CONVERT(bigint, IDENT_CURRENT(N'analysis.ArticleSummaries'))"));
    }

    [Fact]
    public async Task Migration_DualOwnedTableLocations_FailsWithoutChangingEitherLocation()
    {
        await using TemporaryDatabase database = await TemporaryDatabase.CreateAsync();
        await database.ExecuteAsync("""
            EXEC(N'CREATE SCHEMA [analysis]');
            CREATE TABLE [dbo].[ResearcherAnalyses]
                ([Id] bigint NOT NULL PRIMARY KEY, [ReportJson] nvarchar(max) NOT NULL);
            CREATE TABLE [analysis].[ResearcherAnalyses]
                ([Id] bigint NOT NULL PRIMARY KEY, [ReportJson] nvarchar(max) NOT NULL);
            CREATE TABLE [dbo].[ArticleSummaries]
                ([Id] bigint NOT NULL PRIMARY KEY, [ReportJson] nvarchar(max) NOT NULL);
            INSERT INTO [dbo].[ResearcherAnalyses] VALUES (8301, N'{"location":"dbo"}');
            INSERT INTO [analysis].[ResearcherAnalyses] VALUES (8302, N'{"location":"analysis"}');
            INSERT INTO [dbo].[ArticleSummaries] VALUES (8401, N'{"mustStay":"dbo"}');
            """);
        await using ServiceProvider services = CreateServices(database.ConnectionString);

        Exception error = Assert.ThrowsAny<Exception>(services.MigrateAnalysisDatabase);

        Assert.Contains("ResearcherAnalyses exists in both dbo and analysis", error.ToString(),
            StringComparison.Ordinal);
        Assert.Equal(NormalizeJson("{\"location\":\"dbo\"}"), NormalizeJson(
            await database.ScalarAsync<string>(
                "SELECT [ReportJson] FROM [dbo].[ResearcherAnalyses] WHERE [Id]=8301")));
        Assert.Equal(NormalizeJson("{\"location\":\"analysis\"}"), NormalizeJson(
            await database.ScalarAsync<string>(
                "SELECT [ReportJson] FROM [analysis].[ResearcherAnalyses] WHERE [Id]=8302")));
        Assert.Equal(1, await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [dbo].[ArticleSummaries] WHERE [Id]=8401"));
        Assert.Equal(0, await database.ScalarAsync<int>("""
            SELECT COUNT(*) FROM sys.tables
            WHERE schema_id=SCHEMA_ID('analysis') AND name=N'ArticleSummaries';
            """));
        Assert.Equal(0, await database.ScalarAsync<int>("""
            SELECT COUNT(*) FROM [dbo].[ResearcherAnalysisVersionInfo]
            WHERE [Version]=202609140002;
            """));
    }

    [Fact]
    public async Task Migration_DualArticleSummaryLocations_FailsBeforeTransferringResearcherAnalyses()
    {
        await using TemporaryDatabase database = await TemporaryDatabase.CreateAsync();
        await database.ExecuteAsync("""
            EXEC(N'CREATE SCHEMA [analysis]');
            CREATE TABLE [dbo].[ResearcherAnalyses]
                ([Id] bigint NOT NULL PRIMARY KEY, [ReportJson] nvarchar(max) NOT NULL);
            CREATE TABLE [dbo].[ArticleSummaries]
                ([Id] bigint NOT NULL PRIMARY KEY, [ReportJson] nvarchar(max) NOT NULL);
            CREATE TABLE [analysis].[ArticleSummaries]
                ([Id] bigint NOT NULL PRIMARY KEY, [ReportJson] nvarchar(max) NOT NULL);
            INSERT INTO [dbo].[ResearcherAnalyses] VALUES (8501, N'{"mustStay":"dbo"}');
            INSERT INTO [dbo].[ArticleSummaries] VALUES (8601, N'{"location":"dbo"}');
            INSERT INTO [analysis].[ArticleSummaries] VALUES (8602, N'{"location":"analysis"}');
            """);
        await using ServiceProvider services = CreateServices(database.ConnectionString);

        Exception error = Assert.ThrowsAny<Exception>(services.MigrateAnalysisDatabase);

        Assert.Contains("ArticleSummaries exists in both dbo and analysis", error.ToString(),
            StringComparison.Ordinal);
        Assert.Equal(1, await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [dbo].[ResearcherAnalyses] WHERE [Id]=8501"));
        Assert.Equal(0, await database.ScalarAsync<int>("""
            SELECT COUNT(*) FROM sys.tables
            WHERE schema_id=SCHEMA_ID('analysis') AND name=N'ResearcherAnalyses';
            """));
        Assert.Equal(1, await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [dbo].[ArticleSummaries] WHERE [Id]=8601"));
        Assert.Equal(1, await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [analysis].[ArticleSummaries] WHERE [Id]=8602"));
        Assert.Equal(0, await database.ScalarAsync<int>("""
            SELECT COUNT(*) FROM [dbo].[ResearcherAnalysisVersionInfo]
            WHERE [Version]=202609140002;
            """));
    }

    [Fact]
    public async Task Migration_PartialOwnedGroup_FailsWithoutCompletingThatGroup()
    {
        await using TemporaryDatabase database = await TemporaryDatabase.CreateAsync();
        await database.ExecuteAsync("""
            EXEC(N'CREATE SCHEMA [analysis]');
            CREATE TABLE [analysis].[ArticleSourceSnapshots] ([Id] bigint NOT NULL PRIMARY KEY);
            """);
        await using ServiceProvider services = CreateServices(database.ConnectionString);

        Exception error = Assert.ThrowsAny<Exception>(services.MigrateAnalysisDatabase);

        Assert.Contains("canonical article evidence schema is incomplete", error.ToString(),
            StringComparison.Ordinal);
        Assert.Equal(0, await database.ScalarAsync<int>("""
            SELECT COUNT(*) FROM sys.tables
            WHERE schema_id=SCHEMA_ID('analysis') AND name=N'ArticleSourcePages';
            """));
        Assert.Equal(0, await database.ScalarAsync<int>("""
            SELECT COUNT(*) FROM [dbo].[ResearcherAnalysisVersionInfo]
            WHERE [Version]=202609140005;
            """));
    }

    private static ServiceProvider CreateServices(string connectionString)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:UsageDatabase"] = connectionString
            }).Build();
        ServiceCollection services = new();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddAnalysisDatabaseMigrations(configuration);
        return services.BuildServiceProvider();
    }

    private static string NormalizeJson(string value) => value.Replace(" ", "", StringComparison.Ordinal);

    private sealed class TemporaryDatabase : IAsyncDisposable
    {
        private readonly string _databaseName;
        private readonly string _masterConnectionString;
        public string ConnectionString { get; }

        private TemporaryDatabase(string databaseName, string masterConnectionString,
            string connectionString)
        {
            _databaseName = databaseName;
            _masterConnectionString = masterConnectionString;
            ConnectionString = connectionString;
        }

        public static async Task<TemporaryDatabase> CreateAsync()
        {
            string databaseName = "FullSchemaMigrationTransition_" + Guid.NewGuid().ToString("N");
            SqlConnectionStringBuilder connection = new(
                Environment.GetEnvironmentVariable("ACADEMIC_TEST_SQLSERVER") ??
                @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;Encrypt=true;TrustServerCertificate=true");
            connection.Encrypt = SqlConnectionEncryptOption.Mandatory;
            connection.InitialCatalog = "master";
            string masterConnectionString = connection.ConnectionString;
            await using SqlConnection master = new(masterConnectionString);
            await master.OpenAsync();
            await using SqlCommand create = master.CreateCommand();
            create.CommandText = $"CREATE DATABASE [{databaseName}]";
            await create.ExecuteNonQueryAsync();
            connection.InitialCatalog = databaseName;
            return new(databaseName, masterConnectionString, connection.ConnectionString);
        }

        public async Task ExecuteAsync(string sql)
        {
            await using SqlConnection connection = new(ConnectionString);
            await connection.OpenAsync();
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        public async Task<T> ScalarAsync<T>(string sql)
        {
            await using SqlConnection connection = new(ConnectionString);
            await connection.OpenAsync();
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = sql;
            return (T)(await command.ExecuteScalarAsync())!;
        }

        public async Task<string[]> QueryStringsAsync(string sql)
        {
            List<string> values = [];
            await using SqlConnection connection = new(ConnectionString);
            await connection.OpenAsync();
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = sql;
            await using SqlDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                values.Add(reader.GetString(0));
            return values.ToArray();
        }

        public async ValueTask DisposeAsync()
        {
            await using SqlConnection master = new(_masterConnectionString);
            await master.OpenAsync();
            await using SqlCommand drop = master.CreateCommand();
            drop.CommandText = $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                $"DROP DATABASE [{_databaseName}]";
            await drop.ExecuteNonQueryAsync();
        }
    }
}
