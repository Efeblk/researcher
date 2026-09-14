using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;

namespace ServiceAcceptancePilot;

internal sealed record RetainedTeachingBaseline(
    IReadOnlyList<Guid> AttemptIds, IReadOnlyList<long> FacultyRunIds,
    IReadOnlyList<long> ReviewRunIds, IReadOnlyList<long> DossierIds,
    IReadOnlyList<long> ContextIds, IReadOnlyList<long> SourceSnapshotIds,
    decimal UsageCostUsd, long ActionCount, string UsageFingerprint,
    string FacultyFingerprint, string ReviewFingerprint, string DossierFingerprint,
    string ContextFingerprint, string SourceFingerprint);

internal static class RetainedTeachingBaselineAudit
{
    internal const string DatabaseName = "AcademicQualifiedServiceAcceptanceV10_2bbb62932a7b47f684be7ed267856447";
    internal const int ExpectedUsageCount = 78;
    internal const decimal ExpectedUsageCostUsd = 1.615535250m;

    internal static string ConnectionString => new SqlConnectionStringBuilder
    {
        DataSource = @"(localdb)\MSSQLLocalDB", InitialCatalog = DatabaseName,
        IntegratedSecurity = true, TrustServerCertificate = true
    }.ConnectionString;

    internal static async Task<RetainedTeachingBaseline> InspectAsync()
    {
        await using SqlConnection connection = new(ConnectionString);
        await connection.OpenAsync();
        Guid[] attempts = await GuidsAsync(connection,
            "SELECT AttemptId FROM [analysis].[GeminiUsageAttempts] ORDER BY AttemptId");
        long[] faculty = await LongsAsync(connection,
            "SELECT Id FROM [faculty].[AssistantRuns] ORDER BY Id");
        long[] reviews = await LongsAsync(connection,
            "SELECT Id FROM [analysis].[CanonicalArticleReviewRuns] ORDER BY Id");
        long[] dossiers = await LongsAsync(connection,
            "SELECT Id FROM [hr].[EvidenceDossiers] ORDER BY Id");
        long[] contexts = await LongsAsync(connection,
            "SELECT Id FROM [faculty].[AssistantContextVersions] ORDER BY Id");
        long[] sources = await LongsAsync(connection,
            "SELECT Id FROM [analysis].[ArticleSourceSnapshots] ORDER BY Id");
        decimal cost = await ScalarDecimalAsync(connection,
            "SELECT COALESCE(SUM(EstimatedUsd),0) FROM [analysis].[GeminiUsageAttempts]");
        long actions = await ScalarLongAsync(connection,
            "SELECT COUNT_BIG(*) FROM [hr].[DossierReviewActions]");
        RetainedTeachingBaseline baseline = new(attempts, faculty, reviews, dossiers, contexts, sources,
            cost, actions,
            await FingerprintAsync(connection, "[analysis].[GeminiUsageAttempts]", "AttemptId", attempts.Cast<object>().ToArray()),
            await FingerprintAsync(connection, "[faculty].[AssistantRuns]", "Id", faculty.Cast<object>().ToArray()),
            await FingerprintAsync(connection, "[analysis].[CanonicalArticleReviewRuns]", "Id", reviews.Cast<object>().ToArray()),
            await FingerprintAsync(connection, "[hr].[EvidenceDossiers]", "Id", dossiers.Cast<object>().ToArray()),
            await FingerprintAsync(connection, "[faculty].[AssistantContextVersions]", "Id", contexts.Cast<object>().ToArray()),
            await RetainedAcceptanceDatabase.SourceSummaryFingerprintAsync(connection));
        ValidateKnown(baseline);
        return baseline;
    }

