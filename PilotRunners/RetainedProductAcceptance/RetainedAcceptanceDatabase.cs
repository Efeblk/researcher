using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace ServiceAcceptancePilot;

internal static class RetainedAcceptanceDatabase
{
    internal const string SourceName = "AcademicFinalServiceAcceptance_5bb273911b08453991d59a3963250ddc";
    internal const string TargetPrefix = "AcademicQualifiedServiceAcceptanceV10_";
    private const long ExpectedUsageCalls = 51;
    private const long ExpectedUsageTokens = 386738;
    private const decimal ExpectedUsageCostUsd = 0.945550500m;
    private const string ExpectedDatabaseFingerprint =
        "f3389b38df6ad8f86f7ff51d0c8f6341de0c15560fc542b82c22640cf336d7fa";
    private const string ExpectedUsageFingerprint =
        "329aecc16f87091d971c6b11dcbceedeb768f09d808b7fc8f00b22092c06044f";
    private const string ExpectedFacultyFingerprint =
        "732b103329ac25f1e872360b214947ba4be062a62a4dadf6fecceea73b86f7c8";
    private static readonly string[] ExpectedSourceHashes =
    [
        "c2f7f4ac5265813d06ac7f3776abc9b7dcdfe0c21efc1b943fc4059afab950ab",
        "ef1663dd32671bce737af77d0cfd0777fd8ebd95bb86f5d0ff2b4aa457675fa3"
    ];

