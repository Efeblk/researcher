using System.Security.Cryptography;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using AcademicCollector.Analysis.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Knowledge;
using AcademicCollectorDemo.Modules.AcademicPerformance.ProductAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.FacultyAssistant;

public sealed class FacultyAssistantScheduler(AcademicDbContext database,
    IAcademicEvidenceSearchService search, IOptions<FacultyAssistantOptions> options,
    IOptions<ArticleSummaryAutomationOptions>? analysisOptions = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<FacultyAssistantRunResponse> EnqueueAsync(AcademicProductAccessGrant grant,
        StartFacultyAssistantRequest request, CancellationToken cancellationToken)
    {
        if (request.ClientRequestId == Guid.Empty) throw new FacultyAssistantInputException("ClientRequestId is required.");
        if (request.CanonicalWorkIds is null || request.CanonicalWorkIds.Any(value => value <= 0) ||
            request.CanonicalWorkIds.Distinct().Count() != request.CanonicalWorkIds.Count)
            throw new FacultyAssistantInputException("Canonical work IDs must be distinct positive values.");
        string requestJson = JsonSerializer.Serialize(request, JsonOptions);
        FacultyAssistantRun? prior = await database.FacultyAssistantRuns.AsNoTracking().SingleOrDefaultAsync(value =>
            value.PersonelId == grant.SubjectPersonelId && value.ActorAuditId == grant.ActorAuditId &&
            value.ClientRequestId == request.ClientRequestId, cancellationToken);
        if (prior is not null)
        {
            if (prior.RequestJson != requestJson) throw new FacultyAssistantConflictException();
            if (!await FacultyAssistantReadService.HasAssociationsAsync(database, prior, cancellationToken))
                throw new FacultyAssistantInputException("The saved run is no longer available under current publication associations.");
            return await FacultyAssistantReadService.MapAsync(database, prior, true, cancellationToken,
                analysisOptions?.Value.PolicyVersion);
        }
        FacultyAssistantContextVersion? context = await ContextAsync(grant.SubjectPersonelId,
            request.ContextVersion, cancellationToken);
        AcademicEvidenceSearchResponse retrieved = await search.SearchAsync(grant.SubjectPersonelId,
            new() { Query = request.Query, CanonicalWorkIds = request.CanonicalWorkIds, Take = request.Take }, cancellationToken);
        if (retrieved.Hits.Count == 0) throw new FacultyAssistantInputException("No current saved evidence matched this question.");
        if (retrieved.Hits.Any(hit => hit.AnalysisFreshnessStatus == AnalysisFreshnessStatus.Stale))
            throw new FacultyAssistantInputException(
                "The selected saved evidence is stale and must be regenerated before this question can be queued.");
        FacultyAssistantAnalysisRequest input = new()
        {
            Mode = request.Mode, Language = request.Language, Query = request.Query.Trim(),
            PrivateContext = context?.ContextJson,
            EvidenceCatalogHash = retrieved.InputHash,
            Evidence = retrieved.Hits.Select(hit => new FacultyAssistantEvidence(hit.EvidenceId,
                hit.CanonicalWorkId, hit.ArticleSourceSpanId, hit.SourceId, hit.PageNumber,
                hit.StartOffset, hit.EndOffset, hit.ExactText, hit.SourceKind, hit.IsPartial)).ToList()
        };
        List<ValidationResult> validationResults = [];
        if (!Validator.TryValidateObject(input, new ValidationContext(input), validationResults,
            validateAllProperties: true))
            throw new FacultyAssistantInputException(
                "The authorized assistant input is invalid or exceeds the shared analysis limits.");
        string inputJson = JsonSerializer.Serialize(input, JsonOptions);
        FacultyAssistantRun run = new()
        {
            RunId = Guid.NewGuid(), PersonelId = grant.SubjectPersonelId, ActorAuditId = grant.ActorAuditId,
            AuthorizationGrantId = grant.AuthorizationGrantId, ClientRequestId = request.ClientRequestId,
            Mode = request.Mode, Language = request.Language, ContextVersionId = context?.Id,
            RetrievalPolicyVersion = options.Value.RetrievalPolicyVersion, Status = "Pending",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            RequestJson = requestJson,
            RetrievalManifestJson = JsonSerializer.Serialize(retrieved, JsonOptions),
            AuthorizedInputJson = inputJson,
            InputFingerprint = Hash(options.Value.RetrievalPolicyVersion + "\n" + retrieved.InputHash + "\n" + inputJson)
        };
        database.FacultyAssistantRuns.Add(run);
        try { await database.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException)
        {
            database.ChangeTracker.Clear();
            FacultyAssistantRun raced = await database.FacultyAssistantRuns.AsNoTracking().SingleAsync(value =>
                value.PersonelId == grant.SubjectPersonelId && value.ActorAuditId == grant.ActorAuditId &&
                value.ClientRequestId == request.ClientRequestId, cancellationToken);
            if (raced.RequestJson != requestJson) throw new FacultyAssistantConflictException();
            if (!await FacultyAssistantReadService.HasAssociationsAsync(database, raced, cancellationToken))
                throw new FacultyAssistantInputException("The saved run is no longer available under current publication associations.");
            return await FacultyAssistantReadService.MapAsync(database, raced, true, cancellationToken,
                analysisOptions?.Value.PolicyVersion);
        }
        return await FacultyAssistantReadService.MapAsync(database, run, false, cancellationToken,
            analysisOptions?.Value.PolicyVersion);
    }

    private async Task<FacultyAssistantContextVersion?> ContextAsync(string personelId, int? version,
        CancellationToken cancellationToken)
    {
        IQueryable<FacultyAssistantContextVersion> query = database.FacultyAssistantContextVersions.AsNoTracking()
            .Where(value => value.PersonelId == personelId);
        return version.HasValue ? await query.SingleOrDefaultAsync(value => value.Version == version, cancellationToken)
            ?? throw new FacultyAssistantInputException("The requested private context version was not found.")
            : await query.OrderByDescending(value => value.Version).FirstOrDefaultAsync(cancellationToken);
    }

    private static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public sealed class FacultyAssistantInputException(string message) : Exception(message);
