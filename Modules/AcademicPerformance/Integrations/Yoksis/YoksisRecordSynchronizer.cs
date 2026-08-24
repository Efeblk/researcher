using System.Text.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using Microsoft.EntityFrameworkCore;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis;

public sealed class YoksisRecordSynchronizer
{
    private static readonly string[] PreferredIdentifierFields =
    [
        "YAYIN_ID",
        "PATENT_ID",
        "PROJE_ID",
        "ARASTIRMACI_ID",
        "GOREV_ID",
        "KAYIT_ID"
    ];

    private readonly AcademicDbContext _dbContext;

    public YoksisRecordSynchronizer(AcademicDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<int> SyncAsync(
        int researcherId,
        YoksisCollectResponse response)
    {
        List<YoksisRecord>? existingRecords = null;
        List<YoksisOperationResult>? successfulCategories = null;
        HashSet<string>? successfulOperations = null;

        existingRecords = await _dbContext.YoksisRecords
            .Where(record => record.ResearcherId == researcherId)
            .ToListAsync();
        successfulCategories = response.Categories
            .Where(category =>
                category.IsSuccess &&
                !string.IsNullOrWhiteSpace(category.OperationName))
            .ToList();
        successfulOperations = successfulCategories
            .Select(category => category.OperationName!)
            .ToHashSet(StringComparer.Ordinal);

        _dbContext.YoksisRecords.RemoveRange(existingRecords.Where(record =>
            successfulOperations.Contains(record.OperationName)));

        foreach (YoksisOperationResult category in successfulCategories)
        {
            int recordIndex = 0;

            foreach (Dictionary<string, string?> recordData in category.Records)
            {
                YoksisRecord? record = null;

                record = new YoksisRecord();
                record.ResearcherId = researcherId;
                record.CategoryName = category.CategoryName ??
                    "YÖKSİS kategorisi";
                record.OperationName = category.OperationName!;
                record.RecordIndex = recordIndex;
                record.ExternalRecordId = FindExternalRecordId(recordData);
                record.RecordJson = JsonSerializer.Serialize(recordData);
                record.CollectedAt = response.CollectedAt;
                _dbContext.YoksisRecords.Add(record);
                recordIndex++;
            }
        }

        await _dbContext.SaveChangesAsync();
        return await _dbContext.YoksisRecords.CountAsync(record =>
            record.ResearcherId == researcherId);
    }

    private static string? FindExternalRecordId(
        Dictionary<string, string?> record)
    {
        string? identifier = null;

        foreach (string fieldName in PreferredIdentifierFields)
        {
            identifier = record.GetValueOrDefault(fieldName);

            if (!string.IsNullOrWhiteSpace(identifier))
            {
                return identifier.Trim();
            }
        }

        identifier = record
            .Where(field =>
                field.Key.EndsWith("_ID", StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(field.Value))
            .Select(field => field.Value)
            .FirstOrDefault();
        return identifier?.Trim();
    }
}
