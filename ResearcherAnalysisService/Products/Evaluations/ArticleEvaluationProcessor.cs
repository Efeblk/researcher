using System.Data;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AcademicCollector.Analysis.Contracts;
using ResearcherAnalysisService.Products.ArticleReviews;
using ResearcherAnalysisService.Products.Data;
using ResearcherAnalysisService.Products.ProductAccess;
using Microsoft.EntityFrameworkCore;

namespace ResearcherAnalysisService.Products.Evaluations;

public sealed class ArticleEvaluationProcessor(
    AnalysisDbContext database,
    ArticleEvaluationServiceClient client,
    AnalysisSourceLock sourceLock,
    IAcademicProductAccessService access,
    ILogger<ArticleEvaluationProcessor> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        await using SqlApplicationLock? workerLock = await SqlApplicationLock.TryAcquireAsync(
            database.Database.GetConnectionString()!, "AcademicCollector.ArticleEvaluationWorker", 0,
            cancellationToken);
        if (workerLock is null)
            return false;
        ClaimedItem? claimed = await ClaimAsync(cancellationToken);
        if (claimed is null)
            return false;

        try
        {
            if (string.IsNullOrWhiteSpace(claimed.OwnerPersonelId))
            {
                await FinishFailureAsync(claimed, "AccessRevoked",
                    "Authorization is no longer available.", null, cancellationToken);
                return true;
            }
            AcademicProductAccessGrant persisted = new(claimed.AuthorizationGrantId, claimed.ActorAuditId,
                claimed.OwnerPersonelId, AcademicProductOperation.ArticleEvaluationStart);
            AcademicProductAccessGrant currentGrant = await access.ReauthorizeAsync(persisted, cancellationToken);
            if (currentGrant.SubjectPersonelId != claimed.OwnerPersonelId ||
                currentGrant.ActorAuditId != claimed.ActorAuditId)
            {
                await FinishFailureAsync(claimed, "AccessRevoked",
                    "Authorization is no longer available.", null, cancellationToken);
                return true;
            }
            if (claimed.CanonicalWorkId.HasValue &&
                !await HasCurrentAssociationAsync(claimed.OwnerPersonelId, claimed.CanonicalWorkId!.Value, cancellationToken))
            {
                await FinishFailureAsync(claimed, "AssociationLost",
                    "The owning researcher is no longer associated with this canonical work.", null, cancellationToken);
                return true;
            }
            ArticleEvaluationProfile profile = JsonSerializer.Deserialize<ArticleEvaluationProfile>(
                claimed.ProfileSnapshotJson, JsonOptions)
                ?? throw new JsonException("The pinned profile snapshot is unusable.");
            ArticleEvaluationProfilesResponse current = await client.GetProfilesAsync(cancellationToken);
            ArticleEvaluationProfile? currentProfile = current.Profiles.SingleOrDefault(value =>
                value.ProfileId == claimed.ProfileId);
            if (currentProfile is null || currentProfile.SettingsFingerprint != claimed.ProfileFingerprint)
            {
                await FinishFailureAsync(claimed, "ProfileDrift",
                    "The profile configuration no longer matches the enqueue-time fingerprint.", null, cancellationToken);
                return true;
            }

            BuiltRequest built = await BuildRequestAsync(claimed, cancellationToken);
            await StoreRequestAsync(claimed, built.Request, cancellationToken);
            AcademicCollector.Analysis.Contracts.ArticleEvaluationResponse response =
                await client.ExecuteAsync(profile, built.Request, cancellationToken);
            if (claimed.Phase == ArticleEvaluationTaskKinds.Review &&
                response.Outcome == ArticleEvaluationOutcomes.Completed)
            {
                if (response.ReviewReport is null)
                    throw new JsonException("A real review result omitted its report.");
                ArticleReviewServiceClient.Validate(response.ReviewReport, built.Request.Source);
            }
            if ((claimed.Phase is ArticleEvaluationTaskKinds.Calibration or ArticleEvaluationTaskKinds.CrossCheck) &&
                response.Outcome == ArticleEvaluationOutcomes.Completed && response.Verdicts is null)
                throw new JsonException("A verdict task omitted verdicts.");
            await FinishSuccessAsync(claimed, built, response, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ArticleEvaluationPreflightException exception)
        {
            string code = exception.StatusCode == HttpStatusCode.Conflict ? "ProfileDrift" : "PreflightRejected";
            await FinishFailureAsync(claimed, code, "The analysis service rejected the pinned request before generation.",
                null, CancellationToken.None);
        }
        catch (ArticleEvaluationNoInputException)
        {
            await FinishFailureAsync(claimed, "NoFindings",
                "Blind cross-check was skipped because the successful dependency retained no findings.",
                null, CancellationToken.None, ArticleEvaluationStatus.Skipped);
        }
        catch (Exception exception) when (exception is AcademicProductAccessUnavailableException or
            AcademicProductUnauthenticatedException or AcademicProductAccessDeniedException)
        {
            string code = exception is AcademicProductAccessUnavailableException
                ? "AuthorizationUnavailable" : "AccessRevoked";
            await FinishFailureAsync(claimed, code,
                "Authorization is no longer available.", null, CancellationToken.None);
        }
        catch (Exception exception)
        {
            await FinishFailureAsync(claimed, "ExecutionFailed",
                "The evaluation attempt failed; no retry was scheduled.", exception, CancellationToken.None);
            logger.LogWarning("Article evaluation work item {WorkItemId} failed ({ErrorType}).",
                claimed.WorkItemId, exception.GetType().Name);
        }
        return true;
    }

    private async Task<ClaimedItem?> ClaimAsync(CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        await sourceLock.AcquireWriteGateAsync(cancellationToken);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<ArticleEvaluationWorkItem> abandoned = await database.ArticleEvaluationWorkItems
            .Include(value => value.Attempts).Include(value => value.Case)
            .Where(value => value.Status == ArticleEvaluationStatus.Running).ToListAsync(cancellationToken);
        foreach (ArticleEvaluationWorkItem item in abandoned)
        {
            item.Status = ArticleEvaluationStatus.Interrupted;
            item.CompletedAt = now;
            item.ExecutionToken = null;
            item.OutcomeCode = "Interrupted";
            item.OutcomeMessage = "The process ended after a potentially paid call; this item was not retried.";
            foreach (ArticleEvaluationAttempt abandonedAttempt in item.Attempts.Where(value => value.Status == ArticleEvaluationStatus.Running))
            {
                abandonedAttempt.Status = ArticleEvaluationStatus.Interrupted;
                abandonedAttempt.CompletedAt = now;
                abandonedAttempt.CostStatus = "Unknown";
                abandonedAttempt.ErrorCode = "Interrupted";
                abandonedAttempt.ErrorMessage = item.OutcomeMessage;
            }
        }

        List<ArticleEvaluationWorkItem> blocked = await database.ArticleEvaluationWorkItems
            .Include(value => value.DependsOn).Include(value => value.Case)
            .Where(value => value.Status == ArticleEvaluationStatus.Pending && value.DependsOnWorkItemId != null &&
                value.DependsOn != null && value.DependsOn.Status != ArticleEvaluationStatus.Pending &&
                value.DependsOn.Status != ArticleEvaluationStatus.Running &&
                value.DependsOn.Status != ArticleEvaluationStatus.Completed)
            .ToListAsync(cancellationToken);
        foreach (ArticleEvaluationWorkItem item in blocked)
        {
            item.Status = ArticleEvaluationStatus.Skipped;
            item.CompletedAt = now;
            item.OutcomeCode = "DependencyFailed";
            item.OutcomeMessage = "Blind cross-check was skipped because its review dependency did not succeed.";
        }
        if (abandoned.Count > 0 || blocked.Count > 0)
        {
            await database.SaveChangesAsync(cancellationToken);
            await UpdateRunsAsync(now, cancellationToken);
        }

        ArticleEvaluationWorkItem? selected = await database.ArticleEvaluationWorkItems
            .Include(value => value.Case)!.ThenInclude(value => value!.Run)
            .Where(value => value.Status == ArticleEvaluationStatus.Pending && value.AttemptCount < value.MaximumAttempts &&
                (value.DependsOnWorkItemId == null || value.DependsOn != null &&
                    value.DependsOn.Status == ArticleEvaluationStatus.Completed))
            .OrderBy(value => value.Id).FirstOrDefaultAsync(cancellationToken);
        if (selected is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }
        Guid token = Guid.NewGuid();
        selected.Status = ArticleEvaluationStatus.Running;
        selected.ExecutionToken = token;
        selected.AttemptCount++;
        selected.StartedAt = now;
        selected.Case!.Run!.Status = ArticleEvaluationStatus.Running;
        selected.Case.Run.StartedAt ??= now;
        selected.Case.Run.UpdatedAt = now;
        ArticleEvaluationAttempt attempt = new()
        {
            AttemptNumber = selected.AttemptCount,
            ExecutionToken = token,
            StartedAt = now,
            RequestJson = "{}",
            RequestHash = new string('0', 64)
        };
        selected.Attempts.Add(attempt);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(selected.Id, attempt.Id, selected.Case.ArticleEvaluationRunId,
            selected.Case.CanonicalWorkId, selected.Case.BaseAnalysisRunId,
            selected.Case.SourceSnapshotJson, selected.Case.RequestPayloadJson,
            selected.Case.ExpectedVerdictsJson, selected.Phase, selected.ProfileId,
            selected.ProfileFingerprint, selected.ProfileSnapshotJson,
            selected.ExecutionToken!.Value, selected.Case.Run.OwnerPersonelId,
            selected.Case.Run.ActorAuditId, selected.Case.Run.AuthorizationGrantId);
    }

    private async Task<BuiltRequest> BuildRequestAsync(ClaimedItem item, CancellationToken cancellationToken)
    {
        StoredPayload payload = JsonSerializer.Deserialize<StoredPayload>(item.RequestPayloadJson, JsonOptions)
            ?? throw new JsonException("The immutable case request is unusable.");
        ArticleEvaluationRequest request = new(item.ProfileId, item.Phase,
            item.ProfileFingerprint, payload.Source);
        Dictionary<string, string>? findingMap = null;
        if (item.Phase == ArticleEvaluationTaskKinds.Calibration)
            request = request with { CalibrationClaims = payload.CalibrationClaims };
        else if (item.Phase == ArticleEvaluationTaskKinds.CrossCheck)
        {
            long dependencyId = await database.ArticleEvaluationWorkItems.AsNoTracking()
                .Where(value => value.Id == item.WorkItemId).Select(value => value.DependsOnWorkItemId!.Value)
                .SingleAsync(cancellationToken);
            ArticleEvaluationWorkItem dependency = await database.ArticleEvaluationWorkItems.AsNoTracking()
                .Include(value => value.Result)
                .SingleAsync(value => value.Id == dependencyId, cancellationToken);
            AcademicCollector.Analysis.Contracts.ArticleEvaluationResponse dependencyResponse =
                JsonSerializer.Deserialize<AcademicCollector.Analysis.Contracts.ArticleEvaluationResponse>(
                    dependency.Result!.ResultJson, JsonOptions)
                ?? throw new JsonException("The dependency result is unusable.");
            List<ArticleReviewFinding> original = dependencyResponse.ReviewReport?.Reviews
                .SelectMany(value => value.Findings).ToList()
                ?? throw new JsonException("The dependency contains no review report.");
            if (original.Count == 0)
                throw new ArticleEvaluationNoInputException();
            findingMap = [];
            List<ArticleReviewFinding> blind = [];
            for (int index = 0; index < original.Count; index++)
            {
                string opaqueId = $"f{index + 1:D4}";
                findingMap[opaqueId] = original[index].FindingId;
                blind.Add(original[index] with { FindingId = opaqueId });
            }
            request = request with { Findings = blind };
        }
        return new(request, findingMap);
    }

    private async Task FinishSuccessAsync(
        ClaimedItem claimed,
        BuiltRequest built,
        AcademicCollector.Analysis.Contracts.ArticleEvaluationResponse response,
        CancellationToken cancellationToken)
    {
        string requestJson = JsonSerializer.Serialize(built.Request, JsonOptions);
        string responseJson = JsonSerializer.Serialize(response, JsonOptions);
        string? metricsJson = response.Outcome == ArticleEvaluationOutcomes.Completed
            ? CreateMetrics(claimed, built, response) : null;
        CostSummary cost = Cost(response.Telemetry.Attempts);
        database.ChangeTracker.Clear();
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        await sourceLock.AcquireWriteGateAsync(cancellationToken);
        ArticleEvaluationWorkItem? item = await database.ArticleEvaluationWorkItems
            .Include(value => value.Attempts).Include(value => value.Case)!.ThenInclude(value => value!.Run)
            .SingleOrDefaultAsync(value => value.Id == claimed.WorkItemId, cancellationToken);
        if (item is null || item.Status != ArticleEvaluationStatus.Running || item.ExecutionToken != claimed.Token)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }
        ArticleEvaluationAttempt attempt = item.Attempts.Single(value => value.Id == claimed.AttemptId &&
            value.ExecutionToken == claimed.Token);
        attempt.RequestJson = requestJson;
        attempt.RequestHash = Hash(requestJson);
        attempt.ResponseJson = responseJson;
        attempt.ReturnedModelIdentity = string.Join(", ", response.ReturnedModels.Distinct(StringComparer.Ordinal));
        attempt.TelemetryJson = JsonSerializer.Serialize(response.Telemetry, JsonOptions);
        attempt.EstimatedCostUsd = cost.Value;
        attempt.CostStatus = cost.Status;
        attempt.ErrorCode = response.ErrorCode;
        attempt.ErrorMessage = FailureMessage(response.Failure);
        if (claimed.CanonicalWorkId.HasValue && claimed.OwnerPersonelId is not null &&
            !await HasCurrentAssociationAsync(claimed.OwnerPersonelId, claimed.CanonicalWorkId!.Value, cancellationToken))
        {
            await MarkFailureAsync(item, claimed, "AssociationLost",
                "The owning researcher association was removed before save.", null, cancellationToken);
            await UpdateRunAsync(item.Case!.Run!, DateTimeOffset.UtcNow, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }
        DateTimeOffset now = DateTimeOffset.UtcNow;
        item.Status = response.Outcome == ArticleEvaluationOutcomes.Completed
            ? ArticleEvaluationStatus.Completed : ArticleEvaluationStatus.Failed;
        item.CompletedAt = now;
        item.ExecutionToken = null;
        item.OutcomeCode = response.ErrorCode ?? response.Outcome;
        item.OutcomeMessage = FailureMessage(response.Failure);
        attempt.Status = item.Status;
        attempt.CompletedAt = now;
        if (response.Outcome == ArticleEvaluationOutcomes.Completed)
        {
            item.Result = new()
            {
                ActualModelIdentity = attempt.ReturnedModelIdentity,
                MetricsJson = metricsJson!,
                ResultJson = responseJson,
                CreatedAt = now
            };
        }
        await UpdateRunAsync(item.Case!.Run!, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task FinishFailureAsync(ClaimedItem claimed, string code, string message,
        Exception? exception, CancellationToken cancellationToken,
        string status = ArticleEvaluationStatus.Failed)
    {
        database.ChangeTracker.Clear();
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        await sourceLock.AcquireWriteGateAsync(cancellationToken);
        ArticleEvaluationWorkItem? item = await database.ArticleEvaluationWorkItems
            .Include(value => value.Attempts).Include(value => value.Case)!.ThenInclude(value => value!.Run)
            .SingleOrDefaultAsync(value => value.Id == claimed.WorkItemId, cancellationToken);
        if (item is not null && item.Status == ArticleEvaluationStatus.Running && item.ExecutionToken == claimed.Token)
        {
            await MarkFailureAsync(item, claimed, code, message, exception, cancellationToken, status);
            await UpdateRunAsync(item.Case!.Run!, DateTimeOffset.UtcNow, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task MarkFailureAsync(ArticleEvaluationWorkItem item, ClaimedItem claimed,
        string code, string message, Exception? exception, CancellationToken cancellationToken,
        string status = ArticleEvaluationStatus.Failed)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        item.Status = status;
        item.CompletedAt = now;
        item.ExecutionToken = null;
        item.OutcomeCode = code;
        item.OutcomeMessage = message;
        ArticleEvaluationAttempt attempt = item.Attempts.Single(value => value.Id == claimed.AttemptId &&
            value.ExecutionToken == claimed.Token);
        attempt.Status = status;
        attempt.CompletedAt = now;
        attempt.ErrorCode = code;
        attempt.ErrorMessage = exception is null ? message : $"{message} ({exception.GetType().Name})";
        if (attempt.TelemetryJson is null)
            attempt.CostStatus = "Unknown";
        await database.SaveChangesAsync(cancellationToken);
    }

    private async Task StoreRequestAsync(ClaimedItem claimed, ArticleEvaluationRequest request,
        CancellationToken cancellationToken)
    {
        string requestJson = JsonSerializer.Serialize(request, JsonOptions);
        database.ChangeTracker.Clear();
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        await sourceLock.AcquireWriteGateAsync(cancellationToken);
        ArticleEvaluationWorkItem? item = await database.ArticleEvaluationWorkItems
            .Include(value => value.Attempts)
            .SingleOrDefaultAsync(value => value.Id == claimed.WorkItemId, cancellationToken);
        if (item is null || item.Status != ArticleEvaluationStatus.Running || item.ExecutionToken != claimed.Token)
            throw new ArticleEvaluationAttemptLostException();
        ArticleEvaluationAttempt attempt = item.Attempts.Single(value => value.Id == claimed.AttemptId &&
            value.ExecutionToken == claimed.Token);
        attempt.RequestJson = requestJson;
        attempt.RequestHash = Hash(requestJson);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task UpdateRunAsync(ArticleEvaluationRun run, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await database.SaveChangesAsync(cancellationToken);
        await database.Entry(run).Collection(value => value.Cases).Query()
            .Include(value => value.WorkItems).LoadAsync(cancellationToken);
        List<ArticleEvaluationWorkItem> items = run.Cases.SelectMany(value => value.WorkItems).ToList();
        run.CompletedWorkItems = items.Count(value => value.Status == ArticleEvaluationStatus.Completed);
        run.FailedWorkItems = items.Count(value => value.Status is ArticleEvaluationStatus.Failed or ArticleEvaluationStatus.Interrupted);
        run.SkippedWorkItems = items.Count(value => value.Status == ArticleEvaluationStatus.Skipped);
        run.UpdatedAt = now;
        if (items.All(value => value.Status is not (ArticleEvaluationStatus.Pending or ArticleEvaluationStatus.Running)))
        {
            run.Status = run.FailedWorkItems + run.SkippedWorkItems == 0
                ? ArticleEvaluationStatus.Completed : ArticleEvaluationStatus.CompletedWithFailures;
            run.CompletedAt = now;
        }
        await database.SaveChangesAsync(cancellationToken);
    }

    private async Task UpdateRunsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        List<ArticleEvaluationRun> runs = await database.ArticleEvaluationRuns
            .Include(value => value.Cases).ThenInclude(value => value.WorkItems)
            .Where(value => value.Status == ArticleEvaluationStatus.Running).ToListAsync(cancellationToken);
        foreach (ArticleEvaluationRun run in runs)
        {
            List<ArticleEvaluationWorkItem> items = run.Cases.SelectMany(value => value.WorkItems).ToList();
            run.CompletedWorkItems = items.Count(value => value.Status == ArticleEvaluationStatus.Completed);
            run.FailedWorkItems = items.Count(value => value.Status is ArticleEvaluationStatus.Failed or ArticleEvaluationStatus.Interrupted);
            run.SkippedWorkItems = items.Count(value => value.Status == ArticleEvaluationStatus.Skipped);
            run.UpdatedAt = now;
            if (items.All(value => value.Status is not (ArticleEvaluationStatus.Pending or ArticleEvaluationStatus.Running)))
            {
                run.Status = ArticleEvaluationStatus.CompletedWithFailures;
                run.CompletedAt = now;
            }
        }
        await database.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> HasCurrentAssociationAsync(string personelId, int canonicalWorkId,
        CancellationToken cancellationToken) => await database.CanonicalResearcherWorks.AsNoTracking()
        .AnyAsync(value => value.PersonelId == personelId && value.CanonicalWorkId == canonicalWorkId,
            cancellationToken);

    private static string CreateMetrics(ClaimedItem claimed, BuiltRequest built,
        AcademicCollector.Analysis.Contracts.ArticleEvaluationResponse response)
    {
        if (claimed.Phase == ArticleEvaluationTaskKinds.Calibration)
        {
            List<CalibrationReference> references = JsonSerializer.Deserialize<List<CalibrationReference>>(
                claimed.ExpectedVerdictsJson!, JsonOptions) ?? [];
            CalibrationScore score = ArticleEvaluationScorer.Score(references,
                response.Verdicts?.Select(value => new EvaluationVerdict(value.ItemId, value.Verdict)).ToList());
            return JsonSerializer.Serialize(score, JsonOptions);
        }
        if (claimed.Phase == ArticleEvaluationTaskKinds.Review)
        {
            List<ArticleReviewFinding> findings = response.ReviewReport!.Reviews.SelectMany(value => value.Findings).ToList();
            int evidence = findings.Sum(value => value.Evidence.Count);
            return JsonSerializer.Serialize(new
            {
                FindingCount = findings.Count,
                EvidenceCount = evidence,
                ExactEvidenceLinkRate = evidence == 0 ? (double?)null : 1d,
                MetricDefinition = "Exact source-ID, page, offset, and quote catalog match only; it does not establish semantic or scientific correctness.",
                response.ReviewReport.Coverage.CandidateFindings,
                response.ReviewReport.Coverage.SupportedFindings,
                response.ReviewReport.Coverage.UnsupportedFindings,
                response.ReviewReport.Coverage.UncertainFindings,
                response.ReviewReport.Coverage.OmittedFindings,
                ScientificAccuracy = (double?)null,
                RealArticleOmissionRecall = (double?)null
            }, JsonOptions);
        }
        return JsonSerializer.Serialize(new
        {
            FindingIdMap = built.FindingIdMap,
            VerdictCount = response.Verdicts?.Count ?? 0,
            ScientificAccuracy = (double?)null,
            Limitation = "Cross-model verdict agreement is a weak signal; source wording and shared model behavior are not independent expert review."
        }, JsonOptions);
    }

    private static CostSummary Cost(IReadOnlyList<ArticleEvaluationAttemptTelemetry> attempts)
    {
        int known = attempts.Count(value => value.EstimatedCostUsd.HasValue);
        decimal? value = known == 0 ? null : attempts.Where(value => value.EstimatedCostUsd.HasValue)
            .Sum(value => value.EstimatedCostUsd!.Value);
        return new(value, known == 0 ? "Unknown" : known == attempts.Count ? "Known" : "Partial");
    }

    private static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string? FailureMessage(AnalysisFailureDetail? failure)
    {
        if (failure is null) return null;
        string location = string.Join("/", new[] { failure.Stage, failure.Role }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        return string.IsNullOrEmpty(location) ? failure.Reason : $"{failure.Reason} ({location})";
    }

    private sealed record StoredPayload(
        ReviewArticleRequest Source,
        IReadOnlyList<ArticleEvaluationCalibrationClaim>? CalibrationClaims);
    private sealed record BuiltRequest(ArticleEvaluationRequest Request, Dictionary<string, string>? FindingIdMap);
    private sealed record CostSummary(decimal? Value, string Status);
    private sealed record ClaimedItem(
        long WorkItemId, long AttemptId, long RunId, int? CanonicalWorkId, long? BaseAnalysisRunId,
        string SourceSnapshotJson, string RequestPayloadJson, string? ExpectedVerdictsJson,
        string Phase, string ProfileId, string ProfileFingerprint, string ProfileSnapshotJson,
        Guid Token, string? OwnerPersonelId, string ActorAuditId, string AuthorizationGrantId);
}

public sealed class ArticleEvaluationNoInputException : Exception { }
public sealed class ArticleEvaluationAttemptLostException : Exception { }
