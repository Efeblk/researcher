using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data;

public sealed class AcademicDatabaseInitializer
{
    private readonly AcademicDbContext _dbContext;
    private readonly IConfiguration _configuration;

    public AcademicDatabaseInitializer(
        AcademicDbContext dbContext,
        IConfiguration configuration)
    {
        _dbContext = dbContext;
        _configuration = configuration;
    }

    public async Task EnsureReadyAsync()
    {
        string? provider = null;

        await _dbContext.Database.EnsureCreatedAsync();

        provider = _configuration["Database:Provider"]
            ?? DatabaseConfiguration.SqliteProvider;

        if (provider.Equals(
            DatabaseConfiguration.SqliteProvider,
            StringComparison.OrdinalIgnoreCase))
        {
            await CreateSqliteAcademicWorksTableAsync();
            await RemoveObsoleteSqliteDataAsync();
            await CreateSqlitePublicationSummariesTableAsync();
            await CreateSqlitePublicationDisplayApprovalsTableAsync();
            await CreateSqliteOrcidTablesAsync();
            await AddSqliteAcademicWorkColumnsAsync();
            return;
        }

        if (provider.Equals(
            DatabaseConfiguration.SqlServerProvider,
            StringComparison.OrdinalIgnoreCase))
        {
            await CreateSqlServerAcademicWorksTableAsync();
            await RemoveObsoleteSqlServerDataAsync();
            await CreateSqlServerPublicationSummariesTableAsync();
            await CreateSqlServerPublicationDisplayApprovalsTableAsync();
            await CreateSqlServerOrcidTablesAsync();
            await AddSqlServerAcademicWorkColumnsAsync();
        }
    }

    private async Task CreateSqliteOrcidTablesAsync()
    {
        string createTablesSql =
            """
            CREATE TABLE IF NOT EXISTS "OrcidProfiles" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_OrcidProfiles" PRIMARY KEY AUTOINCREMENT,
                "ResearcherId" INTEGER NOT NULL,
                "DisplayName" TEXT NULL,
                "GivenNames" TEXT NULL,
                "FamilyName" TEXT NULL,
                "CreditName" TEXT NULL,
                "Biography" TEXT NULL,
                "CountryCodes" TEXT NULL,
                "Keywords" TEXT NULL,
                "CurrentOrganization" TEXT NULL,
                "CurrentDepartment" TEXT NULL,
                "CurrentRoleTitle" TEXT NULL,
                "WorksCount" INTEGER NOT NULL,
                "EmploymentsCount" INTEGER NOT NULL,
                "EducationsCount" INTEGER NOT NULL,
                "FundingsCount" INTEGER NOT NULL,
                "PeerReviewsCount" INTEGER NOT NULL,
                "RecordLastModifiedAt" TEXT NULL,
                "LastUpdatedAt" TEXT NOT NULL,
                "ResearcherUrlsJson" TEXT NULL,
                "ExternalIdentifiersJson" TEXT NULL,
                "EmploymentsJson" TEXT NULL,
                "EducationsJson" TEXT NULL,
                "ActivitiesJson" TEXT NULL,
                "RawDataJson" TEXT NULL,
                CONSTRAINT "FK_OrcidProfiles_Researchers_ResearcherId"
                    FOREIGN KEY ("ResearcherId") REFERENCES "Researchers" ("Id") ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_OrcidProfiles_ResearcherId"
                ON "OrcidProfiles" ("ResearcherId");

            CREATE TABLE IF NOT EXISTS "OrcidWorks" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_OrcidWorks" PRIMARY KEY AUTOINCREMENT,
                "OrcidProfileId" INTEGER NOT NULL,
                "PutCode" INTEGER NOT NULL,
                "Title" TEXT NULL,
                "Subtitle" TEXT NULL,
                "TranslatedTitle" TEXT NULL,
                "WorkType" TEXT NULL,
                "PublicationYear" INTEGER NULL,
                "PublicationDate" TEXT NULL,
                "JournalTitle" TEXT NULL,
                "Doi" TEXT NULL,
                "Url" TEXT NULL,
                "Authors" TEXT NULL,
                "LanguageCode" TEXT NULL,
                "CountryCode" TEXT NULL,
                "ShortDescription" TEXT NULL,
                "Citation" TEXT NULL,
                "SourceName" TEXT NULL,
                "Visibility" TEXT NULL,
                "RecordLastModifiedAt" TEXT NULL,
                "Category" TEXT NOT NULL,
                "CategorySource" TEXT NOT NULL,
                "ExternalIdentifiersJson" TEXT NULL,
                "ContributorsJson" TEXT NULL,
                "RawDataJson" TEXT NULL,
                CONSTRAINT "FK_OrcidWorks_OrcidProfiles_OrcidProfileId"
                    FOREIGN KEY ("OrcidProfileId") REFERENCES "OrcidProfiles" ("Id") ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_OrcidWorks_OrcidProfileId_PutCode"
                ON "OrcidWorks" ("OrcidProfileId", "PutCode");
            """;

        await _dbContext.Database.ExecuteSqlRawAsync(createTablesSql);
    }

