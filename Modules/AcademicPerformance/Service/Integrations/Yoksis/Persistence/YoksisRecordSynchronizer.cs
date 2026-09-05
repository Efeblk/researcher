using System.Text.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using Microsoft.EntityFrameworkCore;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis.Persistence;

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
        YoksisCollectResponse response,
        bool isIncremental = false)
    {
        List<YoksisRecord>? existingRecords = await _dbContext.YoksisRecords
            .Where(record => record.ResearcherId == researcherId)
            .ToListAsync();
        List<YoksisOperationResult>? categoriesToSynchronize = response.Categories
            .Where(category =>
                !string.IsNullOrWhiteSpace(category.OperationName) &&
                (category.IsSuccess || category.Records.Count > 0))
            .ToList();
        HashSet<string>? completedOperations = categoriesToSynchronize
            .Where(category => category.IsSuccess && !isIncremental)
            .Select(category => category.OperationName!)
            .ToHashSet(StringComparer.Ordinal);
        HashSet<int>? removedRecordIds = [];

        foreach (YoksisRecord existingRecord in existingRecords)
        {
            if (completedOperations.Contains(existingRecord.OperationName))
            {
                _dbContext.YoksisRecords.Remove(existingRecord);
                removedRecordIds.Add(existingRecord.Id);
            }
        }

        foreach (YoksisOperationResult category in categoriesToSynchronize)
        {
            int recordIndex = 0;

            foreach (Dictionary<string, string?> recordData in category.Records)
            {
                YoksisRecord? existingRecord = null;

                string? externalRecordId = FindExternalRecordId(recordData);
                string? recordJson = JsonSerializer.Serialize(recordData);

                if (!category.IsSuccess || isIncremental)
                {
                    existingRecord = FindExistingRecord(
                        existingRecords,
                        removedRecordIds,
                        category.OperationName!,
                        externalRecordId,
                        recordJson);

                    if (existingRecord is not null)
                    {
                        _dbContext.YoksisRecords.Remove(existingRecord);
                        removedRecordIds.Add(existingRecord.Id);
                    }
                }

                YoksisRecord? record = new YoksisRecord();
                record.ResearcherId = researcherId;
                record.CategoryName = category.CategoryName ??
                    "YÖKSİS kategorisi";
                record.OperationName = category.OperationName!;
                record.RecordIndex = recordIndex;
                record.ExternalRecordId = externalRecordId;
                record.RecordJson = recordJson;
                record.CollectedAt = response.CollectedAt;
                _dbContext.YoksisRecords.Add(record);
                recordIndex++;
            }
        }

        await _dbContext.SaveChangesAsync();
        return await _dbContext.YoksisRecords.CountAsync(record =>
            record.ResearcherId == researcherId);
    }

    private static YoksisRecord? FindExistingRecord(
        List<YoksisRecord> existingRecords,
        HashSet<int> removedRecordIds,
        string operationName,
        string? externalRecordId,
        string recordJson)
    {
        YoksisRecord? matchingRecord = existingRecords.FirstOrDefault(record =>
            !removedRecordIds.Contains(record.Id) &&
            record.OperationName == operationName &&
            !string.IsNullOrWhiteSpace(externalRecordId) &&
            record.ExternalRecordId == externalRecordId);

        if (matchingRecord is not null ||
            !string.IsNullOrWhiteSpace(externalRecordId))
        {
            return matchingRecord;
        }

        return existingRecords.FirstOrDefault(record =>
            !removedRecordIds.Contains(record.Id) &&
            record.OperationName == operationName &&
            record.RecordJson == recordJson);
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