    internal static async Task AssertOriginalRowsPreservedAsync(RetainedTeachingBaseline baseline,
        int addedActions = 0)
    {
        await using SqlConnection connection = new(ConnectionString);
        await connection.OpenAsync();
        Require(await FingerprintAsync(connection, "[analysis].[GeminiUsageAttempts]", "AttemptId",
                baseline.AttemptIds.Cast<object>().ToArray()) == baseline.UsageFingerprint,
            "An inherited V10 usage row changed.");
        Require(await FingerprintAsync(connection, "[faculty].[AssistantRuns]", "Id",
                baseline.FacultyRunIds.Cast<object>().ToArray()) == baseline.FacultyFingerprint,
            "An inherited V10 faculty row changed.");
        Require(await FingerprintAsync(connection, "[analysis].[CanonicalArticleReviewRuns]", "Id",
                baseline.ReviewRunIds.Cast<object>().ToArray()) == baseline.ReviewFingerprint,
            "An inherited V10 review row changed.");
        Require(await FingerprintAsync(connection, "[hr].[EvidenceDossiers]", "Id",
                baseline.DossierIds.Cast<object>().ToArray()) == baseline.DossierFingerprint,
            "An inherited V10 dossier row changed.");
        Require(await FingerprintAsync(connection, "[faculty].[AssistantContextVersions]", "Id",
                baseline.ContextIds.Cast<object>().ToArray()) == baseline.ContextFingerprint,
            "The inherited V10 context row changed.");
        Require(await RetainedAcceptanceDatabase.SourceSummaryFingerprintAsync(connection) == baseline.SourceFingerprint,
            "Inherited source/page/span/summary/analysis/claim data changed.");
        Require(await ScalarLongAsync(connection, "SELECT COUNT_BIG(*) FROM [analysis].[CanonicalArticleReviewRuns]") == 2 &&
            await ScalarLongAsync(connection, "SELECT COUNT_BIG(*) FROM [hr].[EvidenceDossiers]") == 2 &&
            await ScalarLongAsync(connection, "SELECT COUNT_BIG(*) FROM [faculty].[AssistantContextVersions]") == 1 &&
            await ScalarLongAsync(connection, "SELECT COUNT_BIG(*) FROM [hr].[DossierReviewActions]") ==
                baseline.ActionCount + addedActions &&
            await ScalarLongAsync(connection, "SELECT COUNT_BIG(*) FROM [analysis].[ArticleSourceSnapshots]") == 2,
            "A Teaching-only diagnostic changed a protected table count.");
    }

    private static void ValidateKnown(RetainedTeachingBaseline value)
    {
        Require(value.AttemptIds.Count == ExpectedUsageCount && value.UsageCostUsd == ExpectedUsageCostUsd &&
            value.FacultyRunIds.Count == 9 && value.ReviewRunIds.Count == 2 && value.DossierIds.Count == 2 &&
            value.ContextIds.Count == 1 && value.SourceSnapshotIds.Count == 2 && value.ActionCount == 0,
            "The owned V10 database does not match the released diagnostic baseline.");
    }

    private static async Task<string> FingerprintAsync(SqlConnection connection, string table, string key,
        IReadOnlyList<object> ids)
    {
        Require(ids.Count > 0, "A protected fingerprint set is empty.");
        await using SqlCommand command = connection.CreateCommand();
        string[] names = ids.Select((_, index) => "@p" + index).ToArray();
        command.CommandText = $"SELECT * FROM {table} WHERE [{key}] IN ({string.Join(',', names)}) ORDER BY [{key}] FOR JSON PATH, INCLUDE_NULL_VALUES";
        for (int index = 0; index < ids.Count; index++) command.Parameters.AddWithValue(names[index], ids[index]);
        StringBuilder chunks = new();
        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) chunks.Append(reader.GetString(0));
        string json = chunks.ToString();
        JsonArray rows = JsonNode.Parse(json)?.AsArray() ??
            throw new InvalidOperationException($"The protected {table} fingerprint JSON was invalid.");
        Require(rows.Count == ids.Count, $"The protected {table} fingerprint omitted rows.");
        if (ids[0] is Guid)
        {
            HashSet<Guid> actual = rows.Select(row => Guid.Parse(row![key]!.GetValue<string>())).ToHashSet();
            Require(actual.SetEquals(ids.Cast<Guid>()), $"The protected {table} fingerprint identity set changed.");
        }
        else
        {
            HashSet<long> actual = rows.Select(row => row![key]!.GetValue<long>()).ToHashSet();
            Require(actual.SetEquals(ids.Select(Convert.ToInt64)), $"The protected {table} fingerprint identity set changed.");
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    private static async Task<Guid[]> GuidsAsync(SqlConnection connection, string sql)
    {
        await using SqlCommand command = new(sql, connection); await using SqlDataReader reader = await command.ExecuteReaderAsync();
        List<Guid> values = []; while (await reader.ReadAsync()) values.Add(reader.GetGuid(0)); return values.ToArray();
    }

    private static async Task<long[]> LongsAsync(SqlConnection connection, string sql)
    {
        await using SqlCommand command = new(sql, connection); await using SqlDataReader reader = await command.ExecuteReaderAsync();
        List<long> values = []; while (await reader.ReadAsync()) values.Add(reader.GetInt64(0)); return values.ToArray();
    }

    private static async Task<long> ScalarLongAsync(SqlConnection connection, string sql)
    {
        await using SqlCommand command = new(sql, connection); return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<decimal> ScalarDecimalAsync(SqlConnection connection, string sql)
    {
        await using SqlCommand command = new(sql, connection); return Convert.ToDecimal(await command.ExecuteScalarAsync());
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