    private async Task CreateSqlServerOrcidTablesAsync()
    {
        string createTablesSql =
            """
            IF OBJECT_ID(N'[OrcidProfiles]', N'U') IS NULL
            BEGIN
                CREATE TABLE [OrcidProfiles] (
                    [Id] int NOT NULL IDENTITY,
                    [ResearcherId] int NOT NULL,
                    [DisplayName] nvarchar(500) NULL,
                    [GivenNames] nvarchar(250) NULL,
                    [FamilyName] nvarchar(250) NULL,
                    [CreditName] nvarchar(500) NULL,
                    [Biography] nvarchar(max) NULL,
                    [CountryCodes] nvarchar(250) NULL,
                    [Keywords] nvarchar(4000) NULL,
                    [CurrentOrganization] nvarchar(1000) NULL,
                    [CurrentDepartment] nvarchar(1000) NULL,
                    [CurrentRoleTitle] nvarchar(500) NULL,
                    [WorksCount] int NOT NULL,
                    [EmploymentsCount] int NOT NULL,
                    [EducationsCount] int NOT NULL,
                    [FundingsCount] int NOT NULL,
                    [PeerReviewsCount] int NOT NULL,
                    [RecordLastModifiedAt] datetime2 NULL,
                    [LastUpdatedAt] datetime2 NOT NULL,
                    [ResearcherUrlsJson] nvarchar(max) NULL,
                    [ExternalIdentifiersJson] nvarchar(max) NULL,
                    [EmploymentsJson] nvarchar(max) NULL,
                    [EducationsJson] nvarchar(max) NULL,
                    [ActivitiesJson] nvarchar(max) NULL,
                    [RawDataJson] nvarchar(max) NULL,
                    CONSTRAINT [PK_OrcidProfiles] PRIMARY KEY ([Id]),
                    CONSTRAINT [FK_OrcidProfiles_Researchers_ResearcherId]
                        FOREIGN KEY ([ResearcherId]) REFERENCES [Researchers] ([Id]) ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX [IX_OrcidProfiles_ResearcherId]
                    ON [OrcidProfiles] ([ResearcherId]);
            END;

            IF OBJECT_ID(N'[OrcidWorks]', N'U') IS NULL
            BEGIN
                CREATE TABLE [OrcidWorks] (
                    [Id] int NOT NULL IDENTITY,
                    [OrcidProfileId] int NOT NULL,
                    [PutCode] bigint NOT NULL,
                    [Title] nvarchar(2000) NULL,
                    [Subtitle] nvarchar(2000) NULL,
                    [TranslatedTitle] nvarchar(2000) NULL,
                    [WorkType] nvarchar(100) NULL,
                    [PublicationYear] int NULL,
                    [PublicationDate] datetime2 NULL,
                    [JournalTitle] nvarchar(2000) NULL,
                    [Doi] nvarchar(500) NULL,
                    [Url] nvarchar(2000) NULL,
                    [Authors] nvarchar(4000) NULL,
                    [LanguageCode] nvarchar(20) NULL,
                    [CountryCode] nvarchar(20) NULL,
                    [ShortDescription] nvarchar(max) NULL,
                    [Citation] nvarchar(max) NULL,
                    [SourceName] nvarchar(500) NULL,
                    [Visibility] nvarchar(50) NULL,
                    [RecordLastModifiedAt] datetime2 NULL,
                    [Category] nvarchar(50) NOT NULL,
                    [CategorySource] nvarchar(50) NOT NULL,
                    [ExternalIdentifiersJson] nvarchar(max) NULL,
                    [ContributorsJson] nvarchar(max) NULL,
                    [RawDataJson] nvarchar(max) NULL,
                    CONSTRAINT [PK_OrcidWorks] PRIMARY KEY ([Id]),
                    CONSTRAINT [FK_OrcidWorks_OrcidProfiles_OrcidProfileId]
                        FOREIGN KEY ([OrcidProfileId]) REFERENCES [OrcidProfiles] ([Id]) ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX [IX_OrcidWorks_OrcidProfileId_PutCode]
                    ON [OrcidWorks] ([OrcidProfileId], [PutCode]);
            END;
            """;

        await _dbContext.Database.ExecuteSqlRawAsync(createTablesSql);
    }

