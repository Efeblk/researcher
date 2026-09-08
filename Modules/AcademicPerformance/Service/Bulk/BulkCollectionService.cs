using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Bulk.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Bulk;

public sealed class BulkCollectionService(
    AcademicDbContext database, BulkResearcherInputNormalizer normalizer,
    IOptions<BulkCollectionOptions> options)
{
    public async Task<BulkCollectionStatusResponse> SubmitAsync(
        BulkCollectionSubmitRequest request, CancellationToken cancellationToken = default)
    {
        if (request.BatchId == Guid.Empty)
            throw new ArgumentException("Supply a stable, non-empty BatchId for safe resubmission.");
        if (request.Researchers is null || request.Researchers.Count == 0 ||
            request.Researchers.Count > options.Value.MaximumBatchSize)
            throw new ArgumentException($"Supply between 1 and {options.Value.MaximumBatchSize} researchers.");
        if (request.Researchers.Any(row => row is null ||
            string.IsNullOrWhiteSpace(row.PersonelId) || row.PersonelId.Length > 200))
            throw new ArgumentException("Rows require PersonelID of at most 200 characters.");

        string hash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request.Researchers))));
        await using SqlApplicationLock? gate = await SqlApplicationLock.TryAcquireAsync(
            database.Database.GetConnectionString()!, "AcademicCollector.Batch." + request.BatchId,
            30000, cancellationToken);
        if (gate is null)
            throw new InvalidOperationException("This batch is being submitted. Retry with the same BatchId.");
        BulkCollectionBatch? existing = await database.BulkCollectionBatches
            .SingleOrDefaultAsync(batch => batch.Id == request.BatchId, cancellationToken);
        if (existing is not null)
        {
            if (existing.InputHash != hash)
                throw new ArgumentException("This BatchId already belongs to different input.");
            return await GetStatusAsync(new() { BatchId = request.BatchId }, cancellationToken);
        }

        List<BulkNormalizationResult> prepared = request.Researchers.Select(normalizer.Normalize).ToList();
        HashSet<int> conflicts = FindConflicts(prepared);
        DateTime now = DateTime.UtcNow;
        database.BulkCollectionBatches.Add(new()
        {
            Id = request.BatchId,
            CreatedAt = now,
            InputHash = hash
        });
        for (int index = 0; index < prepared.Count; index++)
        {
            BulkNormalizationResult result = prepared[index];
            BulkResearcherInput input = result.Input;
            BulkCollectionJob job = new()
            {
                BatchId = request.BatchId,
                PersonelId = input.PersonelId.Trim(),
                InputJson = JsonSerializer.Serialize(new PersistedBulkResearcherInput
                {
                    OriginalInput = request.Researchers[index], Input = input, Warnings = result.Warnings
                }),
                NextAttemptAt = now
            };
            if (result.RejectionReason is not null)
            {
                job.Status = BulkJobStatus.Rejected;
                job.ResultMessage = result.RejectionReason;
                job.CompletedAt = now;
            }
            else if (conflicts.Contains(index))
            {
                job.Status = BulkJobStatus.Rejected;
                job.ResultMessage = "Conflicting source or provider identifier in this batch; manual review required.";
                job.CompletedAt = now;
            }
            database.BulkCollectionJobs.Add(job);
        }
        // EF saves batch and rows atomically. Invalid rows are visible without blocking valid rows.
        await database.SaveChangesAsync(cancellationToken);
        return await GetStatusAsync(new() { BatchId = request.BatchId }, cancellationToken);
    }

    public async Task<BulkCollectionStatusResponse> GetStatusAsync(
        BulkCollectionStatusRequest request, CancellationToken cancellationToken = default)
    {
        if (!await database.BulkCollectionBatches.AnyAsync(batch => batch.Id == request.BatchId, cancellationToken))
            throw new ArgumentException("Batch not found.");
        var jobs = database.BulkCollectionJobs.AsNoTracking().Where(job => job.BatchId == request.BatchId);
        var counts = await jobs.GroupBy(job => job.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(group => group.Status, group => group.Count, cancellationToken);
        return new()
        {
            BatchId = request.BatchId,
            Counts = counts,
            WorkerEnabled = options.Value.WorkerEnabled,
            IsComplete = !counts.Keys.Any(status => status is BulkJobStatus.Pending or
                BulkJobStatus.Running or BulkJobStatus.RetryWaiting),
            Jobs = (await jobs.OrderBy(job => job.Id).Skip(Math.Max(0, request.Skip))
                .Take(Math.Clamp(request.Take, 1, 500)).ToListAsync(cancellationToken)).Select(job => new BulkCollectionJobDto
                {
                    Id = job.Id,
                    PersonelId = job.PersonelId,
                    Status = job.Status,
                    Attempts = job.Attempts,
                    CollectorResearcherId = job.CollectorResearcherId,
                    NextAttemptAt = job.NextAttemptAt,
                    Message = job.ResultMessage,
                    Warnings = ReadPersisted(job.InputJson).Warnings
                }).ToList()
        };
    }

    internal static PersistedBulkResearcherInput ReadPersisted(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty(nameof(PersistedBulkResearcherInput.Input), out _))
            return JsonSerializer.Deserialize<PersistedBulkResearcherInput>(json)!;
        return new() { Input = JsonSerializer.Deserialize<BulkResearcherInput>(json)! };
    }

    private static HashSet<int> FindConflicts(IReadOnlyList<BulkNormalizationResult> rows)
    {
        HashSet<int> conflicts = [];
        Dictionary<string, List<int>> keys = new(StringComparer.Ordinal);
        for (int index = 0; index < rows.Count; index++)
        {
            BulkResearcherInput row = rows[index].Input;
            Add("personel:" + row.PersonelId, index);
            if (row.Orcid is not null) Add("orcid:" + row.Orcid, index);
            if (row.GoogleScholarId is not null) Add("scholar:" + row.GoogleScholarId, index);
            if (row.WebOfScienceId is not null) Add("wos:" + row.WebOfScienceId, index);
        }
        foreach (List<int> indexes in keys.Values.Where(value => value.Count > 1))
            foreach (int index in indexes) conflicts.Add(index);
        return conflicts;

        void Add(string key, int index)
        {
            if (!keys.TryGetValue(key, out List<int>? indexes)) keys[key] = indexes = [];
            indexes.Add(index);
        }
    }

    internal static ResearcherCollectRequest ToCollectionRequest(BulkResearcherInput input)
    {
        List<string> identifiers = [];
        if (!string.IsNullOrWhiteSpace(input.Orcid)) identifiers.AddRange(["--orcid", input.Orcid]);
        if (!string.IsNullOrWhiteSpace(input.GoogleScholarId)) identifiers.AddRange(["--scholar", input.GoogleScholarId]);
        if (!string.IsNullOrWhiteSpace(input.WebOfScienceId)) identifiers.AddRange(["--wos", input.WebOfScienceId]);
        return new() { Identifiers = identifiers };
    }
}
