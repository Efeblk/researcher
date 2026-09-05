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
    AcademicDbContext database, ResearcherIdentifierParser parser,
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
            string.IsNullOrWhiteSpace(row.SourceResearcherId) || row.SourceResearcherId.Length > 200 ||
            row.Orcid?.Length > 200 || row.GoogleScholarId?.Length > 200 || row.WebOfScienceId?.Length > 200))
            throw new ArgumentException("Rows require a source researcher ID; input fields must be at most 200 characters.");

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

        DateTime now = DateTime.UtcNow;
        database.BulkCollectionBatches.Add(new()
        {
            Id = request.BatchId,
            CreatedAt = now,
            InputHash = hash
        });
        HashSet<string> sourceIds = new(StringComparer.Ordinal);
        HashSet<string> identities = new(StringComparer.Ordinal);
        foreach (BulkResearcherInput input in request.Researchers)
        {
            BulkCollectionJob job = new()
            {
                BatchId = request.BatchId,
                SourceResearcherId = input.SourceResearcherId.Trim(),
                InputJson = JsonSerializer.Serialize(input),
                NextAttemptAt = now
            };
            try
            {
                var normalized = parser.Create(ToCollectionRequest(input));
                string identity = JsonSerializer.Serialize(new[]
                {
                    normalized.Orcid, normalized.GoogleScholarId, normalized.WebOfScienceResearcherId
                });
                if (!sourceIds.Add(job.SourceResearcherId) || !identities.Add(identity))
                    throw new ArgumentException("Duplicate row.");
            }
            catch (ArgumentException)
            {
                job.Status = BulkJobStatus.Rejected;
                job.ResultMessage = "Invalid provider identifier or duplicate researcher in this batch.";
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
            Jobs = await jobs.OrderBy(job => job.Id).Skip(Math.Max(0, request.Skip))
                .Take(Math.Clamp(request.Take, 1, 500)).Select(job => new BulkCollectionJobDto
                {
                    Id = job.Id,
                    SourceResearcherId = job.SourceResearcherId,
                    Status = job.Status,
                    Attempts = job.Attempts,
                    ResearcherId = job.ResearcherId,
                    NextAttemptAt = job.NextAttemptAt,
                    Message = job.ResultMessage
                }).ToListAsync(cancellationToken)
        };
    }

    private static ResearcherCollectRequest ToCollectionRequest(BulkResearcherInput input)
    {
        List<string> identifiers = [];
        if (!string.IsNullOrWhiteSpace(input.Orcid)) identifiers.AddRange(["--orcid", input.Orcid]);
        if (!string.IsNullOrWhiteSpace(input.GoogleScholarId)) identifiers.AddRange(["--scholar", input.GoogleScholarId]);
        if (!string.IsNullOrWhiteSpace(input.WebOfScienceId)) identifiers.AddRange(["--wos", input.WebOfScienceId]);
        return new() { Identifiers = identifiers };
    }
}
