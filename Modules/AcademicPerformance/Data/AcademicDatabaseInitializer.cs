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
            await CreateSqliteAcademicWorkFilesTableAsync();
            await AddSqliteColumnIfMissingAsync("OpenAlexProfiles", "LastUpdatedAt");
            await AddSqliteColumnIfMissingAsync("OpenAlexProfiles", "RawDataJson");
            await AddSqliteColumnIfMissingAsync(
                "OpenAlexProfiles",
                "WorksResponsePagesJson");
            await AddSqliteColumnIfMissingAsync("OpenAlexWorks", "WorkId");
            await AddSqliteColumnIfMissingAsync("OpenAlexWorks", "PublicationDate");
            await AddSqliteColumnIfMissingAsync("OpenAlexWorks", "Language");
            await AddSqliteColumnIfMissingAsync("OpenAlexWorks", "Abstract");
            await AddSqliteColumnIfMissingAsync("OpenAlexWorks", "Authors");
            await AddSqliteColumnIfMissingAsync("OpenAlexWorks", "Institutions");
            await AddSqliteColumnIfMissingAsync("OpenAlexWorks", "Keywords");
            await AddSqliteColumnIfMissingAsync("OpenAlexWorks", "Topics");
            await AddSqliteColumnIfMissingAsync("OpenAlexWorks", "IsOpenAccess", "INTEGER");
            await AddSqliteColumnIfMissingAsync("OpenAlexWorks", "OpenAccessStatus");
            await AddSqliteColumnIfMissingAsync("OpenAlexWorks", "OpenAccessUrl");
            await AddSqliteColumnIfMissingAsync("OpenAlexWorks", "FullTextUrl");
            await AddSqliteColumnIfMissingAsync("OpenAlexWorks", "License");
            await AddSqliteColumnIfMissingAsync("OpenAlexWorks", "Version");
            await AddSqliteColumnIfMissingAsync("OpenAlexWorks", "Volume");
            await AddSqliteColumnIfMissingAsync("OpenAlexWorks", "Issue");
            await AddSqliteColumnIfMissingAsync("OpenAlexWorks", "FirstPage");
            await AddSqliteColumnIfMissingAsync("OpenAlexWorks", "LastPage");
            await AddSqliteColumnIfMissingAsync("OpenAlexWorks", "IsRetracted", "INTEGER");
            await AddSqliteColumnIfMissingAsync("OpenAlexWorks", "HasFullText", "INTEGER");
            await AddSqliteColumnIfMissingAsync(
                "OpenAlexWorks",
                "ReferencedWorksCount",
                "INTEGER");
            await AddSqliteColumnIfMissingAsync("OpenAlexWorks", "RawDataJson");
            await AddSqliteColumnIfMissingAsync("OpenAlexWorks", "SourceId");
            await AddSqliteColumnIfMissingAsync("OpenAlexWorks", "SourceName");
            await AddSqliteColumnIfMissingAsync("OpenAlexWorks", "SourceType");
            await AddSqliteColumnIfMissingAsync("OpenAlexWorks", "SourceUrl");
            await AddSqliteColumnIfMissingAsync(
                "OpenAlexWorks",
                "Category",
                "TEXT NOT NULL DEFAULT 'Unknown'");
            await AddSqliteColumnIfMissingAsync(
                "OpenAlexWorks",
                "CategorySource",
                "TEXT NOT NULL DEFAULT 'Unknown'");
            await AddSqliteColumnIfMissingAsync("GoogleScholarProfiles", "LastUpdatedAt");
            await AddSqliteColumnIfMissingAsync("GoogleScholarProfiles", "RawDataJson");
            await AddSqliteColumnIfMissingAsync(
                "GoogleScholarProfiles",
                "ResponsePagesJson");
            await AddSqliteColumnIfMissingAsync(
                "GoogleScholarProfiles",
                "CitationCount",
                "INTEGER");
            await AddSqliteColumnIfMissingAsync(
                "GoogleScholarProfiles",
                "HIndex",
                "INTEGER");
            await AddSqliteColumnIfMissingAsync(
                "GoogleScholarProfiles",
                "I10Index",
                "INTEGER");
            await AddSqliteColumnIfMissingAsync(
                "GoogleScholarWorks",
                "Category",
                "TEXT NOT NULL DEFAULT 'Unknown'");
            await AddSqliteColumnIfMissingAsync(
                "GoogleScholarWorks",
                "CategorySource",
                "TEXT NOT NULL DEFAULT 'Unknown'");
            await AddSqliteColumnIfMissingAsync("GoogleScholarWorks", "CitedByUrl");
            await AddSqliteColumnIfMissingAsync(
                "GoogleScholarWorks",
                "CitedBySerpApiUrl");
            await AddSqliteColumnIfMissingAsync("GoogleScholarWorks", "CitesId");
            await AddSqliteColumnIfMissingAsync("GoogleScholarWorks", "RawDataJson");
            await AddSqliteColumnIfMissingAsync(
                "GoogleScholarWorks",
                "DetailRawDataJson");
            await AddSqliteAcademicWorkColumnsAsync();
            await AddSqliteColumnIfMissingAsync("WebOfScienceProfiles", "LastUpdatedAt");
            return;
        }

        if (provider.Equals(
            DatabaseConfiguration.SqlServerProvider,
            StringComparison.OrdinalIgnoreCase))
        {
            await CreateSqlServerAcademicWorksTableAsync();
            await CreateSqlServerAcademicWorkFilesTableAsync();
            await AddSqlServerColumnIfMissingAsync("OpenAlexProfiles", "LastUpdatedAt");
            await AddSqlServerLongTextColumnIfMissingAsync(
                "OpenAlexProfiles",
                "RawDataJson");
            await AddSqlServerLongTextColumnIfMissingAsync(
                "OpenAlexProfiles",
                "WorksResponsePagesJson");
            await AddSqlServerTextColumnIfMissingAsync("OpenAlexWorks", "WorkId", 100);
            await AddSqlServerColumnIfMissingAsync("OpenAlexWorks", "PublicationDate");
            await AddSqlServerTextColumnIfMissingAsync("OpenAlexWorks", "Language", 20);
            await AddSqlServerLongTextColumnIfMissingAsync("OpenAlexWorks", "Abstract");
            await AddSqlServerTextColumnIfMissingAsync("OpenAlexWorks", "Authors", 4000);
            await AddSqlServerTextColumnIfMissingAsync("OpenAlexWorks", "Institutions", 4000);
            await AddSqlServerTextColumnIfMissingAsync("OpenAlexWorks", "Keywords", 4000);
            await AddSqlServerTextColumnIfMissingAsync("OpenAlexWorks", "Topics", 4000);
            await AddSqlServerBooleanColumnIfMissingAsync("OpenAlexWorks", "IsOpenAccess");
            await AddSqlServerTextColumnIfMissingAsync(
                "OpenAlexWorks",
                "OpenAccessStatus",
                50);
            await AddSqlServerTextColumnIfMissingAsync("OpenAlexWorks", "OpenAccessUrl", 2000);
            await AddSqlServerTextColumnIfMissingAsync("OpenAlexWorks", "FullTextUrl", 2000);
            await AddSqlServerTextColumnIfMissingAsync("OpenAlexWorks", "License", 100);
            await AddSqlServerTextColumnIfMissingAsync("OpenAlexWorks", "Version", 100);
            await AddSqlServerTextColumnIfMissingAsync("OpenAlexWorks", "Volume", 100);
            await AddSqlServerTextColumnIfMissingAsync("OpenAlexWorks", "Issue", 100);
            await AddSqlServerTextColumnIfMissingAsync("OpenAlexWorks", "FirstPage", 100);
            await AddSqlServerTextColumnIfMissingAsync("OpenAlexWorks", "LastPage", 100);
            await AddSqlServerBooleanColumnIfMissingAsync("OpenAlexWorks", "IsRetracted");
            await AddSqlServerBooleanColumnIfMissingAsync("OpenAlexWorks", "HasFullText");
            await AddSqlServerIntegerColumnIfMissingAsync(
                "OpenAlexWorks",
                "ReferencedWorksCount");
            await AddSqlServerLongTextColumnIfMissingAsync("OpenAlexWorks", "RawDataJson");
            await AddSqlServerTextColumnIfMissingAsync("OpenAlexWorks", "SourceId", 100);
            await AddSqlServerTextColumnIfMissingAsync("OpenAlexWorks", "SourceName", 2000);
            await AddSqlServerTextColumnIfMissingAsync("OpenAlexWorks", "SourceType", 100);
            await AddSqlServerTextColumnIfMissingAsync("OpenAlexWorks", "SourceUrl", 2000);
            await AddSqlServerRequiredTextColumnIfMissingAsync(
                "OpenAlexWorks",
                "Category",
                50,
                "Unknown");
            await AddSqlServerRequiredTextColumnIfMissingAsync(
                "OpenAlexWorks",
                "CategorySource",
                50,
                "Unknown");
            await AddSqlServerColumnIfMissingAsync("GoogleScholarProfiles", "LastUpdatedAt");
            await AddSqlServerLongTextColumnIfMissingAsync(
                "GoogleScholarProfiles",
                "RawDataJson");
            await AddSqlServerLongTextColumnIfMissingAsync(
                "GoogleScholarProfiles",
                "ResponsePagesJson");
            await AddSqlServerIntegerColumnIfMissingAsync(
                "GoogleScholarProfiles",
                "CitationCount");
            await AddSqlServerIntegerColumnIfMissingAsync("GoogleScholarProfiles", "HIndex");
            await AddSqlServerIntegerColumnIfMissingAsync("GoogleScholarProfiles", "I10Index");
            await AddSqlServerRequiredTextColumnIfMissingAsync(
                "GoogleScholarWorks",
                "Category",
                50,
                "Unknown");
            await AddSqlServerRequiredTextColumnIfMissingAsync(
                "GoogleScholarWorks",
                "CategorySource",
                50,
                "Unknown");
            await AddSqlServerTextColumnIfMissingAsync(
                "GoogleScholarWorks",
                "CitedByUrl",
                2000);
            await AddSqlServerTextColumnIfMissingAsync(
                "GoogleScholarWorks",
                "CitedBySerpApiUrl",
                2000);
            await AddSqlServerTextColumnIfMissingAsync(
                "GoogleScholarWorks",
                "CitesId",
                2000);
            await AddSqlServerLongTextColumnIfMissingAsync(
                "GoogleScholarWorks",
                "RawDataJson");
            await AddSqlServerLongTextColumnIfMissingAsync(
                "GoogleScholarWorks",
                "DetailRawDataJson");
            await AddSqlServerAcademicWorkColumnsAsync();
            await AddSqlServerColumnIfMissingAsync("WebOfScienceProfiles", "LastUpdatedAt");
        }
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
                "CitedByUrl" TEXT NULL,
                "CitedBySerpApiUrl" TEXT NULL,
                "CitesId" TEXT NULL,
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
                "ProviderDetailPayload" TEXT NULL,
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
        await AddSqliteColumnIfMissingAsync("AcademicWorks", "CitedByUrl");
        await AddSqliteColumnIfMissingAsync("AcademicWorks", "CitedBySerpApiUrl");
        await AddSqliteColumnIfMissingAsync("AcademicWorks", "CitesId");
        await AddSqliteColumnIfMissingAsync("AcademicWorks", "IsOpenAccess", "INTEGER");
        await AddSqliteColumnIfMissingAsync("AcademicWorks", "OpenAccessStatus");
        await AddSqliteColumnIfMissingAsync("AcademicWorks", "OpenAccessUrl");
        await AddSqliteColumnIfMissingAsync("AcademicWorks", "HasFullText", "INTEGER");
        await AddSqliteColumnIfMissingAsync("AcademicWorks", "FullTextUrl");
        await AddSqliteColumnIfMissingAsync("AcademicWorks", "License");
        await AddSqliteColumnIfMissingAsync("AcademicWorks", "Version");
        await AddSqliteColumnIfMissingAsync("AcademicWorks", "IsRetracted", "INTEGER");
        await AddSqliteColumnIfMissingAsync("AcademicWorks", "ProviderPayload");
        await AddSqliteColumnIfMissingAsync("AcademicWorks", "ProviderDetailPayload");
    }

    private async Task CreateSqliteAcademicWorkFilesTableAsync()
    {
        string? createTableSql = null;
        string? createIndexSql = null;

        createTableSql =
            """
            CREATE TABLE IF NOT EXISTS "AcademicWorkFiles" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_AcademicWorkFiles"
                    PRIMARY KEY AUTOINCREMENT,
                "AcademicWorkId" INTEGER NOT NULL,
                "SourceUrl" TEXT NULL,
                "RelativePath" TEXT NULL,
                "FileName" TEXT NULL,
                "MimeType" TEXT NULL,
                "FileSizeBytes" INTEGER NULL,
                "Sha256" TEXT NULL,
                "DownloadedAt" TEXT NULL,
                "LastAttemptedAt" TEXT NULL,
                "Status" TEXT NOT NULL DEFAULT 'Pending',
                "ErrorMessage" TEXT NULL,
                CONSTRAINT "FK_AcademicWorkFiles_AcademicWorks_AcademicWorkId"
                    FOREIGN KEY ("AcademicWorkId") REFERENCES "AcademicWorks" ("Id")
                    ON DELETE CASCADE
            );
            """;
        createIndexSql =
            "CREATE UNIQUE INDEX IF NOT EXISTS " +
            "\"IX_AcademicWorkFiles_AcademicWorkId\" " +
            "ON \"AcademicWorkFiles\" (\"AcademicWorkId\");";

        await _dbContext.Database.ExecuteSqlRawAsync(createTableSql);
        await _dbContext.Database.ExecuteSqlRawAsync(createIndexSql);
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
                    [CitedByUrl] nvarchar(2000) NULL,
                    [CitedBySerpApiUrl] nvarchar(2000) NULL,
                    [CitesId] nvarchar(2000) NULL,
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
                    [ProviderDetailPayload] nvarchar(max) NULL,
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
        await AddSqlServerTextColumnIfMissingAsync("AcademicWorks", "CitedByUrl", 2000);
        await AddSqlServerTextColumnIfMissingAsync(
            "AcademicWorks",
            "CitedBySerpApiUrl",
            2000);
        await AddSqlServerTextColumnIfMissingAsync("AcademicWorks", "CitesId", 2000);
        await AddSqlServerBooleanColumnIfMissingAsync("AcademicWorks", "IsOpenAccess");
        await AddSqlServerTextColumnIfMissingAsync("AcademicWorks", "OpenAccessStatus", 50);
        await AddSqlServerTextColumnIfMissingAsync("AcademicWorks", "OpenAccessUrl", 2000);
        await AddSqlServerBooleanColumnIfMissingAsync("AcademicWorks", "HasFullText");
        await AddSqlServerTextColumnIfMissingAsync("AcademicWorks", "FullTextUrl", 2000);
        await AddSqlServerTextColumnIfMissingAsync("AcademicWorks", "License", 100);
        await AddSqlServerTextColumnIfMissingAsync("AcademicWorks", "Version", 100);
        await AddSqlServerBooleanColumnIfMissingAsync("AcademicWorks", "IsRetracted");
        await AddSqlServerLongTextColumnIfMissingAsync("AcademicWorks", "ProviderPayload");
        await AddSqlServerLongTextColumnIfMissingAsync(
            "AcademicWorks",
            "ProviderDetailPayload");
    }

    private async Task CreateSqlServerAcademicWorkFilesTableAsync()
    {
        string? createTableSql = null;
        string? createIndexSql = null;

        createTableSql =
            """
            IF OBJECT_ID(N'[AcademicWorkFiles]', N'U') IS NULL
            BEGIN
                CREATE TABLE [AcademicWorkFiles] (
                    [Id] int NOT NULL IDENTITY,
                    [AcademicWorkId] int NOT NULL,
                    [SourceUrl] nvarchar(2000) NULL,
                    [RelativePath] nvarchar(1000) NULL,
                    [FileName] nvarchar(500) NULL,
                    [MimeType] nvarchar(200) NULL,
                    [FileSizeBytes] bigint NULL,
                    [Sha256] nvarchar(64) NULL,
                    [DownloadedAt] datetime2 NULL,
                    [LastAttemptedAt] datetime2 NULL,
                    [Status] nvarchar(50) NOT NULL
                        CONSTRAINT [DF_AcademicWorkFiles_Status] DEFAULT N'Pending',
                    [ErrorMessage] nvarchar(2000) NULL,
                    CONSTRAINT [PK_AcademicWorkFiles] PRIMARY KEY ([Id]),
                    CONSTRAINT [FK_AcademicWorkFiles_AcademicWorks_AcademicWorkId]
                        FOREIGN KEY ([AcademicWorkId]) REFERENCES [AcademicWorks] ([Id])
                        ON DELETE CASCADE
                );
            END;
            """;
        createIndexSql =
            "IF NOT EXISTS (SELECT 1 FROM sys.indexes " +
            "WHERE name = N'IX_AcademicWorkFiles_AcademicWorkId' " +
            "AND object_id = OBJECT_ID(N'[AcademicWorkFiles]')) " +
            "CREATE UNIQUE INDEX [IX_AcademicWorkFiles_AcademicWorkId] " +
            "ON [AcademicWorkFiles] ([AcademicWorkId]);";

        await _dbContext.Database.ExecuteSqlRawAsync(createTableSql);
        await _dbContext.Database.ExecuteSqlRawAsync(createIndexSql);
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

    private async Task AddSqlServerRequiredTextColumnIfMissingAsync(
        string tableName,
        string columnName,
        int maximumLength,
        string defaultValue)
    {
        string? sql = null;
        string? defaultConstraintName = null;

        defaultConstraintName = $"DF_{tableName}_{columnName}";
        sql =
            $"IF COL_LENGTH('{tableName}', '{columnName}') IS NULL " +
            $"ALTER TABLE [{tableName}] ADD [{columnName}] nvarchar({maximumLength}) " +
            $"NOT NULL CONSTRAINT [{defaultConstraintName}] " +
            $"DEFAULT N'{defaultValue}' WITH VALUES;";

        await _dbContext.Database.ExecuteSqlRawAsync(sql);
    }
}
