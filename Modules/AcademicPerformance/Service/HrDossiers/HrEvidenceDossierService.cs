using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleReviews;
using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.ProductAccess;
using AcademicCollectorDemo.Modules.AcademicPerformance.Metrics;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.HrDossiers;

public sealed class HrEvidenceDossierService(AcademicDbContext database,
    IOptions<ArticleReviewOptions> reviewOptions,
    IOptions<PublicationMetricsOptions> metricOptions,
    TimeProvider timeProvider,
    IOptions<ArticleSummaryAutomationOptions>? analysisOptions = null)
{
    private const string PolicyVersion = "hr-evidence-dossier-v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> ActionTypes = new(StringComparer.Ordinal)
    {
        "Opened", "NoteAdded", "EvidenceQuestioned", "FollowUpRequested", "ReviewCompleted"
    };

    public async Task<HrEvidenceDossierResponse?> CreateAsync(
        AcademicProductAccessGrant grant,
        CreateHrEvidenceDossierRequest request,
        CancellationToken cancellationToken)
    {
        if (request.CanonicalWorkIds is null)
            throw new HrDossierInputException("Canonical work IDs are required.");
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var researcher = await database.Researchers.AsNoTracking().SingleOrDefaultAsync(
            value => value.PersonelId == grant.SubjectPersonelId, cancellationToken);
        if (researcher is null)
            return null;

        var metricQuery = database.PublicationMetricSnapshots.AsNoTracking()
            .Where(value => value.PersonelId == grant.SubjectPersonelId);
        var metric = request.PublicationMetricSnapshotId.HasValue
            ? await metricQuery.SingleOrDefaultAsync(value => value.Id == request.PublicationMetricSnapshotId, cancellationToken)
            : await metricQuery.OrderByDescending(value => value.Id).FirstOrDefaultAsync(cancellationToken);
        if (request.PublicationMetricSnapshotId.HasValue && metric is null)
            throw new HrDossierInputException("The requested publication metric snapshot is unavailable for this subject.");

        IQueryable<CanonicalWork> workQuery = database.CanonicalWorks.AsNoTracking()
            .Where(work => work.Researchers.Any(value => value.PersonelId == grant.SubjectPersonelId));
        if (request.CanonicalWorkIds.Count > 0)
        {
            List<int> requestedIds = request.CanonicalWorkIds.Distinct().Order().ToList();
            if (requestedIds.Count != request.CanonicalWorkIds.Count || requestedIds.Any(id => id <= 0))
                throw new HrDossierInputException("Canonical work IDs must be distinct positive values.");
            workQuery = workQuery.Where(work => requestedIds.Contains(work.Id));
        }
        List<CanonicalWork> works = await workQuery.OrderBy(value => value.Id).Take(201)
            .Include(work => work.Observations.Where(value => value.PersonelId == grant.SubjectPersonelId))
            .ToListAsync(cancellationToken);
        if (works.Count > 200)
            throw new HrDossierInputException("The dossier exceeds the 200-work limit; select an explicit bounded set.");
        if (request.CanonicalWorkIds.Count > 0 && works.Count != request.CanonicalWorkIds.Count)
            throw new HrDossierInputException("One or more canonical works are not currently associated with this subject.");

        List<int> workIds = works.Select(value => value.Id).ToList();
        IQueryable<long> latestReviewIds = database.CanonicalArticleReviewRuns.AsNoTracking()
            .Where(run => workIds.Contains(run.CanonicalWorkId) && run.Language == request.Language)
            .GroupBy(run => run.CanonicalWorkId).Select(group => group.Max(run => run.Id));
        List<CanonicalArticleReviewRun> reviews = await database.CanonicalArticleReviewRuns.AsNoTracking()
            .Where(run => latestReviewIds.Contains(run.Id))
            .Include(run => run.ArticleSourceSnapshot)
            .Include(run => run.Findings).ThenInclude(finding => finding.Evidence)
                .ThenInclude(evidence => evidence.ArticleSourceSpan)
            .AsSplitQuery().ToListAsync(cancellationToken);
        string currentAnalysisPolicy = analysisOptions?.Value.PolicyVersion?.Trim() ??
            new ArticleSummaryAutomationOptions().PolicyVersion;
        IReadOnlyDictionary<long, AnalysisFreshnessResult> analysisFreshness =
            await AnalysisFreshnessEvaluator.EvaluateAsync(database,
                reviews.Select(value => value.BaseAnalysisRunId).Distinct().ToArray(),
                currentAnalysisPolicy, cancellationToken);
        string analysisFreshnessHash = AnalysisFreshnessEvaluator.CreateFreshnessHash(
            analysisFreshness.Values, currentAnalysisPolicy);

        ResearcherPublicationMetricsResponse? metrics = metric is null ? null :
            JsonSerializer.Deserialize<ResearcherPublicationMetricsResponse>(metric.ResultJson, JsonOptions)
            ?? throw new HrDossierInputException("The saved publication metric snapshot is unusable.");
        int validYearUpperBound = metrics?.ValidYearUpperBound ?? DateTime.UtcNow.Year + 1;
        List<HrDossierWork> dossierWorks = works.Select(work => MapWork(work,
            reviews.SingleOrDefault(review => review.CanonicalWorkId == work.Id),
            validYearUpperBound, reviewOptions.Value.PolicyVersion.Trim(),
            reviews.Where(review => review.CanonicalWorkId == work.Id)
                .Select(review => analysisFreshness.GetValueOrDefault(review.BaseAnalysisRunId))
                .SingleOrDefault())).ToList();
        List<string> limitations =
        [
            "This dossier covers collected and saved records only; it is not an institution-complete publication catalog.",
            "Provider bibliometrics retain their provider-specific scope and are not interchangeable.",
            "Exact evidence links show where text was found; they do not prove scientific correctness or complete issue recall.",
            "Article review findings are automatically checked document observations or reviewer questions, not publication-quality or personnel-suitability judgments.",
            "This dossier records evidence for an authorized human review and contains no hiring, ranking, suitability, or quality decision."
        ];
        if (metrics is null)
            limitations.Add("No saved publication metric snapshot was available when this dossier was captured.");
        if (analysisFreshness.Values.Any(value => value.Status == AnalysisFreshnessStatus.Unknown))
            limitations.Add("One or more article review source analyses have unknown freshness because their saved automation identity is incomplete.");
        List<string> metricStaleReasons = [];
        if (metric is not null)
        {
            var state = await database.PublicationMetricsRefreshStates.AsNoTracking().SingleOrDefaultAsync(
                value => value.PersonelId == grant.SubjectPersonelId, cancellationToken);
            if (state is null)
            {
                metricStaleReasons.Add("No current publication metric refresh state exists for this snapshot.");
            }
            else
            {
                if (state.LastSuccessfulSnapshotId != metric.Id)
                    metricStaleReasons.Add("A different publication metric snapshot is selected by current state.");
                if (state.ComputedRevision < state.RequestedRevision)
                    metricStaleReasons.Add("A newer publication metric source revision is waiting to be computed.");
                if (metric.SourceRevision != state.ComputedRevision)
                    metricStaleReasons.Add("The metric source revision is no longer current.");
            }
            if (metric.CatalogVersion != metricOptions.Value.CatalogVersion.Trim())
                metricStaleReasons.Add("The publication metric catalog version has changed.");
            if (metric.ComputationYear != timeProvider.GetUtcNow().Year)
                metricStaleReasons.Add("The publication metric computation year has changed.");
        }
        HrDossierMetricEvidence? metricEvidence = metrics is null ? null : new(metrics,
            metricStaleReasons.Count > 0, metricStaleReasons);
        HrEvidenceDossierContent content = new(
            new(grant.SubjectPersonelId, $"{researcher.FirstName} {researcher.LastName}".Trim(),
                researcher.AcademicTitle, researcher.Department), metricEvidence, dossierWorks, limitations);
        object manifest = new
        {
            request.Language,
            PublicationMetricSnapshotId = metric?.Id,
            CanonicalWorkIds = workIds,
            ReviewRunIds = reviews.OrderBy(value => value.Id).Select(value => value.Id).ToList(),
            ReviewSources = reviews.OrderBy(value => value.Id).Select(value => new
                { value.Id, value.ArticleSourceSnapshotId, value.ArticleSourceSnapshot!.ExtractedTextHash }).ToList(),
            AnalysisFreshnessHash = analysisFreshnessHash,
            AnalysisFreshness = analysisFreshness.Values.OrderBy(value => value.AnalysisRunId).Select(value => new
                { value.AnalysisRunId, value.Status, value.Reasons, value.Identity }).ToList(),
            SourceSpanIds = reviews.SelectMany(value => value.Findings).SelectMany(value => value.Evidence)
                .Select(value => value.ArticleSourceSpanId).Distinct().Order().ToList(),
            Observations = works.SelectMany(work => work.Observations).OrderBy(value => value.Id).Select(value => new
                { value.Id, value.CanonicalWorkId, value.Provider, value.PublicationYearObserved,
                    value.PublicationDateObserved, value.CategoryObserved, value.IsRetracted, value.ObservedAt }).ToList()
        };
        string manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
        string contentJson = JsonSerializer.Serialize(content, JsonOptions);
        string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            PolicyVersion + "\n" + manifestJson + "\n" + contentJson))).ToLowerInvariant();
        HrEvidenceDossier dossier = new()
        {
            PersonelId = grant.SubjectPersonelId,
            CreatedByActorId = grant.ActorAuditId,
            CreatedAt = DateTimeOffset.UtcNow,
            PolicyVersion = PolicyVersion,
            PublicationMetricSnapshotId = metric?.Id,
            InputFingerprint = fingerprint,
            InputManifestJson = manifestJson,
            DossierJson = contentJson
        };
        database.HrEvidenceDossiers.Add(dossier);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Map(dossier);
    }

    public async Task<HrEvidenceDossierResponse?> GetAsync(
        AcademicProductAccessGrant grant, long id, CancellationToken cancellationToken)
    {
        HrEvidenceDossier? dossier = await database.HrEvidenceDossiers.AsNoTracking().SingleOrDefaultAsync(
            value => value.Id == id && value.PersonelId == grant.SubjectPersonelId, cancellationToken);
        return dossier is null ? null : Map(dossier);
    }

    public async Task<HrDossierReviewActionResponse?> AppendActionAsync(
        AcademicProductAccessGrant grant, AppendHrDossierReviewActionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ClientRequestId == Guid.Empty || !ActionTypes.Contains(request.ActionType))
            throw new HrDossierInputException("Supply a request ID and an allowed procedural action type.");
        bool exists = await database.HrEvidenceDossiers.AsNoTracking().AnyAsync(value =>
            value.Id == request.DossierId && value.PersonelId == grant.SubjectPersonelId, cancellationToken);
        if (!exists) return null;
        string? evidenceReference = NormalizeOptional(request.EvidenceReference);
        string? note = NormalizeOptional(request.Note);
        HrDossierReviewAction? prior = await database.HrDossierReviewActions.AsNoTracking().SingleOrDefaultAsync(value =>
            value.DossierId == request.DossierId && value.ActorAuditId == grant.ActorAuditId &&
            value.ClientRequestId == request.ClientRequestId, cancellationToken);
        if (prior is not null)
        {
            if (prior.ActionType != request.ActionType || prior.EvidenceReference != evidenceReference || prior.Note != note)
                throw new HrDossierConflictException();
            return new(request.DossierId, Map(prior), true);
        }
        HrDossierReviewAction action = new()
        {
            DossierId = request.DossierId, ActorAuditId = grant.ActorAuditId,
            ClientRequestId = request.ClientRequestId, ActionType = request.ActionType,
            EvidenceReference = evidenceReference, Note = note,
            RecordedAt = DateTimeOffset.UtcNow
        };
        database.HrDossierReviewActions.Add(action);
        try { await database.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException)
        {
            database.ChangeTracker.Clear();
            HrDossierReviewAction raced = await database.HrDossierReviewActions.AsNoTracking().SingleAsync(value =>
                value.DossierId == request.DossierId && value.ActorAuditId == grant.ActorAuditId &&
                value.ClientRequestId == request.ClientRequestId, cancellationToken);
            if (raced.ActionType != request.ActionType || raced.EvidenceReference != evidenceReference || raced.Note != note)
                throw new HrDossierConflictException();
            return new(request.DossierId, Map(raced), true);
        }
        return new(request.DossierId, Map(action), false);
    }

    public async Task<HrDossierReviewActionListResponse?> ListActionsAsync(
        AcademicProductAccessGrant grant, ListHrDossierReviewActionsRequest request,
        CancellationToken cancellationToken)
    {
        bool exists = await database.HrEvidenceDossiers.AsNoTracking().AnyAsync(value =>
            value.Id == request.DossierId && value.PersonelId == grant.SubjectPersonelId, cancellationToken);
        if (!exists) return null;
        IQueryable<HrDossierReviewAction> query = database.HrDossierReviewActions.AsNoTracking()
            .Where(value => value.DossierId == request.DossierId);
        int count = await query.CountAsync(cancellationToken);
        List<HrDossierReviewActionDto> actions = await query.OrderBy(value => value.Id)
            .Skip(request.Skip).Take(request.Take).Select(value => Map(value)).ToListAsync(cancellationToken);
        return new(request.DossierId, count, request.Skip, request.Take, actions);
    }

    private static HrDossierWork MapWork(CanonicalWork work, CanonicalArticleReviewRun? review,
        int validYearUpperBound, string currentReviewPolicy, AnalysisFreshnessResult? analysisFreshness)
    {
        var years = work.Observations.Where(value => value.PublicationYearObserved.HasValue)
            .Select(value => new HrDossierObservedValue<int>(value.PublicationYearObserved!.Value,
                value.Provider.ToString(), value.ObservedAt,
                value.PublicationYearObserved is >= 1 && value.PublicationYearObserved <= validYearUpperBound))
            .OrderBy(value => value.Value).ThenBy(value => value.Provider).ToList();
        var categories = work.Observations
            .Select(value => new HrDossierObservedValue<string>(value.CategoryObserved.ToString(),
                value.Provider.ToString(), value.ObservedAt, value.CategoryObserved != AcademicWorkCategory.Unknown))
            .OrderBy(value => value.Value).ThenBy(value => value.Provider).ToList();
        List<string> staleReasons = analysisFreshness?.Status == AnalysisFreshnessStatus.Stale
            ? analysisFreshness.Reasons.ToList() : [];
        if (review is not null && review.PolicyVersion != currentReviewPolicy)
            staleReasons.Add("The configured article review policy has changed.");
        return new(work.Id, work.NormalizedDoi, years, categories,
            Status(years.Where(value => value.IsValid).Select(value => value.Value)),
            Status(categories.Where(value => value.IsValid).Select(value => value.Value)),
            work.HasRetractionObservation, review is null ? [] : review.Findings.OrderBy(value => value.Ordinal)
                .Select(finding => new HrDossierArticleReview(review.Id, review.BaseAnalysisRunId,
                    review.ArticleSourceSnapshotId, review.ArticleSourceSnapshot!.ExtractedTextHash, review.ReviewedAt,
                    finding.Role, finding.Kind, finding.Basis, finding.Suggestion,
                    finding.Evidence.OrderBy(value => value.Ordinal).Select(evidence => new HrDossierEvidence(
                        evidence.ArticleSourceSpanId, evidence.ArticleSourceSpan!.SourceId,
                        evidence.ArticleSourceSpan.PageNumber, evidence.ArticleSourceSpan.StartOffset,
                        evidence.ArticleSourceSpan.EndOffset, evidence.ArticleSourceSpan.Text)).ToList(),
                    review.VerificationStatus, review.IsPartial, staleReasons.Count > 0, staleReasons,
                    analysisFreshness?.Status ?? AnalysisFreshnessStatus.Unknown,
                    analysisFreshness?.Reasons ?? ["FreshnessIdentityUnavailable"])).ToList());
    }

    private static string Status<T>(IEnumerable<T> values) => values.Distinct().Take(2).Count() switch
    { 0 => "Unknown", 1 => "Resolved", _ => "Conflict" };
    private static HrEvidenceDossierResponse Map(HrEvidenceDossier value) => new(value.Id, value.PersonelId,
        value.CreatedAt, value.PolicyVersion, value.InputFingerprint,
        JsonSerializer.Deserialize<HrEvidenceDossierContent>(value.DossierJson, JsonOptions)!);
    private static HrDossierReviewActionDto Map(HrDossierReviewAction value) => new(value.Id,
        value.ActionType, value.EvidenceReference, value.Note, value.ActorAuditId, value.RecordedAt);
    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class HrDossierInputException(string message) : Exception(message);
public sealed class HrDossierConflictException : Exception;