    private async Task CreateSqliteAcademicWorksTableAsync()
    {
        string? createTableSql = null;
        string? createResearcherIndexSql = null;
        string? createProviderIndexSql = null;

        createTableSql =
            """
            CREATE TABLE IF NOT EXISTS "AcademicWorks" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_AcademicWorks" PRIMARY KEY AUTOINCREMENT,
                "ResearcherId" INTEGER NOT NULL,
                "Provider" TEXT NOT NULL,
                "ProviderWorkId" TEXT NULL,
                "Title" TEXT NULL,
                "PublicationYear" INTEGER NULL,
                "PublicationDate" TEXT NULL,
                "Doi" TEXT NULL,
                "RawType" TEXT NULL,
                "Category" TEXT NOT NULL,
                "CategorySource" TEXT NOT NULL,
                "CitedByCount" INTEGER NULL,
                "ReferencedWorksCount" INTEGER NULL,
                "Authors" TEXT NULL,
                "Institutions" TEXT NULL,
                "Abstract" TEXT NULL,
                "Keywords" TEXT NULL,
                "Topics" TEXT NULL,
                "Language" TEXT NULL,
                "Publication" TEXT NULL,
                "Volume" TEXT NULL,
                "Issue" TEXT NULL,
                "FirstPage" TEXT NULL,
                "LastPage" TEXT NULL,
                "Link" TEXT NULL,
                "SourceId" TEXT NULL,
                "SourceName" TEXT NULL,
                "SourceType" TEXT NULL,
                "SourceUrl" TEXT NULL,
                "IsOpenAccess" INTEGER NULL,
                "OpenAccessStatus" TEXT NULL,
                "OpenAccessUrl" TEXT NULL,
                "HasFullText" INTEGER NULL,
                "FullTextUrl" TEXT NULL,
                "License" TEXT NULL,
                "Version" TEXT NULL,
                "IsRetracted" INTEGER NULL,
                "ProviderPayload" TEXT NULL,
                "SyncedAt" TEXT NOT NULL,
                CONSTRAINT "FK_AcademicWorks_Researchers_ResearcherId"
                    FOREIGN KEY ("ResearcherId") REFERENCES "Researchers" ("Id")
                    ON DELETE CASCADE
            );
            """;
        createResearcherIndexSql =
            "CREATE INDEX IF NOT EXISTS \"IX_AcademicWorks_ResearcherId\" " +
            "ON \"AcademicWorks\" (\"ResearcherId\");";
        createProviderIndexSql =
            "CREATE INDEX IF NOT EXISTS \"IX_AcademicWorks_ResearcherId_Provider\" " +
            "ON \"AcademicWorks\" (\"ResearcherId\", \"Provider\");";

        await _dbContext.Database.ExecuteSqlRawAsync(createTableSql);
        await _dbContext.Database.ExecuteSqlRawAsync(createResearcherIndexSql);
        await _dbContext.Database.ExecuteSqlRawAsync(createProviderIndexSql);
    }

