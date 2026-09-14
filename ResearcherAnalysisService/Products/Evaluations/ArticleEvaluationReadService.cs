using System.Text.Json;
using AcademicCollector.Analysis.Contracts;
using ResearcherAnalysisService.Products.Api.Contracts;
using ResearcherAnalysisService.Products.Data;
using Microsoft.EntityFrameworkCore;
using ResearcherAnalysisService.Products.ProductAccess;

namespace ResearcherAnalysisService.Products.Evaluations;

public sealed class ArticleEvaluationReadService(AnalysisDbContext database)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ResearcherAnalysisService.Products.Api.Contracts.ArticleEvaluationResponse?> GetAsync(
        AcademicProductAccessGrant grant,
        GetArticleEvaluationRequest request,
        CancellationToken cancellationToken)
    {
        ArticleEvaluationRun? run = await database.ArticleEvaluationRuns.AsNoTracking()
            .SingleOrDefaultAsync(value => value.RunId == request.RunId, cancellationToken);
        if (run is null || run.OwnerPersonelId != grant.SubjectPersonelId ||
            !await CanReadAsync(run, grant.SubjectPersonelId, cancellationToken))
            return null;

        List<ArticleEvaluationCase> cases = await database.ArticleEvaluationCases.AsNoTracking()
            .Where(value => value.ArticleEvaluationRunId == run.Id).OrderBy(value => value.Ordinal)
            .ToListAsync(cancellationToken);
        Dictionary<long, ArticleEvaluationCase> casesById = cases.ToDictionary(value => value.Id);
        List<ArticleEvaluationWorkItem> allItems = await database.ArticleEvaluationWorkItems.AsNoTracking()
            .Include(value => value.Result).Include(value => value.Attempts)
            .Where(value => casesById.Keys.Contains(value.ArticleEvaluationCaseId))
            .OrderBy(value => value.Id).ToListAsync(cancellationToken);
        List<ArticleEvaluationProfile> profiles = JsonSerializer.Deserialize<List<ArticleEvaluationProfile>>(
            run.ProfilesJson, JsonOptions) ?? [];
        ArticleEvaluationAggregateDto aggregate = Aggregate(run, casesById, allItems);
        List<ArticleEvaluationProfileAggregateDto> profileAggregates = profiles.Select(profile =>
            AggregateProfile(run, profile.ProfileId, casesById,
                allItems.Where(value => value.ProfileId == profile.ProfileId).ToList())).ToList();
        List<ArticleEvaluationWorkItemDto> page = allItems.Skip(request.Skip).Take(request.Take)
            .Select(item => Map(item, casesById[item.ArticleEvaluationCaseId])).ToList();
        return new(run.RunId, run.Status, run.OwnerPersonelId, run.DatasetVersion, run.EvaluatorVersion,
            run.PolicyVersion, profiles.Select(profile => new ArticleEvaluationProfileDto(
                profile.ProfileId, profile.DisplayName, profile.Provider, profile.RequestedModel,
                profile.ModelRevision, profile.SettingsFingerprint, profile.SettingsVersion,
            profile.ExecutionSettings, profile.LocalOnly)).ToList(), run.TotalCases, run.TotalWorkItems,
            run.WorstCaseModelCalls,
            run.CompletedWorkItems, run.FailedWorkItems, run.SkippedWorkItems, run.CreatedAt, run.UpdatedAt,
            run.CompletedAt, aggregate, profileAggregates, request.Skip, request.Take, page.Count, page,
            "Calibration measures controlled source-reading against synthetic text. It does not validate scientific accuracy, real-article omission recall, or suitability for personnel decisions.");
    }

    private async Task<bool> CanReadAsync(ArticleEvaluationRun run, string personelId,
        CancellationToken cancellationToken)
    {
        if (!run.IncludesRealCases)
            return true;
        if (string.IsNullOrWhiteSpace(personelId) || personelId != run.OwnerPersonelId)
            return false;
        List<int> workIds = await database.ArticleEvaluationCases.AsNoTracking()
            .Where(value => value.ArticleEvaluationRunId == run.Id && value.CanonicalWorkId != null)
            .Select(value => value.CanonicalWorkId!.Value).Distinct().ToListAsync(cancellationToken);
        int associations = await database.CanonicalResearcherWorks.AsNoTracking()
            .CountAsync(value => value.PersonelId == personelId && workIds.Contains(value.CanonicalWorkId),
                cancellationToken);
        return associations == workIds.Count;
    }

    private static ArticleEvaluationAggregateDto Aggregate(
        ArticleEvaluationRun run,
        IReadOnlyDictionary<long, ArticleEvaluationCase> cases,
        IReadOnlyList<ArticleEvaluationWorkItem> items)
    {
        int scheduled = 0;
        int pending = 0;
        int failed = 0;
        int scored = 0;
        int correct = 0;
        int missing = 0;
        int invalid = 0;
        int uncertain = 0;
        Dictionary<(string Expected, string Actual), int> matrix = [];
        foreach (ArticleEvaluationWorkItem item in items.Where(value =>
            cases[value.ArticleEvaluationCaseId].Kind == ArticleEvaluationTaskKinds.Calibration))
        {
            ArticleEvaluationCase evaluationCase = cases[item.ArticleEvaluationCaseId];
            List<CalibrationReference> references = JsonSerializer.Deserialize<List<CalibrationReference>>(
                evaluationCase.ExpectedVerdictsJson!, JsonOptions) ?? [];
            scheduled += references.Count;
            if (item.Status is ArticleEvaluationStatus.Pending or ArticleEvaluationStatus.Running)
            {
                pending += references.Count;
                continue;
            }
            if (item.Status != ArticleEvaluationStatus.Completed || item.Result is null)
            {
                failed += references.Count;
                foreach (CalibrationReference reference in references)
                    matrix[(reference.ExpectedVerdict, "failed")] =
                        matrix.GetValueOrDefault((reference.ExpectedVerdict, "failed")) + 1;
                continue;
            }
            AcademicCollector.Analysis.Contracts.ArticleEvaluationResponse response =
                JsonSerializer.Deserialize<AcademicCollector.Analysis.Contracts.ArticleEvaluationResponse>(
                    item.Result.ResultJson, JsonOptions)!;
            CalibrationScore score = ArticleEvaluationScorer.Score(references,
                response.Verdicts?.Select(value => new EvaluationVerdict(value.ItemId, value.Verdict)).ToList());
            scored += score.ScoredClaims;
            correct += score.CorrectClaims;
            missing += score.MissingClaims;
            invalid += score.InvalidClaims;
            uncertain += response.Verdicts?.Count(value => value.Verdict == "uncertain") ?? 0;
            foreach (ConfusionCell cell in score.ConfusionMatrix)
                matrix[(cell.Expected, cell.Actual)] = matrix.GetValueOrDefault((cell.Expected, cell.Actual)) + cell.Count;
        }

        List<ArticleEvaluationAttempt> attempts = items.SelectMany(value => value.Attempts).ToList();
        bool hasEstimate = attempts.Any(value => value.EstimatedCostUsd.HasValue);
        decimal? cost = !hasEstimate ? null : attempts.Where(value => value.EstimatedCostUsd.HasValue)
            .Sum(value => value.EstimatedCostUsd!.Value);
        bool anyPartial = attempts.Any(value => value.CostStatus == "Partial");
        bool allKnown = attempts.Count > 0 && attempts.All(value =>
            value.CostStatus == "Known" && value.EstimatedCostUsd.HasValue);
        string costStatus = allKnown ? "Known" : hasEstimate || anyPartial ? "Partial" : "Unknown";
        List<double> exactEvidenceLinkRates = items.Where(value => value.Phase == ArticleEvaluationTaskKinds.Review && value.Result is not null)
            .Select(value => JsonSerializer.Deserialize<JsonElement>(value.Result!.MetricsJson, JsonOptions))
            .Where(value => value.TryGetProperty("exactEvidenceLinkRate", out JsonElement element) && element.ValueKind == JsonValueKind.Number)
            .Select(value => value.GetProperty("exactEvidenceLinkRate").GetDouble()).ToList();
        List<AcademicCollector.Analysis.Contracts.ArticleEvaluationResponse> reviewResponses = items
            .Where(value => value.Phase == ArticleEvaluationTaskKinds.Review && value.Result is not null)
            .Select(value => JsonSerializer.Deserialize<AcademicCollector.Analysis.Contracts.ArticleEvaluationResponse>(
                value.Result!.ResultJson, JsonOptions)!).ToList();
        List<ArticleEvaluationVerdict> crossVerdicts = items
            .Where(value => value.Phase == ArticleEvaluationTaskKinds.CrossCheck && value.Result is not null)
            .SelectMany(value => JsonSerializer.Deserialize<AcademicCollector.Analysis.Contracts.ArticleEvaluationResponse>(
                value.Result!.ResultJson, JsonOptions)?.Verdicts ?? []).ToList();
        bool terminal = run.Status is ArticleEvaluationStatus.Completed or ArticleEvaluationStatus.CompletedWithFailures or
            ArticleEvaluationStatus.Failed or ArticleEvaluationStatus.Cancelled;
        return new(scheduled, pending, failed, scored, correct, missing, invalid,
            terminal && scheduled > 0 ? (double)correct / scheduled : null,
            terminal && scheduled > 0 ? (double)uncertain / scheduled : null,
            terminal && scheduled > 0 ? (double)(scheduled - correct) / scheduled : null,
            matrix.OrderBy(value => value.Key.Expected, StringComparer.Ordinal)
                .ThenBy(value => value.Key.Actual, StringComparer.Ordinal)
                .Select(value => (object)new ConfusionCell(value.Key.Expected, value.Key.Actual, value.Value)).ToList(),
            cost, costStatus,
            crossVerdicts.Count == 0 ? null : (double)crossVerdicts.Count(value => value.Verdict == "supported") / crossVerdicts.Count,
            exactEvidenceLinkRates.Count == 0 ? null : exactEvidenceLinkRates.Average(),
            reviewResponses.Sum(value => value.ReviewReport?.Coverage.CandidateFindings ?? 0),
            reviewResponses.Sum(value => value.ReviewReport?.Coverage.SupportedFindings ?? 0),
            reviewResponses.Sum(value => value.ReviewReport?.Coverage.UnsupportedFindings ?? 0),
            reviewResponses.Sum(value => value.ReviewReport?.Coverage.UncertainFindings ?? 0),
            reviewResponses.Sum(value => value.ReviewReport?.Coverage.OmittedFindings ?? 0),
            null, null);
    }

    private static ArticleEvaluationWorkItemDto Map(ArticleEvaluationWorkItem item,
        ArticleEvaluationCase evaluationCase)
    {
        ArticleEvaluationAttempt? attempt = item.Attempts.OrderByDescending(value => value.AttemptNumber).FirstOrDefault();
        return new(item.Id, evaluationCase.CaseId, evaluationCase.Kind, item.Phase, item.ProfileId,
            item.Status, item.AttemptCount, item.OutcomeCode, item.Result?.ActualModelIdentity,
            Deserialize(item.Result?.MetricsJson), Deserialize(attempt?.TelemetryJson), attempt?.EstimatedCostUsd,
            attempt?.CostStatus ?? "Unknown");
    }

    private static object? Deserialize(string? json) => string.IsNullOrWhiteSpace(json)
        ? null : JsonSerializer.Deserialize<JsonElement>(json, JsonOptions);

    private static ArticleEvaluationProfileAggregateDto AggregateProfile(
        ArticleEvaluationRun run,
        string profileId,
        IReadOnlyDictionary<long, ArticleEvaluationCase> cases,
        IReadOnlyList<ArticleEvaluationWorkItem> items)
    {
        ArticleEvaluationAggregateDto aggregate = Aggregate(run, cases, items);
        List<ArticleEvaluationAttempt> attempts = items.SelectMany(value => value.Attempts).ToList();
        List<ArticleEvaluationTelemetry?> envelopes = attempts.Select(value => string.IsNullOrWhiteSpace(value.TelemetryJson)
            ? null : JsonSerializer.Deserialize<ArticleEvaluationTelemetry>(value.TelemetryJson!, JsonOptions)).ToList();
        List<ArticleEvaluationAttemptTelemetry> telemetry = envelopes.Where(value => value is not null)
            .SelectMany(value => value!.Attempts).ToList();
        bool completeTelemetryCoverage = attempts.Count > 0 && envelopes.All(value => value is not null) &&
            envelopes.All(value => value!.AttemptCount == value.Attempts.Count && value.Attempts.Count > 0);
        bool inputKnown = completeTelemetryCoverage && telemetry.All(value => value.InputTokens.HasValue);
        bool outputKnown = completeTelemetryCoverage && telemetry.All(value => value.OutputTokens.HasValue);
        string tokenStatus = inputKnown && outputKnown ? "Known" : telemetry.Any(value => value.InputTokens.HasValue || value.OutputTokens.HasValue)
            ? "Partial" : "Unknown";
        return new(profileId, aggregate, telemetry.Sum(value => value.ElapsedMilliseconds),
            inputKnown ? telemetry.Sum(value => value.InputTokens!.Value) : null,
            outputKnown ? telemetry.Sum(value => value.OutputTokens!.Value) : null, tokenStatus);
    }
}
