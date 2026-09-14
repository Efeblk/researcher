using System.Text.Json;
using AcademicCollector.Analysis.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.ProductAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.FacultyAssistant;

public sealed class FacultyAssistantReadService(AcademicDbContext database,
    IOptions<ArticleSummaryAutomationOptions>? analysisOptions = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public async Task<FacultyAssistantRunResponse?> GetAsync(AcademicProductAccessGrant grant,
        Guid runId, CancellationToken cancellationToken)
    {
        FacultyAssistantRun? run = await database.FacultyAssistantRuns.AsNoTracking().SingleOrDefaultAsync(value =>
            value.RunId == runId && value.PersonelId == grant.SubjectPersonelId, cancellationToken);
        if (run is null || !await HasAssociationsAsync(database, run, cancellationToken)) return null;
        return await MapAsync(database, run, false, cancellationToken,
            analysisOptions?.Value.PolicyVersion);
    }

    internal static async Task<bool> HasAssociationsAsync(AcademicDbContext database, FacultyAssistantRun run,
        CancellationToken cancellationToken)
    {
        FacultyAssistantAnalysisRequest? input = JsonSerializer.Deserialize<FacultyAssistantAnalysisRequest>(
            run.AuthorizedInputJson ?? "", JsonOptions);
        if (input is null) return false;
        int[] ids = input.Evidence.Select(value => value.CanonicalWorkId).Distinct().ToArray();
        int count = await database.CanonicalResearcherWorks.AsNoTracking().CountAsync(value =>
            value.PersonelId == run.PersonelId && ids.Contains(value.CanonicalWorkId), cancellationToken);
        return count == ids.Length;
    }

    internal static async Task<FacultyAssistantRunResponse> MapAsync(AcademicDbContext database,
        FacultyAssistantRun run, bool reused, CancellationToken cancellationToken,
        string? currentPolicyVersion = null)
    {
        FacultyAssistantContextVersion? context = run.ContextVersionId.HasValue
            ? await database.FacultyAssistantContextVersions.AsNoTracking().SingleOrDefaultAsync(
                value => value.Id == run.ContextVersionId.Value, cancellationToken)
            : null;
        FacultyAssistantPinnedContext? pinnedContext = context is null ? null :
            new(context.Id, context.Version, context.ContextFingerprint);
        AcademicEvidenceSearchResponse retrieved = JsonSerializer.Deserialize<AcademicEvidenceSearchResponse>(
            run.RetrievalManifestJson, JsonOptions) ?? throw new JsonException();
        string policyVersion = currentPolicyVersion?.Trim() ?? new ArticleSummaryAutomationOptions().PolicyVersion;
        IReadOnlyDictionary<long, AnalysisFreshnessResult> currentFreshness =
            await AnalysisFreshnessEvaluator.EvaluateAsync(database,
                retrieved.Hits.Select(value => value.AnalysisRunId).Where(value => value > 0).Distinct().ToArray(),
                policyVersion, cancellationToken);
        string currentFreshnessHash = AnalysisFreshnessEvaluator.CreateFreshnessHash(
            currentFreshness.Values, policyVersion);
        FacultyAssistantRetrievalSummary retrieval = new(
            retrieved.Catalog, retrieved.CatalogVersion, retrieved.QueryHash, retrieved.CorpusHash,
            retrieved.InputHash, retrieved.CorpusFreshness, retrieved.RequestedCanonicalWorkCount,
            retrieved.EligibleCanonicalWorkCount, retrieved.CoveredCanonicalWorkCount,
            retrieved.MissingSourceCanonicalWorkCount, retrieved.CandidateSpanCount,
            retrieved.IsCorpusTruncated,
            retrieved.AnalysisLanguageCoverage.Select(value => new FacultyAssistantRetrievalCoverage(
                value.Value, value.CanonicalWorkCount)).ToList(),
            retrieved.SourceKindCoverage.Select(value => new FacultyAssistantRetrievalCoverage(
                value.Value, value.CanonicalWorkCount)).ToList(),
            retrieved.Hits.Select(value => new FacultyAssistantRetrievedEvidence(
                value.EvidenceId, value.CanonicalWorkId, value.ArticleSourceSnapshotId,
                value.ArticleSourceSpanId, value.AnalysisRunId, value.SourceId, value.PageNumber,
                value.StartOffset, value.EndOffset, value.AnalysisLanguage, value.SourceKind,
                value.ExtractionVersion, value.ExtractedTextHash, value.IsPartial,
                new FacultyAssistantSourceCoverage(value.SourceCoverage.ProcessedChunks,
                    value.SourceCoverage.TotalChunks, value.SourceCoverage.ProcessedPages,
                    value.SourceCoverage.TextBearingPages, value.SourceCoverage.TotalPages,
                    value.SourceCoverage.ScopeReason),
                value.MatchProvenance?.Select(match => new FacultyAssistantMatchProvenance(
                    match.Kind, match.AnalysisRunId, match.Language,
                    match.CanonicalArticleClaimId, match.Section)).ToList(),
                value.IsMatchProvenanceTruncated,
                value.AnalysisFreshnessStatus,
                value.AnalysisFreshnessReasons,
                currentFreshness.GetValueOrDefault(value.AnalysisRunId)?.Status ?? AnalysisFreshnessStatus.Unknown,
                currentFreshness.GetValueOrDefault(value.AnalysisRunId)?.Reasons ?? ["AnalysisRunUnavailable"])).ToList(),
            retrieved.QueryPlanHash, retrieved.ClaimBridgeHash,
            retrieved.ClaimBridgeEligibleRunCount,
            retrieved.ClaimBridgeCoveredCanonicalWorkCount,
            retrieved.ClaimBridgeCandidateClaimCount,
            retrieved.ClaimBridgeCandidateSpanCount,
            retrieved.IsClaimBridgeTruncated,
            retrieved.ClaimBridgeLanguageCoverage?.Select(value =>
                new FacultyAssistantRetrievalCoverage(value.Value, value.CanonicalWorkCount)).ToList(),
            retrieved.IsDirectCorpusTruncated,
            retrieved.FreshnessHash,
            retrieved.CurrentAnalysisRunCount,
            retrieved.StaleAnalysisRunCount,
            retrieved.UnknownAnalysisRunCount,
            currentFreshnessHash,
            currentFreshness.Values.Count(value => value.Status == AnalysisFreshnessStatus.Current),
            currentFreshness.Values.Count(value => value.Status == AnalysisFreshnessStatus.Stale),
            currentFreshness.Values.Count(value => value.Status == AnalysisFreshnessStatus.Unknown));
        return new(run.RunId, run.PersonelId, run.Status, run.CreatedAt, run.UpdatedAt,
            run.AttemptCount, run.RetrievalPolicyVersion, pinnedContext, retrieval,
            run.InputFingerprint, run.ReportJson is null ? null :
                JsonSerializer.Deserialize<FacultyAssistantAnalysisReport>(run.ReportJson, JsonOptions),
            run.ErrorCode, run.ErrorMessage, reused);
    }
}