    private async Task AddSqliteAcademicWorkColumnsAsync()
    {
        await AddSqliteColumnIfMissingAsync("AcademicWorks", "PublicationDate");
        await AddSqliteColumnIfMissingAsync(
            "AcademicWorks",
            "ReferencedWorksCount",
            "INTEGER");
        await AddSqliteColumnIfMissingAsync("AcademicWorks", "Institutions");
        await AddSqliteColumnIfMissingAsync("AcademicWorks", "Abstract");
        await AddSqliteColumnIfMissingAsync("AcademicWorks", "Keywords");
        await AddSqliteColumnIfMissingAsync("AcademicWorks", "Topics");
        await AddSqliteColumnIfMissingAsync("AcademicWorks", "Language");
        await AddSqliteColumnIfMissingAsync("AcademicWorks", "Volume");
        await AddSqliteColumnIfMissingAsync("AcademicWorks", "Issue");
        await AddSqliteColumnIfMissingAsync("AcademicWorks", "FirstPage");
        await AddSqliteColumnIfMissingAsync("AcademicWorks", "LastPage");
        await AddSqliteColumnIfMissingAsync("AcademicWorks", "IsOpenAccess", "INTEGER");
        await AddSqliteColumnIfMissingAsync("AcademicWorks", "OpenAccessStatus");
        await AddSqliteColumnIfMissingAsync("AcademicWorks", "OpenAccessUrl");
        await AddSqliteColumnIfMissingAsync("AcademicWorks", "HasFullText", "INTEGER");
        await AddSqliteColumnIfMissingAsync("AcademicWorks", "FullTextUrl");
        await AddSqliteColumnIfMissingAsync("AcademicWorks", "License");
        await AddSqliteColumnIfMissingAsync("AcademicWorks", "Version");
        await AddSqliteColumnIfMissingAsync("AcademicWorks", "IsRetracted", "INTEGER");
        await AddSqliteColumnIfMissingAsync("AcademicWorks", "ProviderPayload");
    }

    private async Task CreateSqlServerAcademicWorksTableAsync()
    {
        string? createTableSql = null;
        string? createResearcherIndexSql = null;
        string? createProviderIndexSql = null;

        createTableSql =
            """
            IF OBJECT_ID(N'[AcademicWorks]', N'U') IS NULL
            BEGIN
                CREATE TABLE [AcademicWorks] (
                    [Id] int NOT NULL IDENTITY,
                    [ResearcherId] int NOT NULL,
                    [Provider] nvarchar(50) NOT NULL,
                    [ProviderWorkId] nvarchar(500) NULL,
                    [Title] nvarchar(2000) NULL,
                    [PublicationYear] int NULL,
                    [PublicationDate] datetime2 NULL,
                    [Doi] nvarchar(500) NULL,
                    [RawType] nvarchar(100) NULL,
                    [Category] nvarchar(50) NOT NULL,
                    [CategorySource] nvarchar(50) NOT NULL,
                    [CitedByCount] int NULL,
                    [ReferencedWorksCount] int NULL,
                    [Authors] nvarchar(4000) NULL,
                    [Institutions] nvarchar(4000) NULL,
                    [Abstract] nvarchar(max) NULL,
                    [Keywords] nvarchar(4000) NULL,
                    [Topics] nvarchar(4000) NULL,
                    [Language] nvarchar(20) NULL,
                    [Publication] nvarchar(2000) NULL,
                    [Volume] nvarchar(100) NULL,
                    [Issue] nvarchar(100) NULL,
                    [FirstPage] nvarchar(100) NULL,
                    [LastPage] nvarchar(100) NULL,
                    [Link] nvarchar(2000) NULL,
                    [SourceId] nvarchar(500) NULL,
                    [SourceName] nvarchar(2000) NULL,
                    [SourceType] nvarchar(100) NULL,
                    [SourceUrl] nvarchar(2000) NULL,
                    [IsOpenAccess] bit NULL,
                    [OpenAccessStatus] nvarchar(50) NULL,
                    [OpenAccessUrl] nvarchar(2000) NULL,
                    [HasFullText] bit NULL,
                    [FullTextUrl] nvarchar(2000) NULL,
                    [License] nvarchar(100) NULL,
                    [Version] nvarchar(100) NULL,
                    [IsRetracted] bit NULL,
                    [ProviderPayload] nvarchar(max) NULL,
                    [SyncedAt] datetime2 NOT NULL,
                    CONSTRAINT [PK_AcademicWorks] PRIMARY KEY ([Id]),
                    CONSTRAINT [FK_AcademicWorks_Researchers_ResearcherId]
                        FOREIGN KEY ([ResearcherId]) REFERENCES [Researchers] ([Id])
                        ON DELETE CASCADE
                );
            END;
            """;
        createResearcherIndexSql =
            "IF NOT EXISTS (SELECT 1 FROM sys.indexes " +
            "WHERE name = N'IX_AcademicWorks_ResearcherId' " +
            "AND object_id = OBJECT_ID(N'[AcademicWorks]')) " +
            "CREATE INDEX [IX_AcademicWorks_ResearcherId] " +
            "ON [AcademicWorks] ([ResearcherId]);";
        createProviderIndexSql =
            "IF NOT EXISTS (SELECT 1 FROM sys.indexes " +
            "WHERE name = N'IX_AcademicWorks_ResearcherId_Provider' " +
            "AND object_id = OBJECT_ID(N'[AcademicWorks]')) " +
            "CREATE INDEX [IX_AcademicWorks_ResearcherId_Provider] " +
            "ON [AcademicWorks] ([ResearcherId], [Provider]);";

        await _dbContext.Database.ExecuteSqlRawAsync(createTableSql);
        await _dbContext.Database.ExecuteSqlRawAsync(createResearcherIndexSql);
        await _dbContext.Database.ExecuteSqlRawAsync(createProviderIndexSql);
    }