    internal static async Task<RetainedDatabaseClone> CreateCloneAsync(
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow;
        string targetName = TargetPrefix + Guid.NewGuid().ToString("N");
        ValidateTargetName(targetName);
        (string masterConnection, string sourceConnection) = Connections(SourceName);
        (_, string targetConnection) = Connections(targetName);

        await using SqlConnection master = new(masterConnection);
        await master.OpenAsync(cancellationToken);
        await RequireDatabaseStateAsync(master, SourceName, "ONLINE", cancellationToken);
        await RequireDatabaseAbsentAsync(master, targetName, cancellationToken);
        IReadOnlyList<RetainedDatabaseFile> sourceFiles = await ReadMasterFilesAsync(
            master, SourceName, cancellationToken);
        RequireSingleDataAndLog(sourceFiles, "source");
        RetainedBaseline baseline = await InspectBaselineAsync(sourceConnection);

        string ownedRoot = CanonicalDirectory(Path.Combine(Path.GetTempPath(),
            "researcher-qualified-service-acceptance-v10"));
        string ownedDirectory = CanonicalOwnedPath(ownedRoot, Path.Combine(ownedRoot, targetName));
        if (Directory.Exists(ownedDirectory) || File.Exists(ownedDirectory))
            throw new InvalidOperationException("The dedicated V10 clone directory already exists.");
        Directory.CreateDirectory(ownedDirectory);
        string backupPath = CanonicalOwnedPath(ownedDirectory,
            Path.Combine(ownedDirectory, $"{targetName}.copy-only.bak"));
        string dataPath = CanonicalOwnedPath(ownedDirectory,
            Path.Combine(ownedDirectory, $"{targetName}.mdf"));
        string logPath = CanonicalOwnedPath(ownedDirectory,
            Path.Combine(ownedDirectory, $"{targetName}_log.ldf"));
        RequireDistinctAbsentPaths(backupPath, dataPath, logPath);

        await ExecuteAsync(master, $"BACKUP DATABASE [{SourceName}] TO DISK = {Literal(backupPath)} " +
            "WITH COPY_ONLY, INIT, CHECKSUM", cancellationToken, 600);
        FileInfo backup = new(backupPath);
        if (!backup.Exists || backup.Length <= 0)
            throw new InvalidOperationException("The checksummed V10 source backup is absent or empty.");
        await ExecuteAsync(master, $"RESTORE VERIFYONLY FROM DISK = {Literal(backupPath)} WITH CHECKSUM",
            cancellationToken, 600);
        IReadOnlyList<RetainedDatabaseFile> backupFiles = await ReadBackupFilesAsync(
            master, backupPath, cancellationToken);
        RequireSingleDataAndLog(backupFiles, "backup");
        RequireMatchingLogicalFiles(sourceFiles, backupFiles);

        await RequireDatabaseStateAsync(master, SourceName, "ONLINE", cancellationToken);
        await RequireDatabaseAbsentAsync(master, targetName, cancellationToken);
        RequireDistinctAbsentPaths(dataPath, logPath);
        RetainedDatabaseFile data = backupFiles.Single(value => value.Type == "D");
        RetainedDatabaseFile log = backupFiles.Single(value => value.Type == "L");
        await ExecuteAsync(master, $"RESTORE DATABASE [{targetName}] FROM DISK = {Literal(backupPath)} WITH " +
            $"MOVE {Literal(data.LogicalName)} TO {Literal(dataPath)}, " +
            $"MOVE {Literal(log.LogicalName)} TO {Literal(logPath)}, CHECKSUM, RECOVERY",
            cancellationToken, 600);

        await RequireDatabaseStateAsync(master, targetName, "ONLINE", cancellationToken,
            requiredAccess: "MULTI_USER");
        IReadOnlyList<RetainedDatabaseFile> restoredFiles = await ReadMasterFilesAsync(
            master, targetName, cancellationToken);
        RequireSingleDataAndLog(restoredFiles, "restored target");
        RequirePhysicalPaths(restoredFiles, dataPath, logPath);
        RetainedBaseline restoredBaseline = await InspectBaselineAsync(targetConnection);
        if (JsonSerializer.Serialize(restoredBaseline) != JsonSerializer.Serialize(baseline))
            throw new InvalidOperationException("The restored database is not an exact V5 baseline clone.");
        await AssertBaselinePreservedAsync(sourceConnection, baseline);

        string backupSha256;
        await using (FileStream stream = File.OpenRead(backupPath))
            backupSha256 = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken))
                .ToLowerInvariant();
        return new(targetName, masterConnection, targetConnection, backupPath, dataPath, logPath)
        {
            SourceName = SourceName,
            OwnedDirectory = ownedDirectory,
            BackupBytes = backup.Length,
            BackupSha256 = backupSha256,
            SourceFiles = sourceFiles,
            RestoredFiles = restoredFiles,
            Baseline = baseline,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = DateTimeOffset.UtcNow
        };
    }

    internal static async Task<RetainedBaseline> InspectBaselineAsync(string databaseConnection)
    {
        SqlConnectionStringBuilder builder = ValidateDatabaseConnection(databaseConnection);
        await using SqlConnection connection = new(builder.ConnectionString);
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandTimeout = 120;
        command.CommandText = """
            SELECT
              (SELECT COUNT_BIG(*) FROM [core].[CanonicalWorks]),
              (SELECT COUNT_BIG(*) FROM [analysis].[ArticleSourceSnapshots]),
              (SELECT COUNT_BIG(*) FROM [analysis].[CanonicalArticleAnalysisRuns]),
              (SELECT COUNT_BIG(*) FROM [analysis].[ArticleSummaryAutomationJobs]),
              (SELECT COUNT_BIG(*) FROM [analysis].[CanonicalArticleReviewRuns]),
              (SELECT COUNT_BIG(*) FROM [faculty].[AssistantRuns]),
              (SELECT COUNT_BIG(*) FROM [faculty].[AssistantContextVersions]),
              (SELECT COUNT_BIG(*) FROM [hr].[EvidenceDossiers]),
              (SELECT COUNT_BIG(*) FROM [hr].[DossierReviewActions]),
              (SELECT COUNT_BIG(*) FROM [analysis].[GeminiUsageAttempts]),
              (SELECT COALESCE(SUM(TotalTokenCount),0) FROM [analysis].[GeminiUsageAttempts]),
              (SELECT COALESCE(SUM(EstimatedUsd),0) FROM [analysis].[GeminiUsageAttempts]),
              (SELECT COUNT_BIG(*) FROM [analysis].[GeminiUsageAttempts] WHERE Outcome='Success'),
              (SELECT COUNT_BIG(*) FROM [analysis].[GeminiUsageAttempts] WHERE Outcome='OutputLimit'),
              (SELECT COUNT_BIG(*) FROM [analysis].[GeminiUsageAttempts]
                WHERE CompletedAt IS NULL OR EstimatedUsd IS NULL OR ReturnedModel IS NULL OR
                  ReturnedModel COLLATE Latin1_General_100_BIN2 <>
                    RequestedModel COLLATE Latin1_General_100_BIN2),
              (SELECT COUNT_BIG(*) FROM [faculty].[AssistantRuns]
                WHERE Mode='OwnPaperMethods' AND Status='Completed' AND AttemptCount=1 AND
                  ErrorCode IS NULL AND ReportJson IS NOT NULL),
              (SELECT COUNT_BIG(*) FROM [faculty].[AssistantRuns]
                WHERE Mode='TeachingHelp' AND Status='Failed' AND AttemptCount=1 AND
                  ErrorCode='ProviderFailure' AND ReportJson IS NULL),
              (SELECT COUNT_BIG(*) FROM [analysis].[CanonicalArticleAnalysisRuns]
                WHERE PolicyVersion='article-summary-v5'),
              (SELECT COUNT_BIG(*) FROM [analysis].[ArticleSummaryAutomationJobs]
                WHERE DesiredPolicyVersion='article-summary-v5' AND
                  ProcessedPolicyVersion='article-summary-v5' AND Status='Succeeded')
            """;
        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new InvalidOperationException("The retained V5 baseline counts are unavailable.");
        long canonicalWorks = reader.GetInt64(0);
        long sourceSnapshots = reader.GetInt64(1);
        long analysisRuns = reader.GetInt64(2);
        long automationJobs = reader.GetInt64(3);
        long reviewRuns = reader.GetInt64(4);
        long facultyRuns = reader.GetInt64(5);
        long facultyContexts = reader.GetInt64(6);
        long dossiers = reader.GetInt64(7);
        long dossierActions = reader.GetInt64(8);
        long usageCalls = reader.GetInt64(9);
        long usageTokens = reader.GetInt64(10);
        decimal usageCost = reader.GetDecimal(11);
        long successCalls = reader.GetInt64(12);
        long outputLimitCalls = reader.GetInt64(13);
        long unknownCalls = reader.GetInt64(14);
        long completedMethodsRuns = reader.GetInt64(15);
        long failedTeachingRuns = reader.GetInt64(16);
        long summaryPolicyRuns = reader.GetInt64(17);
        long summaryPolicyJobs = reader.GetInt64(18);
        await reader.CloseAsync();

        List<string> sourceHashes = [];
        await using (SqlCommand hashes = connection.CreateCommand())
        {
            hashes.CommandText = "SELECT ExtractedTextHash FROM [analysis].[ArticleSourceSnapshots] " +
                "ORDER BY ExtractedTextHash COLLATE Latin1_General_100_BIN2";
            await using SqlDataReader hashReader = await hashes.ExecuteReaderAsync();
            while (await hashReader.ReadAsync()) sourceHashes.Add(hashReader.GetString(0));
        }
        string usageFingerprint = await TableFingerprintAsync(connection, "analysis",
            "GeminiUsageAttempts");
        string facultyFingerprint = await TableFingerprintAsync(connection, "faculty", "AssistantRuns");
        IReadOnlyList<Guid> usageAttemptIds = await ReadGuidIdsAsync(connection,
            "SELECT AttemptId FROM [analysis].[GeminiUsageAttempts] ORDER BY AttemptId");
        IReadOnlyList<Guid> facultyRunIds = await ReadGuidIdsAsync(connection,
            "SELECT RunId FROM [faculty].[AssistantRuns] ORDER BY RunId");
        string sourceSummaryFingerprint = await SourceSummaryFingerprintAsync(connection);
        string databaseFingerprint = await DatabaseFingerprintAsync(connection);
        RetainedBaseline result = new(canonicalWorks, sourceSnapshots, sourceHashes, analysisRuns,
            automationJobs, reviewRuns, facultyRuns, facultyContexts, dossiers, dossierActions,
            usageCalls, usageTokens, usageCost, successCalls, outputLimitCalls, unknownCalls,
            completedMethodsRuns, failedTeachingRuns, summaryPolicyRuns, summaryPolicyJobs,
            usageAttemptIds, facultyRunIds, usageFingerprint, facultyFingerprint,
            sourceSummaryFingerprint, databaseFingerprint);
        ValidateKnownBaseline(result);
        return result;
    }

    internal static async Task AssertBaselinePreservedAsync(string databaseConnection,
        RetainedBaseline baseline)
    {
        SqlConnectionStringBuilder builder = ValidateDatabaseConnection(databaseConnection);
        await using SqlConnection connection = new(builder.ConnectionString);
        await connection.OpenAsync();
        string usage = await FilteredGuidFingerprintAsync(connection, "analysis",
            "GeminiUsageAttempts", "AttemptId", "AttemptId", baseline.UsageAttemptIds);
        string faculty = await FilteredGuidFingerprintAsync(connection, "faculty",
            "AssistantRuns", "RunId", "Id", baseline.FacultyRunIds);
        string sourceSummary = await SourceSummaryFingerprintAsync(connection);
        if (!string.Equals(usage, baseline.UsageFingerprint, StringComparison.Ordinal) ||
            !string.Equals(faculty, baseline.FacultyFingerprint, StringComparison.Ordinal) ||
            !string.Equals(sourceSummary, baseline.SourceSummaryFingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("The retained V5 database baseline changed during cloning.");
    }

    internal static async Task<bool> SourceIsOnlineAsync(CancellationToken cancellationToken = default)
    {
        (string masterConnection, _) = Connections(SourceName);
        await using SqlConnection master = new(masterConnection);
        await master.OpenAsync(cancellationToken);
        await using SqlCommand command = master.CreateCommand();
        command.CommandText = "SELECT state_desc,user_access_desc FROM sys.databases WHERE name=@name";
        command.Parameters.AddWithValue("@name", SourceName);
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) && reader.GetString(0) == "ONLINE" &&
            reader.GetString(1) == "MULTI_USER";
    }

    internal static void ValidateTargetName(string name)
    {
        string suffix = name.StartsWith(TargetPrefix, StringComparison.Ordinal)
            ? name[TargetPrefix.Length..]
            : string.Empty;
        if (suffix.Length != 32 || suffix.Any(character =>
                !Uri.IsHexDigit(character) || char.IsUpper(character)))
            throw new InvalidOperationException("The retained-product target database name is invalid.");
        if (string.Equals(name, SourceName, StringComparison.Ordinal))
            throw new InvalidOperationException("The retained V5 source database is never a cleanup target.");
    }

    internal static async Task DropTargetAsync(RetainedDatabaseClone clone,
        CancellationToken cancellationToken = default)
    {
        ValidateTargetName(clone.TargetName);
        SqlConnectionStringBuilder masterBuilder = ValidateMasterConnection(clone.MasterConnection);
        string ownedRoot = CanonicalDirectory(Path.Combine(Path.GetTempPath(),
            "researcher-qualified-service-acceptance-v10"));
        string ownedDirectory = CanonicalOwnedPath(ownedRoot, clone.OwnedDirectory);
        string expectedOwnedDirectory = CanonicalOwnedPath(ownedRoot,
            Path.Combine(ownedRoot, clone.TargetName));
        if (!string.Equals(ownedDirectory, expectedOwnedDirectory,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The retained-product cleanup directory is invalid.");
        string backupPath = CanonicalOwnedPath(ownedDirectory, clone.BackupPath);
        string dataPath = CanonicalOwnedPath(ownedDirectory, clone.DataPath);
        string logPath = CanonicalOwnedPath(ownedDirectory, clone.LogPath);
        if (!string.Equals(Path.GetFileName(dataPath), $"{clone.TargetName}.mdf",
                StringComparison.Ordinal) ||
            !string.Equals(Path.GetFileName(logPath), $"{clone.TargetName}_log.ldf",
                StringComparison.Ordinal) ||
            !string.Equals(Path.GetFileName(backupPath), $"{clone.TargetName}.copy-only.bak",
                StringComparison.Ordinal))
            throw new InvalidOperationException("The retained-product cleanup paths are invalid.");
        await using SqlConnection master = new(masterBuilder.ConnectionString);
        await master.OpenAsync(cancellationToken);
        IReadOnlyList<RetainedDatabaseFile> files = await ReadMasterFilesAsync(
            master, clone.TargetName, cancellationToken);
        if (files.Count > 0) RequirePhysicalPaths(files, dataPath, logPath);
        await ExecuteAsync(master, $"IF DB_ID(N'{clone.TargetName}') IS NOT NULL BEGIN " +
            $"ALTER DATABASE [{clone.TargetName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
            $"DROP DATABASE [{clone.TargetName}]; END", cancellationToken, 120);
        foreach (string path in new[] { backupPath, dataPath, logPath })
            if (File.Exists(path)) File.Delete(path);
        if (Directory.Exists(ownedDirectory) && !Directory.EnumerateFileSystemEntries(ownedDirectory).Any())
            Directory.Delete(ownedDirectory);
    }

    private static void ValidateKnownBaseline(RetainedBaseline value)
    {
        if (value.CanonicalWorks != 2 || value.SourceSnapshots != 2 ||
            !value.SourceHashes.SequenceEqual(ExpectedSourceHashes, StringComparer.Ordinal) ||
            value.AnalysisRuns != 2 || value.AutomationJobs != 2 || value.ReviewRuns != 1 ||
            value.FacultyRuns != 2 || value.FacultyContexts != 1 || value.Dossiers != 1 ||
            value.DossierActions != 0 || value.UsageCalls != ExpectedUsageCalls ||
            value.UsageTokens != ExpectedUsageTokens || value.UsageCostUsd != ExpectedUsageCostUsd ||
            value.SuccessCalls != 48 || value.OutputLimitCalls != 3 || value.UnknownCalls != 0 ||
            value.CompletedMethodsRuns != 1 || value.FailedTeachingRuns != 1 ||
            value.SummaryPolicyRuns != 2 || value.SummaryPolicyJobs != 2 ||
            value.UsageAttemptIds.Count != ExpectedUsageCalls ||
            value.UsageAttemptIds.Distinct().Count() != ExpectedUsageCalls ||
            value.FacultyRunIds.Count != 2 || value.FacultyRunIds.Distinct().Count() != 2 ||
            !string.Equals(value.UsageFingerprint, ExpectedUsageFingerprint,
                StringComparison.Ordinal) ||
            !string.Equals(value.FacultyFingerprint, ExpectedFacultyFingerprint,
                StringComparison.Ordinal) ||
            !IsSha256(value.SourceSummaryFingerprint) ||
            !string.Equals(value.DatabaseFingerprint, ExpectedDatabaseFingerprint,
                StringComparison.Ordinal))
            throw new InvalidOperationException("The retained database does not match the exact frozen V5 baseline.");
    }

    private static async Task<IReadOnlyList<Guid>> ReadGuidIdsAsync(SqlConnection connection, string sql)
    {
        List<Guid> result = [];
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(reader.GetGuid(0));
        return result;
    }

    private static async Task<string> FilteredGuidFingerprintAsync(SqlConnection connection,
        string schema, string table, string filterColumn, string orderColumn, IReadOnlyList<Guid> ids)
    {
        if (ids.Count == 0 || ids.Distinct().Count() != ids.Count)
            throw new InvalidOperationException("The retained fingerprint identity set is invalid.");
        await using SqlCommand command = connection.CreateCommand();
        command.CommandTimeout = 120;
        string[] parameters = ids.Select((_, index) => $"@id{index}").ToArray();
        command.CommandText = $"SELECT * FROM {Identifier(schema)}.{Identifier(table)} " +
            $"WHERE {Identifier(filterColumn)} IN ({string.Join(',', parameters)}) " +
            $"ORDER BY {Identifier(orderColumn)} FOR JSON PATH, INCLUDE_NULL_VALUES";
        for (int index = 0; index < ids.Count; index++)
            command.Parameters.AddWithValue(parameters[index], ids[index]);
        StringBuilder value = new();
        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) value.Append(reader.GetString(0));
        return Hash(value.ToString());
    }

    internal static async Task<string> SourceSummaryFingerprintAsync(SqlConnection connection)
    {
        (string Schema, string Table)[] tables =
        [
            ("core", "CanonicalWorks"),
            ("analysis", "ArticleSourceSnapshots"),
            ("analysis", "ArticleSourcePages"),
            ("analysis", "ArticleSourceSpans"),
            ("analysis", "ArticleSummaries"),
            ("analysis", "ArticleSummaryAutomationJobs"),
            ("analysis", "CanonicalArticleAnalysisRuns"),
            ("analysis", "CanonicalArticleClaims"),
            ("analysis", "CanonicalArticleClaimEvidence")
        ];
        StringBuilder canonical = new();
        foreach ((string schema, string table) in tables)
            canonical.Append(schema).Append('.').Append(table).Append(':')
                .Append(await TableFingerprintAsync(connection, schema, table)).Append('\n');
        return Hash(canonical.ToString());
    }

    private static async Task<string> DatabaseFingerprintAsync(SqlConnection connection)
    {
        List<(string Schema, string Table)> tables = [];
        await using (SqlCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT s.name,t.name FROM sys.tables t JOIN sys.schemas s " +
                "ON s.schema_id=t.schema_id WHERE t.is_ms_shipped=0 ORDER BY s.name,t.name";
            await using SqlDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) tables.Add((reader.GetString(0), reader.GetString(1)));
        }
        StringBuilder canonical = new();
        foreach ((string schema, string table) in tables)
        {
            canonical.Append(schema).Append('.').Append(table).Append(':')
                .Append(await TableFingerprintAsync(connection, schema, table)).Append('\n');
        }
        return Hash(canonical.ToString());
    }

    private static async Task<string> TableFingerprintAsync(SqlConnection connection,
        string schema, string table)
    {
        List<string> keys = [];
        await using (SqlCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT c.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
                JOIN sys.indexes i ON i.object_id=t.object_id AND i.is_primary_key=1
                JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
                JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
                WHERE s.name=@schema AND t.name=@table ORDER BY ic.key_ordinal
                """;
            command.Parameters.AddWithValue("@schema", schema);
            command.Parameters.AddWithValue("@table", table);
            await using SqlDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) keys.Add(reader.GetString(0));
        }
        if (keys.Count == 0 && schema == "dbo" && table == "VersionInfo")
            keys.AddRange(["Version", "AppliedOn", "Description"]);
        if (keys.Count == 0)
            throw new InvalidOperationException($"Baseline table {schema}.{table} has no deterministic key.");
        string order = string.Join(',', keys.Select(Identifier));
        await using SqlCommand json = connection.CreateCommand();
        json.CommandTimeout = 120;
        json.CommandText = $"SELECT * FROM {Identifier(schema)}.{Identifier(table)} " +
            $"ORDER BY {order} FOR JSON PATH, INCLUDE_NULL_VALUES";
        StringBuilder value = new();
        await using SqlDataReader jsonReader = await json.ExecuteReaderAsync();
        while (await jsonReader.ReadAsync()) value.Append(jsonReader.GetString(0));
        return Hash(value.ToString());
    }

    private static async Task<IReadOnlyList<RetainedDatabaseFile>> ReadMasterFilesAsync(
        SqlConnection master, string databaseName, CancellationToken cancellationToken)
    {
        if (databaseName != SourceName) ValidateTargetName(databaseName);
        await using SqlCommand command = master.CreateCommand();
        command.CommandText = "SELECT name,type_desc,physical_name,CAST(size AS bigint)*8192 " +
            "FROM sys.master_files WHERE database_id=DB_ID(@name) ORDER BY file_id";
        command.Parameters.AddWithValue("@name", databaseName);
        List<RetainedDatabaseFile> result = [];
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string typeDescription = reader.GetString(1);
            string type = typeDescription switch
            {
                "ROWS" => "D",
                "LOG" => "L",
                _ => typeDescription
            };
            result.Add(new(reader.GetString(0), type, Path.GetFullPath(reader.GetString(2)),
                reader.GetInt64(3)));
        }
        return result;
    }

    private static async Task<IReadOnlyList<RetainedDatabaseFile>> ReadBackupFilesAsync(
        SqlConnection master, string backupPath, CancellationToken cancellationToken)
    {
        await using SqlCommand command = master.CreateCommand();
        command.CommandTimeout = 120;
        command.CommandText = $"RESTORE FILELISTONLY FROM DISK = {Literal(backupPath)}";
        List<RetainedDatabaseFile> result = [];
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        int logical = reader.GetOrdinal("LogicalName");
        int type = reader.GetOrdinal("Type");
        int physical = reader.GetOrdinal("PhysicalName");
        int size = reader.GetOrdinal("Size");
        int isPresent = reader.GetOrdinal("IsPresent");
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.GetBoolean(isPresent))
                throw new InvalidOperationException("The backup contains an unavailable file entry.");
            result.Add(new(reader.GetString(logical), reader.GetString(type), reader.GetString(physical),
                ConvertBackupSize(reader.GetValue(size))));
        }
        return result;
    }

    private static void RequireSingleDataAndLog(IReadOnlyList<RetainedDatabaseFile> files, string label)
    {
        if (files.Count != 2 || files.Count(value => value.Type == "D") != 1 ||
            files.Count(value => value.Type == "L") != 1 ||
            files.Any(value => string.IsNullOrWhiteSpace(value.LogicalName)) ||
            files.Select(value => value.LogicalName).Distinct(StringComparer.Ordinal).Count() != 2)
            throw new InvalidOperationException($"The {label} must contain exactly one data and one log file.");
    }

    internal static long ConvertBackupSize(object value)
    {
        long converted = value switch
        {
            long integer => integer,
            decimal number when decimal.Truncate(number) == number && number <= long.MaxValue =>
                decimal.ToInt64(number),
            _ => throw new InvalidOperationException("The backup file size is not an integral Int64 value.")
        };
        if (converted < 0)
            throw new InvalidOperationException("The backup file size cannot be negative.");
        return converted;
    }

    private static void RequireMatchingLogicalFiles(IReadOnlyList<RetainedDatabaseFile> source,
        IReadOnlyList<RetainedDatabaseFile> backup)
    {
        foreach (string type in new[] { "D", "L" })
            if (!string.Equals(source.Single(value => value.Type == type).LogicalName,
                    backup.Single(value => value.Type == type).LogicalName, StringComparison.Ordinal))
                throw new InvalidOperationException("Backup logical files differ from the frozen V5 source.");
    }

    private static void RequirePhysicalPaths(IReadOnlyList<RetainedDatabaseFile> files,
        string dataPath, string logPath)
    {
        RequireSingleDataAndLog(files, "restored target");
        if (!string.Equals(Path.GetFullPath(files.Single(value => value.Type == "D").PhysicalPath),
                dataPath, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFullPath(files.Single(value => value.Type == "L").PhysicalPath),
                logPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The restored target files are outside the V10-owned paths.");
    }

    private static async Task RequireDatabaseStateAsync(SqlConnection master, string databaseName,
        string requiredState, CancellationToken cancellationToken, string? requiredAccess = null)
    {
        await using SqlCommand command = master.CreateCommand();
        command.CommandText = "SELECT state_desc,user_access_desc FROM sys.databases WHERE name=@name";
        command.Parameters.AddWithValue("@name", databaseName);
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.GetString(0) != requiredState ||
            requiredAccess is not null && reader.GetString(1) != requiredAccess)
            throw new InvalidOperationException($"Database {databaseName} is not in the required safe state.");
    }

    private static async Task RequireDatabaseAbsentAsync(SqlConnection master, string targetName,
        CancellationToken cancellationToken)
    {
        ValidateTargetName(targetName);
        await using SqlCommand command = master.CreateCommand();
        command.CommandText = "SELECT DB_ID(@name)";
        command.Parameters.AddWithValue("@name", targetName);
        if (await command.ExecuteScalarAsync(cancellationToken) is not DBNull)
            throw new InvalidOperationException("The V10 target database already exists.");
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql,
        CancellationToken cancellationToken, int timeout)
    {
        await using SqlCommand command = connection.CreateCommand();
        command.CommandTimeout = timeout;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static (string Master, string Database) Connections(string name)
    {
        if (name != SourceName) ValidateTargetName(name);
        SqlConnectionStringBuilder master = new()
        {
            DataSource = @"(localdb)\MSSQLLocalDB",
            InitialCatalog = "master",
            IntegratedSecurity = true,
            TrustServerCertificate = true,
            ConnectTimeout = 30
        };
        SqlConnectionStringBuilder database = new(master.ConnectionString) { InitialCatalog = name };
        return (master.ConnectionString, database.ConnectionString);
    }

    private static SqlConnectionStringBuilder ValidateDatabaseConnection(string value)
    {
        SqlConnectionStringBuilder builder = new(value);
        if (!string.Equals(builder.DataSource, @"(localdb)\MSSQLLocalDB", StringComparison.OrdinalIgnoreCase) ||
            !builder.IntegratedSecurity || builder.InitialCatalog != SourceName &&
            !IsValidTargetName(builder.InitialCatalog))
            throw new InvalidOperationException("Only the frozen V5 source or a validated V10 target is allowed.");
        return builder;
    }

    private static SqlConnectionStringBuilder ValidateMasterConnection(string value)
    {
        SqlConnectionStringBuilder builder = new(value);
        if (!string.Equals(builder.DataSource, @"(localdb)\MSSQLLocalDB", StringComparison.OrdinalIgnoreCase) ||
            !builder.IntegratedSecurity || !string.Equals(builder.InitialCatalog, "master",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The retained-product master connection is invalid.");
        return builder;
    }

    private static bool IsValidTargetName(string name)
    {
        try { ValidateTargetName(name); return true; }
        catch (InvalidOperationException) { return false; }
    }

    private static void RequireDistinctAbsentPaths(params string[] paths)
    {
        if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length ||
            paths.Any(path => File.Exists(path) || Directory.Exists(path)))
            throw new InvalidOperationException("A V10 backup or destination path already exists.");
    }

    private static string CanonicalDirectory(string value) =>
        Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string CanonicalOwnedPath(string root, string value)
    {
        string canonicalRoot = CanonicalDirectory(root);
        string canonical = Path.GetFullPath(value);
        if (!canonical.StartsWith(canonicalRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The V10 file path is outside its dedicated owned directory.");
        return canonical;
    }

    private static string Identifier(string value) => $"[{value.Replace("]", "]]", StringComparison.Ordinal)}]";
    private static string Literal(string value) => $"N'{value.Replace("'", "''", StringComparison.Ordinal)}'";
    private static bool IsSha256(string value) => value.Length == 64 &&
        value.All(character => Uri.IsHexDigit(character) && !char.IsUpper(character));
    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

internal sealed record RetainedDatabaseFile(string LogicalName, string Type, string PhysicalPath,
    long SizeBytes);

internal sealed record RetainedDatabaseClone(string TargetName, string MasterConnection,
    string DatabaseConnection, string BackupPath, string DataPath, string LogPath)
{
    public string SourceName { get; init; } = string.Empty;
    public string OwnedDirectory { get; init; } = string.Empty;
    public long BackupBytes { get; init; }
    public string BackupSha256 { get; init; } = string.Empty;
    public IReadOnlyList<RetainedDatabaseFile> SourceFiles { get; init; } = [];
    public IReadOnlyList<RetainedDatabaseFile> RestoredFiles { get; init; } = [];
    public RetainedBaseline? Baseline { get; init; }
    public DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset CompletedAtUtc { get; init; }
}

internal sealed record RetainedBaseline(long CanonicalWorks, long SourceSnapshots,
    IReadOnlyList<string> SourceHashes, long AnalysisRuns, long AutomationJobs, long ReviewRuns,
    long FacultyRuns, long FacultyContexts, long Dossiers, long DossierActions, long UsageCalls,
    long UsageTokens, decimal UsageCostUsd, long SuccessCalls, long OutputLimitCalls,
    long UnknownCalls, long CompletedMethodsRuns, long FailedTeachingRuns, long SummaryPolicyRuns,
    long SummaryPolicyJobs, IReadOnlyList<Guid> UsageAttemptIds, IReadOnlyList<Guid> FacultyRunIds,
    string UsageFingerprint, string FacultyFingerprint, string SourceSummaryFingerprint,
    string DatabaseFingerprint);
