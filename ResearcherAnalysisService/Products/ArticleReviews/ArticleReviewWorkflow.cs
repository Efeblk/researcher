using System.Data;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using AcademicCollector.Analysis.Contracts;
using ResearcherAnalysisService.Products.Api.Contracts;
using ResearcherAnalysisService.Products.ArticleSummaries;
using ResearcherAnalysisService.Products.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ResearcherAnalysisService.Products.ArticleReviews;

public sealed class ArticleReviewWorkflow(
    AnalysisDbContext database,
    ArticleReviewServiceClient client,
    AnalysisSourceLock sourceLock,
    IOptions<ArticleReviewOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CanonicalArticleReviewResponse> ReviewAsync(
        string personelId,
        int canonicalWorkId,
        string language,
        bool forceRegeneration,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource total = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        total.CancelAfter(TimeSpan.FromSeconds(options.Value.TotalTimeoutSeconds));
        long? initialBaseId = await LoadLatestBaseIdAsync(
            personelId, canonicalWorkId, language, total.Token);
        if (initialBaseId is null)
            throw new ArticleReviewUnavailableException(
                "No current researcher association with a successful canonical article analysis was found.");

        await using SqlApplicationLock? executionLock = await SqlApplicationLock.TryAcquireAsync(
            database.Database.GetConnectionString()!,
            $"AcademicCollector.ArticleReview.{canonicalWorkId}.{language}",
            0,
            total.Token);
        if (executionLock is null)
            throw new ArticleReviewBusyException();

        database.ChangeTracker.Clear();
        CanonicalArticleAnalysisRun? captured = await LoadLatestBaseAsync(
            personelId, canonicalWorkId, language, total.Token);
        if (captured is null)
            throw new ArticleReviewUnavailableException(
                "The researcher association or canonical article analysis is no longer available.");
        string policyVersion = options.Value.PolicyVersion.Trim();
        ArticleReviewRuntimeConfiguration configuration = await client.GetConfigurationAsync(total.Token);
        ReviewArticleRequest request = CreateRequest(captured, policyVersion);
        int sourceBytes = request.SourceSpans!.Sum(span =>
            Encoding.UTF8.GetByteCount(span.SourceId) + Encoding.UTF8.GetByteCount(span.Text) + 80);
        if (sourceBytes > options.Value.MaximumSourceBytes)
            throw new ArticleReviewInputTooLargeException();
        ArticleReviewWorkItem? resumable = forceRegeneration ? null : await FindResumableAsync(
            captured, request, configuration, total.Token);
        if (!forceRegeneration && resumable is null)
        {
            CanonicalArticleReviewRun? reusable = await database.CanonicalArticleReviewRuns.AsNoTracking()
                .Where(run => run.BaseAnalysisRunId == captured.Id && run.Language == language &&
                    run.PolicyVersion == policyVersion && run.SettingsFingerprint == configuration.SettingsFingerprint &&
                    database.CanonicalResearcherWorks.Any(association =>
                        association.CanonicalWorkId == canonicalWorkId && association.PersonelId == personelId))
                .OrderByDescending(run => run.Id)
                .FirstOrDefaultAsync(total.Token);
            if (reusable is not null)
            {
                long? finalBaseId = await LoadLatestBaseIdAsync(personelId, canonicalWorkId, language, total.Token);
                if (finalBaseId is null)
                    throw new ArticleReviewUnavailableException("The researcher association is no longer current.");
                if (finalBaseId != captured.Id)
                    throw new ArticleReviewSourceChangedException();
                return Map(reusable, personelId, true, false, []);
            }
        }
        ArticleReviewWorkItem workItem = resumable ?? await GetOrCreateWorkItemAsync(captured, request, configuration,
            forceRegeneration ? Guid.NewGuid() : Guid.Empty, total.Token);
        ArticleReviewReport report = await ExecuteStagesAsync(personelId, captured, request, configuration,
            workItem, total.Token);
        CanonicalArticleReviewRun saved = await PersistAsync(
            personelId, canonicalWorkId, captured.Id, captured.ArticleSourceSnapshotId,
            configuration.SettingsFingerprint, workItem.Id, request, report, total.Token);
        return Map(saved, personelId, false, false, []);
    }

    public async Task<CanonicalArticleReviewResponse?> GetLatestAsync(
        string personelId,
        int canonicalWorkId,
        string language,
        CancellationToken cancellationToken)
    {
        CanonicalArticleReviewRun? run = await database.CanonicalArticleReviewRuns.AsNoTracking()
            .Where(value => value.CanonicalWorkId == canonicalWorkId && value.Language == language &&
                database.CanonicalResearcherWorks.Any(association =>
                    association.CanonicalWorkId == canonicalWorkId && association.PersonelId == personelId))
            .OrderByDescending(value => value.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (run is null)
            return null;
        long? currentBaseId = await database.CanonicalArticleAnalysisRuns.AsNoTracking()
            .Where(value => value.CanonicalWorkId == canonicalWorkId && value.Language == language &&
                database.CanonicalResearcherWorks.Any(association =>
                    association.CanonicalWorkId == canonicalWorkId && association.PersonelId == personelId))
            .OrderByDescending(value => value.Id)
            .Select(value => (long?)value.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (!await HasAssociationAsync(personelId, canonicalWorkId, cancellationToken))
            return null;
        List<string> reasons = [];
        if (currentBaseId != run.BaseAnalysisRunId)
            reasons.Add("A newer canonical article analysis is available.");
        if (run.PolicyVersion != options.Value.PolicyVersion.Trim())
            reasons.Add("The configured article review policy has changed.");
        return Map(run, personelId, false, reasons.Count > 0, reasons);
    }

    private async Task<ArticleReviewWorkItem?> FindResumableAsync(CanonicalArticleAnalysisRun captured,
        ReviewArticleRequest request, ArticleReviewRuntimeConfiguration configuration,
        CancellationToken cancellationToken)
    {
        ArticleReviewWorkItem? workItem = await database.ArticleReviewWorkItems
            .Where(value => value.BaseAnalysisRunId == captured.Id &&
                value.ArticleSourceSnapshotId == captured.ArticleSourceSnapshotId &&
                value.Language == request.Language && value.PolicyVersion == request.PolicyVersion &&
                value.SettingsFingerprint == configuration.SettingsFingerprint)
            .OrderByDescending(value => value.Id).FirstOrDefaultAsync(cancellationToken);
        if (workItem is null || workItem.Status == "Completed") return null;
        if (workItem.SourceSnapshotJson != JsonSerializer.Serialize(request, JsonOptions) ||
            workItem.ConfigurationJson != JsonSerializer.Serialize(configuration, JsonOptions))
            throw new ArticleReviewUnavailableException("The saved article review checkpoint identity is invalid.");
        return workItem;
    }

    private async Task<ArticleReviewWorkItem> GetOrCreateWorkItemAsync(
        CanonicalArticleAnalysisRun captured,
        ReviewArticleRequest request,
        ArticleReviewRuntimeConfiguration configuration,
        Guid generationNonce,
        CancellationToken cancellationToken)
    {
        string sourceJson = JsonSerializer.Serialize(request, JsonOptions);
        string configurationJson = JsonSerializer.Serialize(configuration, JsonOptions);
        string workKey = Hash(JsonSerializer.SerializeToUtf8Bytes(new
        {
            captured.Id,
            captured.CanonicalWorkId,
            captured.ArticleSourceSnapshotId,
            request.Language,
            request.SourceHash,
            request.ExtractionVersion,
            request.PolicyVersion,
            configuration.SettingsFingerprint,
            generationNonce
        }, JsonOptions));
        ArticleReviewWorkItem? existing = await database.ArticleReviewWorkItems
            .Include(value => value.Checkpoints)
            .SingleOrDefaultAsync(value => value.WorkKey == workKey, cancellationToken);
        if (existing is not null)
        {
            if (existing.SourceSnapshotJson != sourceJson || existing.ConfigurationJson != configurationJson ||
                existing.BaseAnalysisRunId != captured.Id ||
                existing.ArticleSourceSnapshotId != captured.ArticleSourceSnapshotId)
                throw new ArticleReviewUnavailableException("The saved article review checkpoint identity is invalid.");
            return existing;
        }
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ArticleReviewWorkItem created = new()
        {
            WorkKey = workKey,
            CanonicalWorkId = captured.CanonicalWorkId,
            BaseAnalysisRunId = captured.Id,
            ArticleSourceSnapshotId = captured.ArticleSourceSnapshotId,
            Language = request.Language,
            PolicyVersion = request.PolicyVersion,
            SettingsFingerprint = configuration.SettingsFingerprint,
            GenerationNonce = generationNonce,
            MaximumCalls = options.Value.MaximumProviderCalls,
            MaximumSpendUsd = options.Value.MaximumSpendUsd,
            ConfigurationJson = configurationJson,
            SourceSnapshotJson = sourceJson,
            CreatedAt = now,
            UpdatedAt = now
        };
        database.ArticleReviewWorkItems.Add(created);
        await database.SaveChangesAsync(cancellationToken);
        return created;
    }

    private async Task<ArticleReviewReport> ExecuteStagesAsync(
        string personelId,
        CanonicalArticleAnalysisRun captured,
        ReviewArticleRequest request,
        ArticleReviewRuntimeConfiguration configuration,
        ArticleReviewWorkItem workItem,
        CancellationToken cancellationToken)
    {
        List<(ArticleReviewGenerationStageResult Generation, VerificationOutcome Verification)> completed = [];
        foreach (string role in ArticleReviewerRoles)
        {
            ArticleReviewGenerationStageResult generation = await GenerateRoleAsync(personelId, captured,
                request, configuration, workItem, role, cancellationToken);
            VerificationOutcome verification = generation.Findings.Count == 0
                ? new([], [configuration.VerificationModel])
                : await VerifyBatchAdaptiveAsync(personelId, captured, request, configuration, workItem,
                    role, generation.Findings, null, 0, cancellationToken);
            completed.Add((generation, verification));
        }
        return AssembleReport(request, configuration, completed);
    }

    private async Task<ArticleReviewGenerationStageResult> GenerateRoleAsync(
        string personelId, CanonicalArticleAnalysisRun captured, ReviewArticleRequest source,
        ArticleReviewRuntimeConfiguration configuration, ArticleReviewWorkItem workItem, string role,
        CancellationToken cancellationToken)
    {
        string batchKey = Hash(Encoding.UTF8.GetBytes($"generation:{role}"));
        ArticleReviewStageQuoteRequest quoteRequest = new(ArticleReviewStageKinds.Generation, role, source);
        ArticleReviewStageCheckpoint checkpoint = await GetOrCreateCheckpointAsync(workItem.Id,
            ArticleReviewStageKinds.Generation, role, batchKey, null, 0, quoteRequest, cancellationToken);
        if (checkpoint.Status == "Completed")
            return ReadGeneration(checkpoint, source, configuration);
        if (checkpoint.Status == "OutputLimit")
            return await RecoverGenerationAsync(personelId, captured, source, configuration, workItem,
                role, checkpoint, batchKey, cancellationToken);
        EnsureDispatchable(checkpoint);
        await ValidateCurrentAsync(personelId, captured, source, cancellationToken);
        ArticleReviewStageQuote quote = await client.QuoteAsync(quoteRequest, cancellationToken);
        await AdmitAsync(workItem, checkpoint, quote, cancellationToken);
        ArticleReviewStageDispatchRequest dispatch = new(checkpoint.AttemptId!.Value,
            quote.SettingsFingerprint, quote.RequestFingerprint, quote.MaximumChargeUsd, role, source);
        try
        {
            ArticleReviewGenerationStageResult result = await client.GenerateAsync(dispatch, cancellationToken);
            ValidateGenerationResult(checkpoint, result, source, configuration, false);
            Complete(checkpoint, result.Attempt, JsonSerializer.Serialize(result, JsonOptions), configuration);
            await database.SaveChangesAsync(cancellationToken);
            return ReadGeneration(checkpoint, source, configuration);
        }
        catch (ArticleReviewAnalysisException exception)
        {
            await FailAsync(checkpoint, exception, configuration, cancellationToken);
            if (checkpoint.Status == "OutputLimit")
                return await RecoverGenerationAsync(personelId, captured, source, configuration, workItem,
                    role, checkpoint, batchKey, cancellationToken);
            throw;
        }
    }

    private async Task<ArticleReviewGenerationStageResult> RecoverGenerationAsync(
        string personelId, CanonicalArticleAnalysisRun captured, ReviewArticleRequest source,
        ArticleReviewRuntimeConfiguration configuration, ArticleReviewWorkItem workItem, string role,
        ArticleReviewStageCheckpoint initial, string parentBatchKey, CancellationToken cancellationToken)
    {
        if (configuration.GenerationRecoveryPolicyVersion != ArticleReviewGenerationRecovery.PolicyVersion ||
            configuration.GenerationRecoveryThinkingLevel != ArticleReviewGenerationRecovery.RecoveryThinkingLevel ||
            configuration.GenerationThinkingLevel != ArticleReviewGenerationRecovery.InitialThinkingLevel ||
            initial.Status != "OutputLimit" || initial.ErrorCode != "output_limit" ||
            initial.AttemptId is null || initial.ActualCostUsd is null or < 0 ||
            initial.PricingVersion != configuration.PricingVersion || initial.BatchKey != parentBatchKey ||
            initial.ParentBatchKey is not null || initial.Ordinal != 0)
            throw new ArticleReviewUnavailableException(
                "The saved article review generation cannot authorize a bounded recovery.");

        Guid recoveryOfAttemptId = initial.AttemptId.Value;
        string recoveryBatchKey = Hash(Encoding.UTF8.GetBytes($"generation:{role}:medium"));
        ArticleReviewStageQuoteRequest quoteRequest = new(ArticleReviewStageKinds.Generation, role, source)
        {
            GenerationThinkingLevel = ArticleReviewGenerationRecovery.RecoveryThinkingLevel,
            RecoveryOfAttemptId = recoveryOfAttemptId
        };
        ArticleReviewStageCheckpoint checkpoint = await GetOrCreateCheckpointAsync(workItem.Id,
            ArticleReviewStageKinds.Generation, role, recoveryBatchKey, parentBatchKey, 1,
            quoteRequest, cancellationToken);
        if (checkpoint.Status == "Completed")
            return ReadGeneration(checkpoint, source, configuration);
        EnsureDispatchable(checkpoint);
        await ValidateCurrentAsync(personelId, captured, source, cancellationToken);
        ArticleReviewStageQuote quote = await client.QuoteAsync(quoteRequest, cancellationToken);
        await ValidateCurrentAsync(personelId, captured, source, cancellationToken);
        await AdmitAsync(workItem, checkpoint, quote, cancellationToken);
        ArticleReviewStageDispatchRequest dispatch = new(checkpoint.AttemptId!.Value,
            quote.SettingsFingerprint, quote.RequestFingerprint, quote.MaximumChargeUsd, role, source)
        {
            GenerationThinkingLevel = ArticleReviewGenerationRecovery.RecoveryThinkingLevel,
            RecoveryOfAttemptId = recoveryOfAttemptId
        };
        try
        {
            ArticleReviewGenerationStageResult result = await client.GenerateAsync(dispatch, cancellationToken);
            ValidateGenerationResult(checkpoint, result, source, configuration, false);
            Complete(checkpoint, result.Attempt, JsonSerializer.Serialize(result, JsonOptions), configuration);
            await database.SaveChangesAsync(cancellationToken);
            return ReadGeneration(checkpoint, source, configuration);
        }
        catch (ArticleReviewAnalysisException exception)
        {
            await FailAsync(checkpoint, exception, configuration, cancellationToken);
            throw;
        }
    }

    private async Task<VerificationOutcome> VerifyBatchAdaptiveAsync(
        string personelId, CanonicalArticleAnalysisRun captured, ReviewArticleRequest source,
        ArticleReviewRuntimeConfiguration configuration, ArticleReviewWorkItem workItem, string role,
        IReadOnlyList<ArticleReviewCandidateFinding> findings, string? parentBatchKey, int ordinal,
        CancellationToken cancellationToken)
    {
        string batchKey = Hash(JsonSerializer.SerializeToUtf8Bytes(
            findings.Select(value => value.FindingId).ToList(), JsonOptions));
        ArticleReviewStageQuoteRequest quoteRequest = new(ArticleReviewStageKinds.Verification, role, source)
        { Findings = findings };
        ArticleReviewStageCheckpoint checkpoint = await GetOrCreateCheckpointAsync(workItem.Id,
            ArticleReviewStageKinds.Verification, role, batchKey, parentBatchKey, ordinal,
            quoteRequest, cancellationToken);
        if (checkpoint.Status == "Completed")
        {
            ArticleReviewVerificationStageResult saved = ReadVerification(checkpoint, findings, configuration);
            return new(saved.Verdicts, [saved.Model]);
        }
        if (checkpoint.Status == "OutputLimit")
            return await SplitVerificationAsync(personelId, captured, source, configuration, workItem,
                role, findings, batchKey, cancellationToken);
        EnsureDispatchable(checkpoint);
        await ValidateCurrentAsync(personelId, captured, source, cancellationToken);
        ArticleReviewStageQuote quote = await client.QuoteAsync(quoteRequest, cancellationToken);
        await AdmitAsync(workItem, checkpoint, quote, cancellationToken);
        ArticleReviewStageDispatchRequest dispatch = new(checkpoint.AttemptId!.Value,
            quote.SettingsFingerprint, quote.RequestFingerprint, quote.MaximumChargeUsd, role, source)
        { Findings = findings };
        try
        {
            ArticleReviewVerificationStageResult result = await client.VerifyAsync(dispatch, cancellationToken);
            ValidateVerificationResult(checkpoint, result, findings, configuration, false);
            Complete(checkpoint, result.Attempt, JsonSerializer.Serialize(result, JsonOptions), configuration);
            await database.SaveChangesAsync(cancellationToken);
            return new(result.Verdicts, [result.Model]);
        }
        catch (ArticleReviewAnalysisException exception)
        {
            await FailAsync(checkpoint, exception, configuration, cancellationToken);
            if (checkpoint.Status != "OutputLimit") throw;
            return await SplitVerificationAsync(personelId, captured, source, configuration, workItem,
                role, findings, batchKey, cancellationToken);
        }
    }

    private async Task<VerificationOutcome> SplitVerificationAsync(
        string personelId, CanonicalArticleAnalysisRun captured, ReviewArticleRequest source,
        ArticleReviewRuntimeConfiguration configuration, ArticleReviewWorkItem workItem, string role,
        IReadOnlyList<ArticleReviewCandidateFinding> findings, string parentBatchKey,
        CancellationToken cancellationToken)
    {
        if (findings.Count == 1)
            throw new ArticleReviewUnavailableException(
                "A singleton article review verification exhausted the provider output limit.");
        int middle = findings.Count / 2;
        VerificationOutcome first = await VerifyBatchAdaptiveAsync(personelId,
            captured, source, configuration, workItem, role, findings.Take(middle).ToList(),
            parentBatchKey, 0, cancellationToken);
        VerificationOutcome second = await VerifyBatchAdaptiveAsync(personelId,
            captured, source, configuration, workItem, role, findings.Skip(middle).ToList(),
            parentBatchKey, 1, cancellationToken);
        return new(first.Verdicts.Concat(second.Verdicts).ToList(),
            first.Models.Concat(second.Models).Distinct(StringComparer.Ordinal).ToList());
    }

    private async Task<ArticleReviewStageCheckpoint> GetOrCreateCheckpointAsync(
        long workItemId, string stage, string role, string batchKey, string? parentBatchKey, int ordinal,
        ArticleReviewStageQuoteRequest request, CancellationToken cancellationToken)
    {
        ArticleReviewStageCheckpoint? checkpoint = await database.ArticleReviewStageCheckpoints.SingleOrDefaultAsync(
            value => value.ArticleReviewWorkItemId == workItemId && value.Stage == stage &&
                value.Role == role && value.BatchKey == batchKey, cancellationToken);
        string requestJson = request.GenerationThinkingLevel is null && request.RecoveryOfAttemptId is null
            ? JsonSerializer.Serialize(new
            {
                request.Stage,
                request.Role,
                request.Source.Language,
                request.Source.SourceKind,
                request.Source.SourceHash,
                request.Source.ExtractionVersion,
                request.Source.PolicyVersion,
                request.Findings
            }, JsonOptions)
            : JsonSerializer.Serialize(new
        {
            request.Stage,
            request.Role,
            request.Source.Language,
            request.Source.SourceKind,
            request.Source.SourceHash,
            request.Source.ExtractionVersion,
            request.Source.PolicyVersion,
            request.Findings,
            request.GenerationThinkingLevel,
            request.RecoveryOfAttemptId
        }, JsonOptions);
        if (checkpoint is not null)
        {
            if (checkpoint.RequestJson != requestJson || checkpoint.ParentBatchKey != parentBatchKey ||
                checkpoint.Ordinal != ordinal)
                throw new ArticleReviewUnavailableException("The saved article review stage identity is invalid.");
            return checkpoint;
        }
        DateTimeOffset now = DateTimeOffset.UtcNow;
        checkpoint = new()
        {
            ArticleReviewWorkItemId = workItemId, Stage = stage, Role = role, BatchKey = batchKey,
            ParentBatchKey = parentBatchKey, Ordinal = ordinal, RequestJson = requestJson,
            CreatedAt = now, UpdatedAt = now
        };
        database.ArticleReviewStageCheckpoints.Add(checkpoint);
        await database.SaveChangesAsync(cancellationToken);
        return checkpoint;
    }

    private async Task AdmitAsync(ArticleReviewWorkItem workItem, ArticleReviewStageCheckpoint checkpoint,
        ArticleReviewStageQuote quote, CancellationToken cancellationToken)
    {
        if (quote.SettingsFingerprint != workItem.SettingsFingerprint || quote.MaximumChargeUsd < 0)
            throw new ArticleReviewUnavailableException("The article review model settings changed before dispatch.");
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        List<ArticleReviewStageCheckpoint> attempted = await database.ArticleReviewStageCheckpoints.AsNoTracking()
            .Where(value => value.ArticleReviewWorkItemId == workItem.Id && value.AttemptId != null)
            .ToListAsync(cancellationToken);
        if (attempted.Any(value => value.ReservedCostUsd is null || value.ReservedCostUsd < 0 ||
                value.ActualCostUsd < 0 || value.ActualCostUsd > value.ReservedCostUsd))
            throw new ArticleReviewUnavailableException("The cumulative article review provider budget is indeterminate.");
        decimal charged = attempted.Sum(value => value.ActualCostUsd ?? value.ReservedCostUsd!.Value);
        if (attempted.Count >= workItem.MaximumCalls || charged + quote.MaximumChargeUsd > workItem.MaximumSpendUsd)
            throw new ArticleReviewUnavailableException("The cumulative article review provider budget is exhausted.");
        Guid attemptId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        int updated = await database.ArticleReviewStageCheckpoints
            .Where(value => value.Id == checkpoint.Id && value.Status == "Pending" && value.AttemptId == null)
            .ExecuteUpdateAsync(update => update
                .SetProperty(value => value.AttemptId, attemptId)
                .SetProperty(value => value.RequestFingerprint, quote.RequestFingerprint)
                .SetProperty(value => value.ReservedCostUsd, quote.MaximumChargeUsd)
                .SetProperty(value => value.Status, "Dispatched")
                .SetProperty(value => value.UpdatedAt, now), cancellationToken);
        if (updated != 1)
            throw new ArticleReviewBusyException();
        await transaction.CommitAsync(cancellationToken);
        await database.Entry(checkpoint).ReloadAsync(cancellationToken);
    }

    private static void Complete(ArticleReviewStageCheckpoint checkpoint,
        ArticleReviewProviderAttempt attempt, string resultJson, ArticleReviewRuntimeConfiguration configuration)
    {
        if (attempt.AttemptId != checkpoint.AttemptId || attempt.Outcome != "Success" ||
            attempt.EstimatedCostUsd is null or < 0 || checkpoint.ReservedCostUsd is null ||
            attempt.EstimatedCostUsd > checkpoint.ReservedCostUsd ||
            attempt.PricingVersion != configuration.PricingVersion || string.IsNullOrWhiteSpace(attempt.ReturnedModel))
        {
            checkpoint.Status = "Unknown";
            checkpoint.ErrorCode = "invalid_attempt_attestation";
            checkpoint.UpdatedAt = DateTimeOffset.UtcNow;
            throw new ArticleReviewUnavailableException("The provider attempt could not be safely attributed.");
        }
        checkpoint.ActualCostUsd = attempt.EstimatedCostUsd;
        checkpoint.PricingVersion = attempt.PricingVersion;
        checkpoint.ResultJson = resultJson;
        checkpoint.Status = "Completed";
        checkpoint.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private async Task FailAsync(ArticleReviewStageCheckpoint checkpoint,
        ArticleReviewAnalysisException exception, ArticleReviewRuntimeConfiguration configuration,
        CancellationToken cancellationToken)
    {
        ArticleReviewProviderAttempt? attempt = exception.ProviderAttempt;
        bool attributed = attempt is not null && attempt.AttemptId == checkpoint.AttemptId &&
            attempt.EstimatedCostUsd is >= 0 && checkpoint.ReservedCostUsd.HasValue &&
            attempt.EstimatedCostUsd <= checkpoint.ReservedCostUsd &&
            attempt.PricingVersion == configuration.PricingVersion &&
            ModelCompatible(attempt.ReturnedModel, checkpoint.Stage == ArticleReviewStageKinds.Generation
                ? configuration.GenerationModel : configuration.VerificationModel);
        checkpoint.ActualCostUsd = attributed ? attempt!.EstimatedCostUsd : null;
        checkpoint.PricingVersion = attributed ? attempt!.PricingVersion : null;
        checkpoint.ErrorCode = exception.ErrorCode;
        checkpoint.Status = exception.ErrorCode == "output_limit" && attempt?.Outcome == "OutputLimit" && attributed
            ? "OutputLimit" :
            attributed ? "Failed" : "Unknown";
        checkpoint.UpdatedAt = DateTimeOffset.UtcNow;
        using CancellationTokenSource saveTimeout = new(TimeSpan.FromSeconds(5));
        await database.SaveChangesAsync(saveTimeout.Token);
    }

    private async Task ValidateCurrentAsync(string personelId, CanonicalArticleAnalysisRun captured,
        ReviewArticleRequest source, CancellationToken cancellationToken)
    {
        CanonicalArticleAnalysisRun? current = await LoadLatestBaseAsync(personelId, captured.CanonicalWorkId,
            source.Language, cancellationToken);
        if (current is null || current.Id != captured.Id ||
            JsonSerializer.Serialize(CreateRequest(current, source.PolicyVersion), JsonOptions) !=
            JsonSerializer.Serialize(source, JsonOptions))
            throw new ArticleReviewSourceChangedException();
    }

    private static void EnsureDispatchable(ArticleReviewStageCheckpoint checkpoint)
    {
        if (checkpoint.Status != "Pending" || checkpoint.AttemptId.HasValue)
            throw new ArticleReviewUnavailableException(
                "A previous article review provider dispatch has an unresolved or failed outcome.");
    }

    private static ArticleReviewGenerationStageResult ReadGeneration(ArticleReviewStageCheckpoint checkpoint,
        ReviewArticleRequest source, ArticleReviewRuntimeConfiguration configuration)
    {
        ArticleReviewGenerationStageResult result = JsonSerializer.Deserialize<ArticleReviewGenerationStageResult>(
            checkpoint.ResultJson!, JsonOptions) ?? throw new JsonException("The generation checkpoint is invalid.");
        ValidateGenerationResult(checkpoint, result, source, configuration, true);
        return result;
    }

    private static void ValidateGenerationResult(ArticleReviewStageCheckpoint checkpoint,
        ArticleReviewGenerationStageResult result, ReviewArticleRequest source,
        ArticleReviewRuntimeConfiguration configuration, bool persisted)
    {
        if (result.Role != checkpoint.Role || result.Findings is null || result.Findings.Count > 3 ||
            result.Findings.Any(value => !ValidCandidate(checkpoint.Role, value, source.SourceSpans!)) ||
            result.Findings.Select(value => value.FindingId).Distinct(StringComparer.Ordinal).Count() !=
                result.Findings.Count || !Bounded(result.Model, 200) ||
            result.PromptVersion != configuration.GenerationPromptVersion ||
            result.Attempt is null ||
            result.Model != result.Attempt.ReturnedModel ||
            !ModelCompatible(result.Model, configuration.GenerationModel) ||
            !ValidAttempt(checkpoint, result.Attempt, configuration, persisted))
            throw new JsonException("The generation checkpoint prompt does not match its configuration.");
    }

    private static ArticleReviewVerificationStageResult ReadVerification(
        ArticleReviewStageCheckpoint checkpoint, IReadOnlyList<ArticleReviewCandidateFinding> candidates,
        ArticleReviewRuntimeConfiguration configuration)
    {
        ArticleReviewVerificationStageResult result = JsonSerializer.Deserialize<ArticleReviewVerificationStageResult>(
            checkpoint.ResultJson!, JsonOptions) ?? throw new JsonException("The verification checkpoint is invalid.");
        ValidateVerificationResult(checkpoint, result, candidates, configuration, true);
        return result;
    }

    private static void ValidateVerificationResult(ArticleReviewStageCheckpoint checkpoint,
        ArticleReviewVerificationStageResult result, IReadOnlyList<ArticleReviewCandidateFinding> candidates,
        ArticleReviewRuntimeConfiguration configuration, bool persisted)
    {
        if (result.Verdicts is null || result.Verdicts.Count != candidates.Count ||
            result.Verdicts.Any(value => value is null) ||
            result.Verdicts.Select(value => value.FindingId).Distinct(StringComparer.Ordinal).Count() !=
                candidates.Count || result.Verdicts.Any(value =>
                value is null || !candidates.Any(candidate => candidate.FindingId == value.FindingId) ||
                value.Verdict is not ("supported" or "unsupported" or "uncertain") ||
                !Bounded(value.Reason, 500)) || !Bounded(result.Model, 200) ||
            result.PromptVersion != configuration.VerificationPromptVersion ||
            result.Attempt is null ||
            result.Model != result.Attempt.ReturnedModel ||
            !ModelCompatible(result.Model, configuration.VerificationModel) ||
            !ValidAttempt(checkpoint, result.Attempt, configuration, persisted))
            throw new JsonException("The verification checkpoint prompt does not match its configuration.");
    }

    private static bool ValidCandidate(string role, ArticleReviewCandidateFinding value,
        IReadOnlyList<ArticleSourceSpan> sourceSpans) =>
        value is not null && value.Role == role &&
        (role == "teaching"
            ? value.Kind is "teaching_adaptation" or "review_question"
            : value.Kind is "source_observation" or "review_question") &&
        Bounded(value.FindingId, 100) && value.FindingId == value.FindingId.Trim() &&
        Bounded(value.Basis, 1200) && value.SourceIds is not null && value.SourceIds.Count is > 0 and <= 2 &&
        value.SourceIds.Distinct(StringComparer.Ordinal).Count() == value.SourceIds.Count &&
        value.SourceIds.All(id => sourceSpans.Any(span => span.SourceId == id &&
            !string.IsNullOrWhiteSpace(span.Text))) &&
        (value.Kind == "source_observation" ? value.Suggestion is null : Bounded(value.Suggestion, 1200));

    private static bool ValidAttempt(ArticleReviewStageCheckpoint checkpoint,
        ArticleReviewProviderAttempt attempt, ArticleReviewRuntimeConfiguration configuration, bool persisted) =>
        attempt is not null && attempt.AttemptId == checkpoint.AttemptId && attempt.Outcome == "Success" &&
        (!persisted || attempt.EstimatedCostUsd == checkpoint.ActualCostUsd) && attempt.EstimatedCostUsd is >= 0 &&
        attempt.EstimatedCostUsd <= checkpoint.ReservedCostUsd &&
        (!persisted || attempt.PricingVersion == checkpoint.PricingVersion) &&
        attempt.PricingVersion == configuration.PricingVersion && Bounded(attempt.ReturnedModel, 200);

    private static bool Bounded(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum;

    private static bool ModelCompatible(string? returnedModel, string configuredModel) =>
        Bounded(returnedModel, 200) && string.Equals(returnedModel, configuredModel, StringComparison.Ordinal);

    private static ArticleReviewReport AssembleReport(ReviewArticleRequest request,
        ArticleReviewRuntimeConfiguration configuration,
        IReadOnlyList<(ArticleReviewGenerationStageResult Generation, VerificationOutcome Verification)> completed)
    {
        if (completed.Count != ArticleReviewerRoles.Length ||
            !completed.Select(value => value.Generation.Role).SequenceEqual(ArticleReviewerRoles))
            throw new JsonException("All four specialist roles must complete before a report is assembled.");
        List<ArticleSpecialistReview> reviews = [];
        List<string> omissionReasons = [];
        int unsupported = 0, uncertain = 0, candidates = 0;
        foreach ((ArticleReviewGenerationStageResult generation,
                     VerificationOutcome verification) in completed)
        {
            IReadOnlyList<ArticleReviewVerificationVerdict> verdicts = verification.Verdicts;
            candidates += generation.Findings.Count;
            if (verdicts.Count != generation.Findings.Count) throw new JsonException("Verification is incomplete.");
            List<ArticleReviewFinding> supported = [];
            foreach (ArticleReviewCandidateFinding finding in generation.Findings)
            {
                ArticleReviewVerificationVerdict verdict = verdicts.Single(value => value.FindingId == finding.FindingId);
                if (verdict.Verdict != "supported")
                {
                    if (verdict.Verdict == "unsupported") unsupported++; else uncertain++;
                    if (omissionReasons.Count < 20)
                        omissionReasons.Add($"{generation.Role}/{finding.FindingId} {verdict.Verdict}: {verdict.Reason}");
                    continue;
                }
                List<ArticleReviewEvidence> evidence = finding.SourceIds.Select(sourceId =>
                {
                    ArticleSourceSpan span = request.SourceSpans!.Single(value => value.SourceId == sourceId);
                    return new ArticleReviewEvidence(span.SourceId, span.PageNumber, span.StartOffset,
                        span.EndOffset, span.Text);
                }).ToList();
                supported.Add(new($"{generation.Role}:{finding.FindingId}", generation.Role, finding.Kind,
                    finding.Basis, finding.Suggestion, evidence));
            }
            reviews.Add(new(generation.Role,
                supported.Count == 0 ? "no_supported_findings" : "automatically_checked", supported));
        }
        int supportedCount = reviews.Sum(value => value.Findings.Count);
        string outcome = supportedCount == 0 ? "no_supported_findings" : "automatically_checked";
        return new(request.Language, request.SourceKind, request.SourceHash, request.ExtractionVersion,
            request.PolicyVersion, outcome,
            new(request.Pages.Count, request.Pages.Count(page => !string.IsNullOrWhiteSpace(page.Text)),
                request.TotalSourcePages, request.IsPartial, request.ScopeReason),
            new(4, 4, candidates, candidates, supportedCount, unsupported, uncertain,
                unsupported + uncertain, omissionReasons), reviews,
            string.Join(",", completed.Select(value => value.Generation.Model).Distinct(StringComparer.Ordinal)),
            configuration.GenerationPromptVersion,
            new(outcome, string.Join(",", completed.SelectMany(value => value.Verification.Models)
                    .Distinct(StringComparer.Ordinal)),
                configuration.VerificationPromptVersion,
                configuration.Provider == "Gemini" || configuration.GenerationModel == configuration.VerificationModel,
                "Automatic checking does not prove that the article is flawless or that every possible issue was found."))
        {
            SourceFidelity = ArticleSourceFidelity.Create(request.SourceKind, request.ExtractionVersion)
        };
    }

    private static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static readonly string[] ArticleReviewerRoles =
        ["method", "quantitative", "claim_evidence", "teaching"];

    private sealed record VerificationOutcome(
        IReadOnlyList<ArticleReviewVerificationVerdict> Verdicts,
        IReadOnlyList<string> Models);

    private async Task<CanonicalArticleReviewRun> PersistAsync(
        string personelId,
        int canonicalWorkId,
        long capturedBaseAnalysisRunId,
        long sourceSnapshotId,
        string settingsFingerprint,
        long workItemId,
        ReviewArticleRequest request,
        ArticleReviewReport report,
        CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        await sourceLock.AcquireWriteGateAsync(cancellationToken);
        CanonicalArticleAnalysisRun? current = await LoadLatestBaseAsync(personelId, canonicalWorkId,
            report.Language, cancellationToken);
        ReviewArticleRequest? currentRequest = null;
        if (current is not null)
        {
            try { currentRequest = CreateRequest(current, request.PolicyVersion); }
            catch (ArticleReviewUnavailableException) { }
        }
        if (current is null || current.Id != capturedBaseAnalysisRunId || currentRequest is null ||
            JsonSerializer.Serialize(currentRequest, JsonOptions) != JsonSerializer.Serialize(request, JsonOptions))
            throw new ArticleReviewSourceChangedException();
        ArticleReviewServiceClient.Validate(report, request);
        Dictionary<string, ArticleSourceSpanSnapshot> spans = await database.ArticleSourceSpans
            .Where(span => span.ArticleSourceSnapshotId == sourceSnapshotId)
            .ToDictionaryAsync(span => span.SourceId, StringComparer.Ordinal, cancellationToken);

        CanonicalArticleReviewRun run = CreateRun(
            canonicalWorkId, capturedBaseAnalysisRunId, sourceSnapshotId, settingsFingerprint, report, spans);
        database.CanonicalArticleReviewRuns.Add(run);
        ArticleReviewWorkItem workItem = await database.ArticleReviewWorkItems.SingleAsync(
            value => value.Id == workItemId, cancellationToken);
        workItem.Status = "Completed";
        workItem.UpdatedAt = DateTimeOffset.UtcNow;
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return run;
    }

    private async Task<CanonicalArticleAnalysisRun?> LoadLatestBaseAsync(
        string personelId,
        int canonicalWorkId,
        string language,
        CancellationToken cancellationToken)
    {
        string? sourceIdentityHash = await CanonicalSourceIdentity.LoadAsync(
            database, canonicalWorkId, cancellationToken);
        if (sourceIdentityHash is null)
            return null;
        return await database.CanonicalArticleAnalysisRuns.AsNoTracking()
            .Include(run => run.SavedArticleSummary)
            .Include(run => run.ArticleSourceSnapshot)!.ThenInclude(snapshot => snapshot!.Pages)
            .Include(run => run.ArticleSourceSnapshot)!.ThenInclude(snapshot => snapshot!.Spans)
            .Where(run => run.CanonicalWorkId == canonicalWorkId && run.Language == language &&
                run.SourceIdentityHash == sourceIdentityHash &&
                database.CanonicalResearcherWorks.Any(association =>
                    association.CanonicalWorkId == canonicalWorkId && association.PersonelId == personelId))
            .OrderByDescending(run => run.Id)
            .AsSplitQuery()
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<long?> LoadLatestBaseIdAsync(
        string personelId,
        int canonicalWorkId,
        string language,
        CancellationToken cancellationToken)
    {
        string? sourceIdentityHash = await CanonicalSourceIdentity.LoadAsync(
            database, canonicalWorkId, cancellationToken);
        if (sourceIdentityHash is null)
            return null;
        return await database.CanonicalArticleAnalysisRuns.AsNoTracking()
            .Where(run => run.CanonicalWorkId == canonicalWorkId && run.Language == language &&
                run.SourceIdentityHash == sourceIdentityHash &&
                database.CanonicalResearcherWorks.Any(association =>
                    association.CanonicalWorkId == canonicalWorkId && association.PersonelId == personelId))
            .OrderByDescending(run => run.Id)
            .Select(run => (long?)run.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static ReviewArticleRequest CreateRequest(CanonicalArticleAnalysisRun run, string policyVersion)
    {
        if (run.ArticleSourceSnapshot is null || run.SavedArticleSummary is null)
            throw new ArticleReviewUnavailableException("The saved article source is incomplete.");
        SummarizeArticleRequest? original;
        try
        {
            original = JsonSerializer.Deserialize<SummarizeArticleRequest>(
                run.SavedArticleSummary.SnapshotJson, JsonOptions);
        }
        catch (JsonException)
        {
            throw new ArticleReviewUnavailableException("The saved article source coverage is unusable.");
        }
        if (original is null)
            throw new ArticleReviewUnavailableException("The saved article source coverage is unavailable.");
        List<ArticlePage> pages = run.ArticleSourceSnapshot.Pages.OrderBy(page => page.Ordinal)
            .Select(page => new ArticlePage(page.PageNumber, page.Text)).ToList();
        List<ArticleSourceSpan> spans = run.ArticleSourceSnapshot.Spans.OrderBy(span => span.Ordinal)
            .Select(span => new ArticleSourceSpan(span.SourceId, span.PageNumber,
                span.StartOffset, span.EndOffset, span.Text)).ToList();
        string calculatedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(pages, JsonOptions)))).ToLowerInvariant();
        if (original.Language != run.Language || original.SourceHash != calculatedHash ||
            original.SourceHash != run.ArticleSourceSnapshot.ExtractedTextHash ||
            original.SourceKind != run.ArticleSourceSnapshot.SourceKind ||
            original.ExtractionVersion != run.ArticleSourceSnapshot.ExtractionVersion ||
            original.Pages is null || !original.Pages.SequenceEqual(pages) ||
            original.SourceSpans is null || !original.SourceSpans.SequenceEqual(spans) ||
            original.TotalSourcePages < pages.Count || original.TotalSourcePages <= 0 ||
            original.TotalSourcePages > pages.Count && !original.IsPartial ||
            original.SourceKind == "abstract" && !original.IsPartial ||
            original.IsPartial && string.IsNullOrWhiteSpace(original.ScopeReason) ||
            original.ScopeReason?.Length > 4000 ||
            !ArticleSourceCatalog.IsValid(pages, spans, original.SourceKind))
            throw new ArticleReviewUnavailableException("The saved article source does not match its immutable catalog.");
        return new(run.Language,
            original.SourceKind, original.SourceHash, original.ExtractionVersion, policyVersion,
            pages, original.TotalSourcePages, original.IsPartial, original.ScopeReason)
        {
            SourceSpans = spans
        };
    }

    private static CanonicalArticleReviewRun CreateRun(
        int canonicalWorkId,
        long baseAnalysisRunId,
        long sourceSnapshotId,
        string settingsFingerprint,
        ArticleReviewReport report,
        IReadOnlyDictionary<string, ArticleSourceSpanSnapshot> spans)
    {
        CanonicalArticleReviewRun run = new()
        {
            CanonicalWorkId = canonicalWorkId,
            BaseAnalysisRunId = baseAnalysisRunId,
            ArticleSourceSnapshotId = sourceSnapshotId,
            ReviewedAt = DateTimeOffset.UtcNow,
            Language = report.Language,
            PolicyVersion = report.PolicyVersion,
            SettingsFingerprint = settingsFingerprint,
            Model = report.Model,
            PromptVersion = report.PromptVersion,
            Outcome = report.Outcome,
            VerificationStatus = report.Verification.Status,
            VerificationModel = report.Verification.Model,
            VerificationPromptVersion = report.Verification.PromptVersion,
            UsesSameModelFamily = report.Verification.UsesSameModelFamily,
            VerificationLimitation = report.Verification.Limitation,
            ProcessedPages = report.SourceCoverage.ProcessedPages,
            TextBearingPages = report.SourceCoverage.TextBearingPages,
            TotalPages = report.SourceCoverage.TotalPages,
            IsPartial = report.SourceCoverage.IsPartial,
            ScopeReason = report.SourceCoverage.ScopeReason,
            ProcessedRoles = report.Coverage.ProcessedRoles,
            TotalRoles = report.Coverage.TotalRoles,
            CandidateFindings = report.Coverage.CandidateFindings,
            AutomaticallyCheckedFindings = report.Coverage.AutomaticallyCheckedFindings,
            SupportedFindings = report.Coverage.SupportedFindings,
            UnsupportedFindings = report.Coverage.UnsupportedFindings,
            UncertainFindings = report.Coverage.UncertainFindings,
            OmittedFindings = report.Coverage.OmittedFindings,
            OmissionReasonsJson = JsonSerializer.Serialize(report.Coverage.OmissionReasons, JsonOptions),
            ReportJson = JsonSerializer.Serialize(report, JsonOptions)
        };
        int ordinal = 0;
        foreach (ArticleReviewFinding value in report.Reviews.SelectMany(review => review.Findings))
        {
            CanonicalArticleReviewFinding finding = new()
            {
                Ordinal = ordinal++,
                ExternalFindingId = value.FindingId,
                Role = value.Role,
                Kind = value.Kind,
                Basis = value.Basis,
                Suggestion = value.Suggestion
            };
            finding.Evidence.AddRange(value.Evidence.Select((evidence, evidenceOrdinal) =>
            {
                if (!spans.TryGetValue(evidence.SourceId, out ArticleSourceSpanSnapshot? span) ||
                    span.PageNumber != evidence.PageNumber || span.StartOffset != evidence.StartOffset ||
                    span.EndOffset != evidence.EndOffset || span.Text != evidence.Quote)
                    throw new JsonException("The article review cites an unknown immutable source span.");
                return new CanonicalArticleReviewEvidence { ArticleSourceSpan = span, Ordinal = evidenceOrdinal };
            }));
            run.Findings.Add(finding);
        }
        return run;
    }

    private async Task<bool> HasAssociationAsync(
        string personelId,
        int canonicalWorkId,
        CancellationToken cancellationToken) =>
        await database.CanonicalResearcherWorks.AsNoTracking().AnyAsync(association =>
            association.CanonicalWorkId == canonicalWorkId && association.PersonelId == personelId,
            cancellationToken);

    private static CanonicalArticleReviewResponse Map(
        CanonicalArticleReviewRun run,
        string personelId,
        bool reused,
        bool isStale,
        List<string> staleReasons) => new()
    {
        PersonelId = personelId,
        CanonicalWorkId = run.CanonicalWorkId,
        ReviewRunId = run.Id,
        BaseAnalysisRunId = run.BaseAnalysisRunId,
        ReviewedAt = run.ReviewedAt,
        Reused = reused,
        IsStale = isStale,
        StaleReasons = staleReasons,
        Report = JsonSerializer.Deserialize<ArticleReviewReport>(run.ReportJson, JsonOptions)
            ?? throw new JsonException("The saved article review is unusable.")
    };
}