    private async Task AddSqlServerAcademicWorkColumnsAsync()
    {
        await AddSqlServerColumnIfMissingAsync("AcademicWorks", "PublicationDate");
        await AddSqlServerIntegerColumnIfMissingAsync(
            "AcademicWorks",
            "ReferencedWorksCount");
        await AddSqlServerTextColumnIfMissingAsync("AcademicWorks", "Institutions", 4000);
        await AddSqlServerLongTextColumnIfMissingAsync("AcademicWorks", "Abstract");
        await AddSqlServerTextColumnIfMissingAsync("AcademicWorks", "Keywords", 4000);
        await AddSqlServerTextColumnIfMissingAsync("AcademicWorks", "Topics", 4000);
        await AddSqlServerTextColumnIfMissingAsync("AcademicWorks", "Language", 20);
        await AddSqlServerTextColumnIfMissingAsync("AcademicWorks", "Volume", 100);
        await AddSqlServerTextColumnIfMissingAsync("AcademicWorks", "Issue", 100);
        await AddSqlServerTextColumnIfMissingAsync("AcademicWorks", "FirstPage", 100);
        await AddSqlServerTextColumnIfMissingAsync("AcademicWorks", "LastPage", 100);
        await AddSqlServerBooleanColumnIfMissingAsync("AcademicWorks", "IsOpenAccess");
        await AddSqlServerTextColumnIfMissingAsync("AcademicWorks", "OpenAccessStatus", 50);
        await AddSqlServerTextColumnIfMissingAsync("AcademicWorks", "OpenAccessUrl", 2000);
        await AddSqlServerBooleanColumnIfMissingAsync("AcademicWorks", "HasFullText");
        await AddSqlServerTextColumnIfMissingAsync("AcademicWorks", "FullTextUrl", 2000);
        await AddSqlServerTextColumnIfMissingAsync("AcademicWorks", "License", 100);
        await AddSqlServerTextColumnIfMissingAsync("AcademicWorks", "Version", 100);
        await AddSqlServerBooleanColumnIfMissingAsync("AcademicWorks", "IsRetracted");
        await AddSqlServerLongTextColumnIfMissingAsync("AcademicWorks", "ProviderPayload");
    }

