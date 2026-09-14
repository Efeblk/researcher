using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ResearcherAnalysisService.Products.Data;

/// <summary>
/// Coordinates analysis reads with the collector's canonical write transactions without
/// granting the analysis service permission to mutate collector-owned rows.
/// </summary>
public sealed class AnalysisSourceLock(AnalysisDbContext database)
{
    private const int TimeoutMilliseconds = 15000;

    public Task AcquireWriteGateAsync(CancellationToken cancellationToken = default) =>
        AcquireAsync("write-gate", cancellationToken);

    public Task AcquireResearcherLockAsync(string personelId, CancellationToken cancellationToken = default) =>
        AcquireAsync("researcher:" + Hash(personelId.Trim()), cancellationToken);

    private async Task AcquireAsync(string resource, CancellationToken cancellationToken)
    {
        if (database.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A transaction is required before acquiring a collector source lock.");

        DbConnection connection = database.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using DbCommand command = connection.CreateCommand();
        command.Transaction = database.Database.CurrentTransaction.GetDbTransaction();
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = @resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = @timeout;
            SELECT @result;
            """;
        DbParameter resourceParameter = command.CreateParameter();
        resourceParameter.ParameterName = "@resource";
        resourceParameter.Value = "canonical-work:" + resource;
        command.Parameters.Add(resourceParameter);
        DbParameter timeoutParameter = command.CreateParameter();
        timeoutParameter.ParameterName = "@timeout";
        timeoutParameter.Value = TimeoutMilliseconds;
        command.Parameters.Add(timeoutParameter);
        int result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        if (result < 0)
            throw new InvalidOperationException($"Could not acquire collector source lock (SQL result {result}).");
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
