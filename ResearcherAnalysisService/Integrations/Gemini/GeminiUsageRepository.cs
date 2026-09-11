using Microsoft.Data.SqlClient;
using System.Data;

namespace ResearcherAnalysisService.Integrations.Gemini;

public sealed class GeminiUsageRepository(IConfiguration configuration) : IGeminiUsageRepository
{
    public async Task BeginAsync(Guid attemptId, DateTime startedAt, string requestedModel,
        CancellationToken cancellationToken)
    {
        await using SqlConnection connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using SqlCommand command = connection.CreateCommand();
        command.CommandTimeout = 5;
        command.CommandText = """
            INSERT INTO [analysis].[GeminiUsageAttempts]
                (AttemptId,StartedAt,RequestedModel,Outcome)
            VALUES (@attemptId,@startedAt,@requestedModel,'Pending')
            """;
        command.Parameters.Add("@attemptId", SqlDbType.UniqueIdentifier).Value = attemptId;
        command.Parameters.Add("@startedAt", SqlDbType.DateTime2).Value = startedAt;
        command.Parameters.Add("@requestedModel", SqlDbType.NVarChar, 200).Value = requestedModel;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CompleteAsync(Guid attemptId, DateTime completedAt, GeminiUsageCompletion completion,
        CancellationToken cancellationToken)
    {
        await using SqlConnection connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using SqlCommand command = connection.CreateCommand();
        command.CommandTimeout = 5;
        command.CommandText = """
            UPDATE [analysis].[GeminiUsageAttempts] SET
                CompletedAt=@completedAt,ReturnedModel=@returnedModel,Outcome=@outcome,HttpStatus=@httpStatus,
                PromptTokenCount=@promptTokenCount,CachedTokenCount=@cachedTokenCount,
                CandidateTokenCount=@candidateTokenCount,ThoughtTokenCount=@thoughtTokenCount,
                TotalTokenCount=@totalTokenCount,PricingVersion=@pricingVersion,EstimatedUsd=@estimatedUsd
            WHERE AttemptId=@attemptId AND Outcome='Pending'
            """;
        command.Parameters.Add("@attemptId", SqlDbType.UniqueIdentifier).Value = attemptId;
        command.Parameters.Add("@completedAt", SqlDbType.DateTime2).Value = completedAt;
        AddNullable(command, "@returnedModel", SqlDbType.NVarChar, completion.ReturnedModel, 200);
        command.Parameters.Add("@outcome", SqlDbType.NVarChar, 40).Value = completion.Outcome;
        AddNullable(command, "@httpStatus", SqlDbType.Int, completion.HttpStatus);
        AddNullable(command, "@promptTokenCount", SqlDbType.BigInt, completion.PromptTokenCount);
        AddNullable(command, "@cachedTokenCount", SqlDbType.BigInt, completion.CachedTokenCount);
        AddNullable(command, "@candidateTokenCount", SqlDbType.BigInt, completion.CandidateTokenCount);
        AddNullable(command, "@thoughtTokenCount", SqlDbType.BigInt, completion.ThoughtTokenCount);
        AddNullable(command, "@totalTokenCount", SqlDbType.BigInt, completion.TotalTokenCount);
        AddNullable(command, "@pricingVersion", SqlDbType.NVarChar, completion.PricingVersion, 80);
        AddNullable(command, "@estimatedUsd", SqlDbType.Decimal, completion.EstimatedUsd,
            precision: 19, scale: 9);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("The Gemini usage attempt could not be completed.");
    }

    public async Task<GeminiSpendingStatus> GetSpendingAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        CancellationToken operationToken = timeout.Token;
        try
        {
            await using SqlConnection connection = CreateConnection();
            await connection.OpenAsync(operationToken);
            await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable, operationToken);
            await using SqlCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = 5;
            command.CommandText = """
                SELECT MIN(StartedAt),COUNT_BIG(*),
                    COALESCE(SUM(CASE WHEN EstimatedUsd IS NULL THEN CAST(1 AS bigint) ELSE CAST(0 AS bigint) END),0),
                    CASE WHEN COUNT_BIG(*)=0 THEN CAST(0 AS decimal(19,9))
                         WHEN SUM(CASE WHEN EstimatedUsd IS NULL THEN 1 ELSE 0 END)=0 THEN SUM(EstimatedUsd)
                         ELSE NULL END
                FROM [analysis].[GeminiUsageAttempts];
                SELECT TOP (3) StartedAt,COALESCE(ReturnedModel,RequestedModel),EstimatedUsd
                FROM [analysis].[GeminiUsageAttempts]
                ORDER BY StartedAt DESC,AttemptId DESC;
                """;
            await using SqlDataReader reader = await command.ExecuteReaderAsync(operationToken);
            await reader.ReadAsync(operationToken);
            DateTime? since = reader.IsDBNull(0) ? null : DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc);
            long requestCount = reader.GetInt64(1);
            long unknownCount = reader.GetInt64(2);
            decimal? total = reader.IsDBNull(3) ? null : reader.GetDecimal(3);
            await reader.NextResultAsync(operationToken);
            List<GeminiSpendingItem> last = [];
            while (await reader.ReadAsync(operationToken))
                last.Add(new(DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc), reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetDecimal(2)));
            await reader.CloseAsync();
            await transaction.CommitAsync(operationToken);
            return new()
            {
                Available = true, Since = since, RequestCount = requestCount, UnknownCount = unknownCount,
                EstimatedTotalUsd = unknownCount == 0 ? total : null, Last3 = last
            };
        }
        catch (Exception)
        {
            return new() { Available = false };
        }
    }

    private SqlConnection CreateConnection()
    {
        string? connectionString = configuration.GetConnectionString("UsageDatabase");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Gemini usage storage is not configured.");
        return new SqlConnection(connectionString);
    }

    private static void AddNullable(SqlCommand command, string name, SqlDbType type, object? value,
        int size = 0, byte precision = 0, byte scale = 0)
    {
        SqlParameter parameter = size > 0 ? command.Parameters.Add(name, type, size) :
            command.Parameters.Add(name, type);
        if (precision > 0) parameter.Precision = precision;
        if (scale > 0) parameter.Scale = scale;
        parameter.Value = value ?? DBNull.Value;
    }
}