    private async Task CreateSqlitePublicationSummariesTableAsync()
    {
        string? createTableSql = null;
        string? createResearcherIndexSql = null;
        string? createFingerprintIndexSql = null;

        createTableSql =
            """
            CREATE TABLE IF NOT EXISTS "PublicationSummaries" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_PublicationSummaries"
                    PRIMARY KEY AUTOINCREMENT,
                "ResearcherId" INTEGER NOT NULL,
                "Fingerprint" TEXT NOT NULL,
                "Title" TEXT NOT NULL,
                "PublicationYear" INTEGER NULL,
                "PublicationDate" TEXT NULL,
                "Doi" TEXT NULL,
                "Category" TEXT NOT NULL,
                "Authors" TEXT NULL,
                "Abstract" TEXT NULL,
                "Keywords" TEXT NULL,
                "Topics" TEXT NULL,
                "Language" TEXT NULL,
                "Publication" TEXT NULL,
                "Volume" TEXT NULL,
                "Issue" TEXT NULL,
                "FirstPage" TEXT NULL,
                "LastPage" TEXT NULL,
                "CitedByCount" INTEGER NULL,
                "IsOpenAccess" INTEGER NULL,
                "IsRetracted" INTEGER NULL,
                "PublicationUrl" TEXT NULL,
                "PdfUrl" TEXT NULL,
                "Sources" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_PublicationSummaries_Researchers_ResearcherId"
                    FOREIGN KEY ("ResearcherId") REFERENCES "Researchers" ("Id")
                    ON DELETE CASCADE
            );
            """;
        createResearcherIndexSql =
            "CREATE INDEX IF NOT EXISTS " +
            "\"IX_PublicationSummaries_ResearcherId\" " +
            "ON \"PublicationSummaries\" (\"ResearcherId\");";
        createFingerprintIndexSql =
            "CREATE UNIQUE INDEX IF NOT EXISTS " +
            "\"IX_PublicationSummaries_ResearcherId_Fingerprint\" " +
            "ON \"PublicationSummaries\" (\"ResearcherId\", \"Fingerprint\");";

        await _dbContext.Database.ExecuteSqlRawAsync(createTableSql);
        await _dbContext.Database.ExecuteSqlRawAsync(createResearcherIndexSql);
        await _dbContext.Database.ExecuteSqlRawAsync(createFingerprintIndexSql);
    }

    private async Task CreateSqlServerPublicationSummariesTableAsync()
    {
        string? createTableSql = null;
        string? createResearcherIndexSql = null;
        string? createFingerprintIndexSql = null;

        createTableSql =
            """
            IF OBJECT_ID(N'[PublicationSummaries]', N'U') IS NULL
            BEGIN
                CREATE TABLE [PublicationSummaries] (
                    [Id] int NOT NULL IDENTITY,
                    [ResearcherId] int NOT NULL,
                    [Fingerprint] nvarchar(64) NOT NULL,
                    [Title] nvarchar(2000) NOT NULL,
                    [PublicationYear] int NULL,
                    [PublicationDate] datetime2 NULL,
                    [Doi] nvarchar(500) NULL,
                    [Category] nvarchar(50) NOT NULL,
                    [Authors] nvarchar(4000) NULL,
                    [Abstract] nvarchar(max) NULL,
                    [Keywords] nvarchar(4000) NULL,
                    [Topics] nvarchar(4000) NULL,
                    [Language] nvarchar(20) NULL,
                    [Publication] nvarchar(2000) NULL,
                    [Volume] nvarchar(100) NULL,
                    [Issue] nvarchar(100) NULL,
                    [FirstPage] nvarchar(100) NULL,
                    [LastPage] nvarchar(100) NULL,
                    [CitedByCount] int NULL,
                    [IsOpenAccess] bit NULL,
                    [IsRetracted] bit NULL,
                    [PublicationUrl] nvarchar(2000) NULL,
                    [PdfUrl] nvarchar(2000) NULL,
                    [Sources] nvarchar(200) NOT NULL,
                    [UpdatedAt] datetime2 NOT NULL,
                    CONSTRAINT [PK_PublicationSummaries] PRIMARY KEY ([Id]),
                    CONSTRAINT [FK_PublicationSummaries_Researchers_ResearcherId]
                        FOREIGN KEY ([ResearcherId]) REFERENCES [Researchers] ([Id])
                        ON DELETE CASCADE
                );
            END;
            """;
        createResearcherIndexSql =
            "IF NOT EXISTS (SELECT 1 FROM sys.indexes " +
            "WHERE name = N'IX_PublicationSummaries_ResearcherId' " +
            "AND object_id = OBJECT_ID(N'[PublicationSummaries]')) " +
            "CREATE INDEX [IX_PublicationSummaries_ResearcherId] " +
            "ON [PublicationSummaries] ([ResearcherId]);";
        createFingerprintIndexSql =
            "IF NOT EXISTS (SELECT 1 FROM sys.indexes " +
            "WHERE name = N'IX_PublicationSummaries_ResearcherId_Fingerprint' " +
            "AND object_id = OBJECT_ID(N'[PublicationSummaries]')) " +
            "CREATE UNIQUE INDEX [IX_PublicationSummaries_ResearcherId_Fingerprint] " +
            "ON [PublicationSummaries] ([ResearcherId], [Fingerprint]);";

        await _dbContext.Database.ExecuteSqlRawAsync(createTableSql);
        await _dbContext.Database.ExecuteSqlRawAsync(createResearcherIndexSql);
        await _dbContext.Database.ExecuteSqlRawAsync(createFingerprintIndexSql);
    }

