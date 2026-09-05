using Microsoft.Data.SqlClient;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data;

// A dedicated, non-pooled session owns the lock. Closing it releases the lock,
// including when a worker crashes. It is never returned to a connection pool.
public sealed class SqlApplicationLock : IAsyncDisposable
{
    public SqlConnection Connection { get; }

    private SqlApplicationLock(SqlConnection connection) => Connection = connection;

    public static async Task<SqlApplicationLock?> TryAcquireAsync(
        string connectionString, string resource, int timeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        SqlConnectionStringBuilder settings = new(connectionString) { Pooling = false };
        SqlConnection connection = new(settings.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandTimeout = Math.Max(30, timeoutMilliseconds / 1000 + 5);
            command.CommandText = """
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource = @resource,
                    @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = @timeout;
                SELECT @result;
                """;
            command.Parameters.AddWithValue("@resource", resource);
            command.Parameters.AddWithValue("@timeout", timeoutMilliseconds);
            int result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
            if (result >= 0)
                return new SqlApplicationLock(connection);
            await connection.DisposeAsync();
            return null;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public ValueTask DisposeAsync() => Connection.DisposeAsync();
}
