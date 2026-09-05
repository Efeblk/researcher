using System.Data;
using System.Globalization;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Bulk.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Bulk.SqlImport;

public sealed class BulkSqlImporter(
    IConfiguration configuration, IOptions<BulkSqlSourceOptions> sourceOptions,
    IOptions<BulkCollectionOptions> bulkOptions, BulkCollectionService collectionService)
{
    public async Task<BulkCollectionStatusResponse> ImportAsync(
        Guid batchId, CancellationToken cancellationToken = default)
    {
        BulkSqlSourceOptions source = sourceOptions.Value;
        if (!source.Enabled || string.IsNullOrWhiteSpace(source.Query))
            throw new InvalidOperationException("Configure and enable BulkSqlSource before importing.");
        string connectionString = configuration.GetConnectionString("BulkSource")
            ?? throw new InvalidOperationException("Configure the read-only BulkSource connection.");
        await using SqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqlCommand command = connection.CreateCommand();
        // Only operator-owned configuration supplies SQL. HTTP callers cannot submit SQL text.
        command.CommandText = source.Query;
        command.CommandTimeout = source.CommandTimeoutSeconds;
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        Dictionary<string, int> columns = Enumerable.Range(0, reader.FieldCount)
            .ToDictionary(reader.GetName, index => index, StringComparer.OrdinalIgnoreCase);
        if (!columns.ContainsKey(source.OrcidColumn) && !columns.ContainsKey(source.GoogleScholarIdColumn) &&
            !columns.ContainsKey(source.WebOfScienceIdColumn))
            throw new InvalidOperationException("The query must return at least one configured provider ID column.");

        List<BulkResearcherInput> rows = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            if (rows.Count >= bulkOptions.Value.MaximumBatchSize)
                throw new ArgumentException("The query exceeds MaximumBatchSize. Import smaller batches.");
            rows.Add(ReadRow(reader, columns, source, rows.Count + 1));
        }
        return await collectionService.SubmitAsync(new() { BatchId = batchId, Researchers = rows }, cancellationToken);
    }

    public static BulkResearcherInput ReadRow(
        IDataRecord row, IReadOnlyDictionary<string, int> columns, BulkSqlSourceOptions source, int rowNumber)
    {
        string? Read(string column) => columns.TryGetValue(column, out int ordinal) && !row.IsDBNull(ordinal)
            ? Convert.ToString(row.GetValue(ordinal), CultureInfo.InvariantCulture)?.Trim() : null;
        return new()
        {
            SourceResearcherId = Read(source.SourceResearcherIdColumn) ?? $"row-{rowNumber}",
            Orcid = Read(source.OrcidColumn),
            GoogleScholarId = Read(source.GoogleScholarIdColumn),
            WebOfScienceId = Read(source.WebOfScienceIdColumn)
        };
    }
}