    private async Task CreateSqlitePublicationDisplayApprovalsTableAsync()
    {
        string createTableSql =
            """
            CREATE TABLE IF NOT EXISTS "PublicationDisplayApprovals" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_PublicationDisplayApprovals"
                    PRIMARY KEY AUTOINCREMENT,
                "ResearcherId" INTEGER NOT NULL,
                "PublicationSummaryId" INTEGER NOT NULL,
                "ApprovedAt" TEXT NOT NULL,
                CONSTRAINT "FK_PublicationDisplayApprovals_Researchers_ResearcherId"
                    FOREIGN KEY ("ResearcherId") REFERENCES "Researchers" ("Id"),
                CONSTRAINT "FK_PublicationDisplayApprovals_PublicationSummaries_PublicationSummaryId"
                    FOREIGN KEY ("PublicationSummaryId") REFERENCES "PublicationSummaries" ("Id")
                    ON DELETE CASCADE
            );
            """;
        string createResearcherIndexSql =
            "CREATE INDEX IF NOT EXISTS " +
            "\"IX_PublicationDisplayApprovals_ResearcherId\" " +
            "ON \"PublicationDisplayApprovals\" (\"ResearcherId\");";
        string createPublicationIndexSql =
            "CREATE UNIQUE INDEX IF NOT EXISTS " +
            "\"IX_PublicationDisplayApprovals_PublicationSummaryId\" " +
            "ON \"PublicationDisplayApprovals\" (\"PublicationSummaryId\");";

        await _dbContext.Database.ExecuteSqlRawAsync(createTableSql);
        await _dbContext.Database.ExecuteSqlRawAsync(createResearcherIndexSql);
        await _dbContext.Database.ExecuteSqlRawAsync(createPublicationIndexSql);
    }

    private async Task CreateSqlServerPublicationDisplayApprovalsTableAsync()
    {
        string createTableSql =
            """
            IF OBJECT_ID(N'[PublicationDisplayApprovals]', N'U') IS NULL
            BEGIN
                CREATE TABLE [PublicationDisplayApprovals] (
                    [Id] int NOT NULL IDENTITY,
                    [ResearcherId] int NOT NULL,
                    [PublicationSummaryId] int NOT NULL,
                    [ApprovedAt] datetime2 NOT NULL,
                    CONSTRAINT [PK_PublicationDisplayApprovals] PRIMARY KEY ([Id]),
                    CONSTRAINT [FK_PublicationDisplayApprovals_Researchers_ResearcherId]
                        FOREIGN KEY ([ResearcherId]) REFERENCES [Researchers] ([Id]),
                    CONSTRAINT [FK_PublicationDisplayApprovals_PublicationSummaries_PublicationSummaryId]
                        FOREIGN KEY ([PublicationSummaryId]) REFERENCES [PublicationSummaries] ([Id])
                        ON DELETE CASCADE
                );
                CREATE INDEX [IX_PublicationDisplayApprovals_ResearcherId]
                    ON [PublicationDisplayApprovals] ([ResearcherId]);
                CREATE UNIQUE INDEX [IX_PublicationDisplayApprovals_PublicationSummaryId]
                    ON [PublicationDisplayApprovals] ([PublicationSummaryId]);
            END;
            """;

        await _dbContext.Database.ExecuteSqlRawAsync(createTableSql);
    }

    private async Task RemoveObsoleteSqliteDataAsync()
    {
        await _dbContext.Database.ExecuteSqlRawAsync(
            "DELETE FROM \"AcademicWorks\" WHERE \"Provider\" = 'OpenAlex';");
        await _dbContext.Database.ExecuteSqlRawAsync(
            "DROP TABLE IF EXISTS \"OpenAlexWorks\";");
        await _dbContext.Database.ExecuteSqlRawAsync(
            "DROP TABLE IF EXISTS \"OpenAlexProfiles\";");
        await _dbContext.Database.ExecuteSqlRawAsync(
            "DROP TABLE IF EXISTS \"ResearcherMetrics\";");
    }

    private async Task RemoveObsoleteSqlServerDataAsync()
    {
        await _dbContext.Database.ExecuteSqlRawAsync(
            "DELETE FROM [AcademicWorks] WHERE [Provider] = N'OpenAlex';");
        await _dbContext.Database.ExecuteSqlRawAsync(
            "IF OBJECT_ID(N'[OpenAlexWorks]', N'U') IS NOT NULL " +
            "DROP TABLE [OpenAlexWorks];");
        await _dbContext.Database.ExecuteSqlRawAsync(
            "IF OBJECT_ID(N'[OpenAlexProfiles]', N'U') IS NOT NULL " +
            "DROP TABLE [OpenAlexProfiles];");
        await _dbContext.Database.ExecuteSqlRawAsync(
            "IF OBJECT_ID(N'[ResearcherMetrics]', N'U') IS NOT NULL " +
            "DROP TABLE [ResearcherMetrics];");
    }

    private async Task AddSqliteColumnIfMissingAsync(
        string tableName,
        string columnName,
        string columnType = "TEXT")
    {
        DbConnection? connection = null;
        DbCommand? command = null;
        object? result = null;
        bool shouldCloseConnection = false;
        int columnCount = 0;

        connection = _dbContext.Database.GetDbConnection();
        shouldCloseConnection = connection.State != ConnectionState.Open;

        if (shouldCloseConnection)
        {
            await connection.OpenAsync();
        }

        try
        {
            command = connection.CreateCommand();
            command.CommandText =
                $"SELECT COUNT(*) FROM pragma_table_info('{tableName}') " +
                $"WHERE name = '{columnName}';";
            result = await command.ExecuteScalarAsync();
            columnCount = Convert.ToInt32(result);

            if (columnCount > 0)
            {
                return;
            }

            command.CommandText =
                $"ALTER TABLE \"{tableName}\" " +
                $"ADD COLUMN \"{columnName}\" {columnType} NULL;";
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            command?.Dispose();

            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task AddSqlServerColumnIfMissingAsync(
        string tableName,
        string columnName)
    {
        string? sql = null;

        sql =
            $"IF COL_LENGTH('{tableName}', '{columnName}') IS NULL " +
            $"ALTER TABLE [{tableName}] ADD [{columnName}] datetime2 NULL;";

        await _dbContext.Database.ExecuteSqlRawAsync(sql);
    }

    private async Task AddSqlServerIntegerColumnIfMissingAsync(
        string tableName,
        string columnName)
    {
        string? sql = null;

        sql =
            $"IF COL_LENGTH('{tableName}', '{columnName}') IS NULL " +
            $"ALTER TABLE [{tableName}] ADD [{columnName}] int NULL;";

        await _dbContext.Database.ExecuteSqlRawAsync(sql);
    }

    private async Task AddSqlServerBooleanColumnIfMissingAsync(
        string tableName,
        string columnName)
    {
        string? sql = null;

        sql =
            $"IF COL_LENGTH('{tableName}', '{columnName}') IS NULL " +
            $"ALTER TABLE [{tableName}] ADD [{columnName}] bit NULL;";

        await _dbContext.Database.ExecuteSqlRawAsync(sql);
    }

    private async Task AddSqlServerTextColumnIfMissingAsync(
        string tableName,
        string columnName,
        int maximumLength)
    {
        string? sql = null;

        sql =
            $"IF COL_LENGTH('{tableName}', '{columnName}') IS NULL " +
            $"ALTER TABLE [{tableName}] " +
            $"ADD [{columnName}] nvarchar({maximumLength}) NULL;";

        await _dbContext.Database.ExecuteSqlRawAsync(sql);
    }

    private async Task AddSqlServerLongTextColumnIfMissingAsync(
        string tableName,
        string columnName)
    {
        string? sql = null;

        sql =
            $"IF COL_LENGTH('{tableName}', '{columnName}') IS NULL " +
            $"ALTER TABLE [{tableName}] ADD [{columnName}] nvarchar(max) NULL;";

        await _dbContext.Database.ExecuteSqlRawAsync(sql);
    }

}
