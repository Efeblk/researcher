using System.Data;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AcademicCollector.Analysis.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries.Enrichment;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Crossref;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;
using AcademicCollectorDemo.Modules.AcademicPerformance.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;

public sealed class ArticleSummaryWorkflow(AcademicDbContext database, SafeArticleFetcher fetcher,
    ArticlePdfExtractor pdfExtractor, ArticleHtmlExtractor htmlExtractor, ArticleMetadataEnricher metadataEnricher,
    ArticleSummaryServiceClient client, IOptions<ArticleSummaryOptions> options,
    CanonicalWorkSynchronizer? canonicalWorkSynchronizer = null,
    IOptionsMonitor<ArticleSummaryAutomationOptions>? automationOptions = null,
    PublicationMetricsRefreshScheduler? publicationMetricsScheduler = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly CanonicalWorkSynchronizer _canonicalWorkSynchronizer =
        canonicalWorkSynchronizer ?? new CanonicalWorkSynchronizer(database);

    public async Task<SavedArticleSummaryResponse?> SummarizeAsync(string personelId, int academicWorkId,
        string language, CancellationToken cancellationToken)
    {
        AcademicWork? work = await database.AcademicWorks.AsNoTracking()
            .Include(x => x.Sources).Include(x => x.CanonicalObservation)
            .SingleOrDefaultAsync(x => x.Id == academicWorkId && x.PersonelId == personelId, cancellationToken);
        if (work is null) return null;
        if (work.CanonicalObservation is null)
        {
            await _canonicalWorkSynchronizer.SyncAsync(personelId, cancellationToken);
            database.ChangeTracker.Clear();
            work = await database.AcademicWorks.AsNoTracking()
                .Include(x => x.Sources).Include(x => x.CanonicalObservation)
                .SingleOrDefaultAsync(x => x.Id == academicWorkId && x.PersonelId == personelId, cancellationToken);
        }
        if (work?.CanonicalObservation is null)
            throw new ArticleSourceException("The article has no current canonical publication mapping; no report was saved.");
        int canonicalWorkId = work.CanonicalObservation.CanonicalWorkId;
        await using SqlApplicationLock? executionLock = await AcquireExecutionLockAsync(
            canonicalWorkId, language, options.Value.TotalTimeoutSeconds * 1000, cancellationToken);
        if (executionLock is null)
            throw new ArticleSummaryBusyException();
        database.ChangeTracker.Clear();
        work = await database.AcademicWorks.AsNoTracking()
            .Include(x => x.Sources).Include(x => x.CanonicalObservation)
            .SingleOrDefaultAsync(x => x.Id == academicWorkId && x.PersonelId == personelId, cancellationToken);
        if (work?.CanonicalObservation?.CanonicalWorkId != canonicalWorkId)
            throw new ArticleSourceException("The article changed before summarization; no report was saved.");
        List<AcademicWork> sourceWorks = ArticleSummaryAutomationScheduler.PrepareWorks(
            await LoadSameDoiWorksAsync(work, cancellationToken));
        string capturedGlobalInputHash = ArticleSummaryAutomationScheduler.CreateInputHash(
            await LoadCanonicalWorksAsync(canonicalWorkId, cancellationToken));
        WorkflowResult result = await SummarizeCoreAsync(work, sourceWorks, language,
            AutomationSettings.PolicyVersion.Trim(), capturedGlobalInputHash,
            null, true, cancellationToken);
        return Map(result.Saved, result.Report);
    }

    public async Task<ArticleSummaryAutomationExecutionResult> SummarizeAutomaticAsync(
        ArticleSummaryAutomationAttempt attempt,
        CancellationToken cancellationToken)
    {
        await using SqlApplicationLock? executionLock = await AcquireExecutionLockAsync(
            attempt.CanonicalWorkId, attempt.Language, 0, cancellationToken);
        if (executionLock is null)
            throw new ArticleSummaryBusyException();
        List<AcademicWork> sourceWorks = ArticleSummaryAutomationScheduler.PrepareWorks(
            await LoadCanonicalWorksAsync(attempt.CanonicalWorkId, cancellationToken));
        string currentInputHash = ArticleSummaryAutomationScheduler.CreateInputHash(sourceWorks);
        if (currentInputHash != attempt.InputHash)
            throw new ArticleSummaryAutomationInputChangedException(currentInputHash);
        AcademicWork work = sourceWorks
            .OrderByDescending(value => !string.IsNullOrWhiteSpace(value.FullTextUrl))
            .ThenByDescending(value => value.Sources.Any(source => source.Kind == "Pdf"))
            .ThenByDescending(value => !string.IsNullOrWhiteSpace(value.Abstract))
            .ThenBy(value => value.Provider)
            .ThenBy(value => value.Id)
            .FirstOrDefault() ?? throw new ArticleSourceException(
                "Article evidence is unavailable because no current provider observation exists.");
        WorkflowResult result = await SummarizeCoreAsync(work, sourceWorks, attempt.Language,
            attempt.PolicyVersion, attempt.InputHash, attempt, false, cancellationToken);
        return new(result.AnalysisRunId, result.Reused);
    }

    private async Task<WorkflowResult> SummarizeCoreAsync(
        AcademicWork work,
        List<AcademicWork> sourceWorks,
        string language,
        string policyVersion,
        string capturedGlobalInputHash,
        ArticleSummaryAutomationAttempt? attempt,
        bool forceRegeneration,
        CancellationToken cancellationToken)
    {
        string personelId = work.PersonelId;
        int canonicalWorkId = work.CanonicalObservation?.CanonicalWorkId ??
            throw new ArticleSourceException("The article has no current canonical publication mapping; no report was saved.");
        CapturedWorkIdentity capturedIdentity = CaptureIdentity(work);
        string capturedSourceInputHash = ArticleSummaryAutomationScheduler.CreateInputHash(sourceWorks);
        using CancellationTokenSource total = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        total.CancelAfter(TimeSpan.FromSeconds(options.Value.TotalTimeoutSeconds));

        string? recoveredAbstract = ArticleSummaryAutomationScheduler.GetRecoveredAbstract(sourceWorks);
        int initialBudget = Math.Max(1, options.Value.MaximumSourceRequests / 2);
        IReadOnlyList<ArticleSourceCandidate> stored = ArticleSourceCandidateCatalog.GetCandidates(sourceWorks);
        IReadOnlyList<ArticleSourceCandidate> initial = stored.Take(initialBudget).ToList();
        List<string> failures = [];
        ArticleSourceRequestBudget requestBudget = new(options.Value.MaximumSourceRequests);
        using CancellationTokenSource acquisition = CancellationTokenSource.CreateLinkedTokenSource(total.Token);
        acquisition.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, Math.Min(
            options.Value.TotalTimeoutSeconds / 2, options.Value.TotalTimeoutSeconds - 30))));
        var acquired = await AcquireAsync(initial, language, failures, requestBudget, acquisition.Token, total.Token);
        recoveredAbstract ??= acquired.Abstract;

        ArticleMetadataResult? enrichment = null;
        string doi = CrossrefClient.NormalizeDoi(work.Doi);
        if (acquired.Snapshot is null && !string.IsNullOrWhiteSpace(doi))
        {
            try { enrichment = await metadataEnricher.EnrichAsync(personelId, doi, acquisition.Token); }
            catch (OperationCanceledException) when (acquisition.IsCancellationRequested && !total.IsCancellationRequested)
            { failures.Add("DOI metadata: source acquisition budget expired."); }
            recoveredAbstract ??= enrichment?.Abstract;
        }
        if (acquired.Snapshot is null && !acquisition.IsCancellationRequested)
        {
            HashSet<string> attempted = initial.Select(x => x.Url).ToHashSet(StringComparer.Ordinal);
            List<ArticleSourceCandidate> remaining = (enrichment?.Sources ?? [])
                .OrderByDescending(x => x.Kind.Equals("Pdf", StringComparison.OrdinalIgnoreCase))
                .Select(x => new ArticleSourceCandidate(x.Origin, x.Url))
                .Concat(stored.Skip(initial.Count)).Where(x => attempted.Add(x.Url))
                .Take(Math.Max(0, options.Value.MaximumSourceRequests - initial.Count)).ToList();
            var remainingAcquisition = await AcquireAsync(remaining, language, failures, requestBudget,
                acquisition.Token, total.Token);
            recoveredAbstract ??= remainingAcquisition.Abstract;
            if (remainingAcquisition.Snapshot is not null) acquired = remainingAcquisition;
        }

        SummarizeArticleRequest snapshot;
        string? sourceUrl = null;
        if (acquired.Snapshot is { } acquiredSnapshot) { snapshot = acquiredSnapshot; sourceUrl = acquired.Url; }
        else if (!string.IsNullOrWhiteSpace(recoveredAbstract))
        {
            string reason = initial.Count == 0 && enrichment is null
                ? "No saved full-text URL exists; only the saved database abstract was summarized."
                : $"Full-text sources were unavailable or unusable; only the recovered abstract was summarized.{StatusSuffix(enrichment)}{FailureSuffix(failures)}";
            snapshot = AbstractSnapshot(recoveredAbstract, language, reason);
        }
        else throw new ArticleSourceException(
            $"Article evidence is unavailable: no usable full text or abstract was found.{StatusSuffix(enrichment)}{FailureSuffix(failures)}");

        string canonical = JsonSerializer.Serialize(snapshot.Pages, JsonOptions);
        snapshot = snapshot with
        {
            SourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant()
        };
        if (!forceRegeneration)
        {
            CanonicalArticleAnalysisRun? reusable = await FindReusableRunAsync(
                canonicalWorkId, language, policyVersion, snapshot, total.Token);
            if (reusable is not null)
            {
                await CompleteReusedAttemptAsync(attempt!, reusable.Id, total.Token);
                return new(reusable.SavedArticleSummary!,
                    JsonSerializer.Deserialize<ArticleSummaryReport>(
                        reusable.SavedArticleSummary!.ReportJson, JsonOptions)!, reusable.Id, true);
            }
        }
        DateTimeOffset sourceAcquiredAt = DateTimeOffset.UtcNow;
        ArticleSummaryReport report = await client.SummarizeAsync(snapshot, total.Token);
        if (report.Verification?.Status == "insufficient_evidence")
            throw new ArticleSourceException("Automatic verification found no adequately supported claims; no summary was saved.");
        SavedArticleSummary saved = new()
        {
            AcademicWorkId = work.Id, OriginalAcademicWorkId = work.Id, PersonelId = personelId,
            SavedAt = DateTimeOffset.UtcNow, SourceUrl = sourceUrl, SourceHash = snapshot.SourceHash,
            SourceKind = snapshot.SourceKind, ExtractionVersion = snapshot.ExtractionVersion,
            SnapshotJson = JsonSerializer.Serialize(snapshot, JsonOptions), ReportJson = JsonSerializer.Serialize(report, JsonOptions)
        };
        CanonicalArticleAnalysisRun run = await PersistAsync(
            work.Id, personelId, canonicalWorkId, capturedIdentity, capturedSourceInputHash,
            capturedGlobalInputHash, attempt is not null, recoveredAbstract,
            enrichment?.Sources ?? [], sourceUrl, acquired.Origin ?? "DatabaseAbstract", sourceAcquiredAt,
            snapshot, report, saved, policyVersion, attempt, total.Token);
        return new(saved, report, run.Id, false);
    }

    public async Task<SavedArticleSummaryResponse?> GetLatestAsync(
        string personelId,
        int academicWorkId,
        string language,
        CancellationToken cancellationToken)
    {
        int? canonicalWorkId = await database.CanonicalWorkObservations.AsNoTracking()
            .Where(value => value.AcademicWorkId == academicWorkId && value.PersonelId == personelId &&
                database.CanonicalResearcherWorks.Any(association =>
                    association.CanonicalWorkId == value.CanonicalWorkId &&
                    association.PersonelId == personelId))
            .Select(value => (int?)value.CanonicalWorkId)
            .SingleOrDefaultAsync(cancellationToken);
        if (canonicalWorkId is not null)
        {
            CanonicalArticleAnalysisRun? canonical = await database.CanonicalArticleAnalysisRuns.AsNoTracking()
                .Include(value => value.SavedArticleSummary)
                .Where(value => value.CanonicalWorkId == canonicalWorkId && value.Language == language)
                .OrderByDescending(value => value.Id).FirstOrDefaultAsync(cancellationToken);
            if (canonical?.SavedArticleSummary is null)
                return await GetLatestLegacyAsync(
                    personelId, academicWorkId, language, cancellationToken);
            ArticleSummaryReport canonicalReport = JsonSerializer.Deserialize<ArticleSummaryReport>(
                canonical.SavedArticleSummary.ReportJson, JsonOptions)!;
            SavedArticleSummaryResponse mapped = Map(canonical.SavedArticleSummary, canonicalReport);
            return mapped with
            {
                OriginalAcademicWorkId = academicWorkId,
                PersonelID = personelId,
                SourceUrl = canonical.SavedArticleSummary.PersonelId == personelId &&
                    canonical.SavedArticleSummary.OriginalAcademicWorkId == academicWorkId
                    ? mapped.SourceUrl
                    : null
            };
        }

        CanonicalArticleAnalysisRun? ownRun = await database.CanonicalArticleAnalysisRuns.AsNoTracking()
            .Include(value => value.SavedArticleSummary)
            .Where(value => value.Language == language &&
                value.SavedArticleSummary!.PersonelId == personelId &&
                value.SavedArticleSummary.OriginalAcademicWorkId == academicWorkId)
            .OrderByDescending(value => value.Id).FirstOrDefaultAsync(cancellationToken);
        if (ownRun?.SavedArticleSummary is not null)
        {
            ArticleSummaryReport report = JsonSerializer.Deserialize<ArticleSummaryReport>(
                ownRun.SavedArticleSummary.ReportJson, JsonOptions)!;
            return Map(ownRun.SavedArticleSummary, report);
        }
        return await GetLatestLegacyAsync(personelId, academicWorkId, language, cancellationToken);
    }

    private async Task<SavedArticleSummaryResponse?> GetLatestLegacyAsync(
        string personelId,
        int academicWorkId,
        string language,
        CancellationToken cancellationToken)
    {
        await foreach (SavedArticleSummary legacy in database.ArticleSummaries.AsNoTracking()
            .Where(value => value.PersonelId == personelId &&
                value.OriginalAcademicWorkId == academicWorkId &&
                !database.CanonicalArticleAnalysisRuns.Any(run =>
                    run.SavedArticleSummaryId == value.Id))
            .OrderByDescending(value => value.Id)
            .AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            ArticleSummaryReport? report = JsonSerializer.Deserialize<ArticleSummaryReport>(
                legacy.ReportJson, JsonOptions);
            if (report?.Language == language)
                return Map(legacy, report);
        }
        return null;
    }

    private async Task<SqlApplicationLock?> AcquireExecutionLockAsync(
        int canonicalWorkId, string language, int timeoutMilliseconds,
        CancellationToken cancellationToken) =>
        await SqlApplicationLock.TryAcquireAsync(database.Database.GetConnectionString()!,
            $"AcademicCollector.ArticleSummary.{canonicalWorkId}.{language}",
            timeoutMilliseconds, cancellationToken);

    private async Task<List<AcademicWork>> LoadSameDoiWorksAsync(AcademicWork work, CancellationToken cancellationToken)
    {
        List<AcademicWork> result = [work];
        string doi = CrossrefClient.NormalizeDoi(work.Doi);
        if (string.IsNullOrWhiteSpace(doi)) return result;
        List<int> ids = (await database.AcademicWorks.AsNoTracking()
            .Where(x => x.PersonelId == work.PersonelId && x.Id != work.Id && x.Doi != null)
            .Select(x => new { x.Id, x.Doi }).ToListAsync(cancellationToken))
            .Where(x => CrossrefClient.NormalizeDoi(x.Doi) == doi).Select(x => x.Id).Take(8).ToList();
        if (ids.Count != 0) result.AddRange(await database.AcademicWorks.AsNoTracking().Include(x => x.Sources)
            .Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken));
        return result;
    }

    private async Task<List<AcademicWork>> LoadCanonicalWorksAsync(
        int canonicalWorkId, CancellationToken cancellationToken) =>
        await database.AcademicWorks.AsNoTracking()
            .Include(value => value.Sources)
            .Include(value => value.CanonicalObservation)
            .Where(value => value.CanonicalObservation != null &&
                value.CanonicalObservation.CanonicalWorkId == canonicalWorkId)
            .OrderBy(value => value.Provider)
            .ThenBy(value => value.ProviderWorkId)
            .ThenBy(value => value.Id)
            .ToListAsync(cancellationToken);

    private async Task<CanonicalArticleAnalysisRun?> FindReusableRunAsync(
        int canonicalWorkId,
        string language,
        string policyVersion,
        SummarizeArticleRequest snapshot,
        CancellationToken cancellationToken) =>
        await database.CanonicalArticleAnalysisRuns.AsNoTracking()
            .Include(value => value.ArticleSourceSnapshot)
            .Include(value => value.SavedArticleSummary)
            .Where(value => value.CanonicalWorkId == canonicalWorkId &&
                value.Language == language && value.PolicyVersion == policyVersion &&
                value.ArticleSourceSnapshot!.ExtractedTextHash == snapshot.SourceHash &&
                value.ArticleSourceSnapshot.SourceKind == snapshot.SourceKind &&
                value.ArticleSourceSnapshot.ExtractionVersion == snapshot.ExtractionVersion)
            .OrderByDescending(value => value.Id)
            .FirstOrDefaultAsync(cancellationToken);

    internal async Task<Acquisition> AcquireAsync(IReadOnlyList<ArticleSourceCandidate> candidates,
        string language, List<string> failures, CancellationToken cancellationToken, CancellationToken totalToken) =>
        await AcquireAsync(candidates, language, failures,
            new ArticleSourceRequestBudget(options.Value.MaximumSourceRequests), cancellationToken, totalToken);

    internal async Task<Acquisition> AcquireAsync(IReadOnlyList<ArticleSourceCandidate> candidates,
        string language, List<string> failures, ArticleSourceRequestBudget requestBudget,
        CancellationToken cancellationToken, CancellationToken totalToken)
    {
        string? discoveredAbstract = null;
        foreach (ArticleSourceCandidate candidate in candidates)
        {
            try
            {
                FetchedArticleSource fetched = await fetcher.FetchSourceAsync(
                    new Uri(candidate.Url), requestBudget, cancellationToken);
                if (fetched.MediaType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
                    return new(await pdfExtractor.ExtractAsync(fetched.Bytes, language, cancellationToken),
                        fetched.FinalUri.ToString(), discoveredAbstract, candidate.Origin);
                discoveredAbstract ??= htmlExtractor.TryExtractAbstract(fetched.Bytes, fetched.FinalUri);
                return new(htmlExtractor.Extract(fetched.Bytes, fetched.FinalUri, language),
                    fetched.FinalUri.ToString(), discoveredAbstract, candidate.Origin);
            }
            catch (Exception exception) when (exception is ArticleSourceException or HttpRequestException or SocketException)
            { failures.Add($"{candidate.Origin}: {SafeFailure(exception)}"); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested && !totalToken.IsCancellationRequested)
            {
                failures.Add("Source acquisition budget expired; the recovered abstract remains available as fallback.");
                return new(null, null, discoveredAbstract, null);
            }
        }
        return new(null, null, discoveredAbstract, null);
    }

    private async Task<CanonicalArticleAnalysisRun> PersistAsync(
        int workId, string personelId, int canonicalWorkId,
        CapturedWorkIdentity capturedIdentity, string capturedSourceInputHash,
        string capturedGlobalInputHash, bool globalSourceSet, string? recoveredAbstract,
        IReadOnlyList<AcademicWorkSource> enrichedSources,
        string? sourceUrl, string sourceOrigin, DateTimeOffset sourceAcquiredAt,
        SummarizeArticleRequest snapshot, ArticleSummaryReport report, SavedArticleSummary saved,
        string policyVersion, ArticleSummaryAutomationAttempt? attempt,
        CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await _canonicalWorkSynchronizer.AcquireWriteGateAsync(cancellationToken);
        ArticleSummaryAutomationJob? job = attempt is null
            ? null
            : await GetOwnedAttemptAsync(attempt, cancellationToken);
        AcademicWork? owned = await database.AcademicWorks.Include(x => x.Sources)
            .Include(x => x.CanonicalObservation)
            .SingleOrDefaultAsync(x => x.Id == workId && x.PersonelId == personelId, cancellationToken);
        if (owned?.CanonicalObservation?.CanonicalWorkId != canonicalWorkId ||
            CaptureIdentity(owned) != capturedIdentity ||
            !await database.CanonicalResearcherWorks.AnyAsync(association =>
                association.CanonicalWorkId == canonicalWorkId && association.PersonelId == personelId,
                cancellationToken))
            throw new ArticleSourceException("The article changed during summarization; no report was saved.");
        List<AcademicWork> currentSourceWorks = globalSourceSet
            ? await LoadCanonicalWorksAsync(canonicalWorkId, cancellationToken)
            : await LoadSameDoiWorksAsync(owned, cancellationToken);
        if (ArticleSummaryAutomationScheduler.CreateInputHash(currentSourceWorks) != capturedSourceInputHash)
            throw new ArticleSourceException("The article sources changed during summarization; no report was saved.");
        bool metricInputChanged = false;
        if (string.IsNullOrWhiteSpace(owned.Abstract) && !string.IsNullOrWhiteSpace(recoveredAbstract))
        {
            owned.Abstract = recoveredAbstract;
            metricInputChanged = true;
        }
        foreach (AcademicWorkSource source in enrichedSources)
            metricInputChanged |= AddSource(owned, source.Url, source.Kind, source.Origin, source.IsOpenAccess);
        if (!string.IsNullOrWhiteSpace(sourceUrl))
        {
            if (snapshot.SourceKind == "pdf")
            {
                if (owned.FullTextUrl != sourceUrl)
                {
                    owned.FullTextUrl = sourceUrl;
                    metricInputChanged = true;
                }
                metricInputChanged |= AddSource(owned, sourceUrl, "Pdf", "ResolvedPdf", null);
            }
            else if (snapshot.SourceKind == "html")
            {
                metricInputChanged |= AddSource(owned, sourceUrl, "Html", "ResolvedHtml", null);
            }
        }
        List<AcademicWork> preWriteGlobalWorks = await LoadCanonicalWorksAsync(
            canonicalWorkId, cancellationToken);
        string preWriteGlobalInputHash = ArticleSummaryAutomationScheduler.CreateInputHash(
            preWriteGlobalWorks);
        bool globalInputChanged = preWriteGlobalInputHash != capturedGlobalInputHash;
        List<AcademicWork> postWriteWorks = preWriteGlobalWorks;
        postWriteWorks.RemoveAll(value => value.Id == owned.Id);
        postWriteWorks.Add(owned);
        string postWriteInputHash = ArticleSummaryAutomationScheduler.CreateInputHash(postWriteWorks);

        ArticleSourceSnapshot? sourceSnapshot = await database.ArticleSourceSnapshots
            .Include(value => value.Spans)
            .SingleOrDefaultAsync(value => value.CanonicalWorkId == canonicalWorkId &&
                value.ExtractedTextHash == snapshot.SourceHash && value.SourceKind == snapshot.SourceKind &&
                value.ExtractionVersion == snapshot.ExtractionVersion, cancellationToken);
        if (sourceSnapshot is null)
        {
            sourceSnapshot = CreateSourceSnapshot(canonicalWorkId, snapshot, saved.SavedAt);
            database.ArticleSourceSnapshots.Add(sourceSnapshot);
        }

        database.ArticleSummaries.Add(saved);
        CanonicalArticleAnalysisRun run = CreateAnalysisRun(
            canonicalWorkId, sourceSnapshot, saved, snapshot, report,
            sourceUrl, sourceOrigin, sourceAcquiredAt, policyVersion);
        database.CanonicalArticleAnalysisRuns.Add(run);
        await database.SaveChangesAsync(cancellationToken);
        if (metricInputChanged && publicationMetricsScheduler is not null)
            await publicationMetricsScheduler.ScheduleAsync(personelId, cancellationToken);
        if (job is not null)
        {
            if (job.DesiredInputHash == attempt!.InputHash &&
                job.DesiredPolicyVersion == attempt.PolicyVersion)
                job.DesiredInputHash = postWriteInputHash;
            FinalizeSuccessfulAttempt(job, attempt, run.Id, false, postWriteInputHash);
            await database.SaveChangesAsync(cancellationToken);
        }
        else if (AutomationSettings.Enabled)
        {
            await CoalesceExistingJobAfterManualRunAsync(
                canonicalWorkId, report.Language, policyVersion, postWriteInputHash,
                capturedGlobalInputHash, globalInputChanged, run.Id, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return run;
    }

    private async Task CompleteReusedAttemptAsync(
        ArticleSummaryAutomationAttempt attempt,
        long analysisRunId,
        CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        await _canonicalWorkSynchronizer.AcquireWriteGateAsync(cancellationToken);
        ArticleSummaryAutomationJob job = await GetOwnedAttemptAsync(attempt, cancellationToken);
        FinalizeSuccessfulAttempt(job, attempt, analysisRunId, true, attempt.InputHash);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<ArticleSummaryAutomationJob> GetOwnedAttemptAsync(
        ArticleSummaryAutomationAttempt attempt,
        CancellationToken cancellationToken)
    {
        ArticleSummaryAutomationJob? job = await database.ArticleSummaryAutomationJobs
            .SingleOrDefaultAsync(value => value.Id == attempt.JobId, cancellationToken);
        if (job is null || job.Status != ArticleSummaryAutomationJobStatus.Running ||
            job.ExecutionToken != attempt.ExecutionToken ||
            job.CanonicalWorkId != attempt.CanonicalWorkId || job.Language != attempt.Language ||
            job.RunningInputHash != attempt.InputHash ||
            job.RunningPolicyVersion != attempt.PolicyVersion)
            throw new ArticleSummaryAutomationAttemptLostException();
        return job;
    }

    private static void FinalizeSuccessfulAttempt(
        ArticleSummaryAutomationJob job,
        ArticleSummaryAutomationAttempt attempt,
        long analysisRunId,
        bool reused,
        string processedInputHash)
    {
        DateTime now = DateTime.UtcNow;
        job.ProcessedInputHash = processedInputHash;
        job.ProcessedPolicyVersion = attempt.PolicyVersion;
        job.LastSuccessfulAnalysisRunId = analysisRunId;
        job.LastOutcomeCode = reused ? "Reused" : "Generated";
        job.LastOutcomeMessage = reused
            ? "A current successful analysis was reused."
            : "Article analysis completed.";
        job.UpdatedAt = now;
        bool current = job.DesiredInputHash == processedInputHash &&
            job.DesiredPolicyVersion == attempt.PolicyVersion;
        job.Status = current
            ? ArticleSummaryAutomationJobStatus.Succeeded
            : ArticleSummaryAutomationJobStatus.Pending;
        job.NextAttemptAt = current ? now : now;
        job.CompletedAt = current ? now : null;
        if (!current)
            job.Attempts = 0;
        job.ExecutionToken = null;
        job.RunningInputHash = null;
        job.RunningPolicyVersion = null;
    }

    private async Task CoalesceExistingJobAfterManualRunAsync(
        int canonicalWorkId,
        string language,
        string policyVersion,
        string inputHash,
        string capturedGlobalInputHash,
        bool globalInputChanged,
        long analysisRunId,
        CancellationToken cancellationToken)
    {
        ArticleSummaryAutomationJob? job = await database.ArticleSummaryAutomationJobs
            .SingleOrDefaultAsync(value => value.CanonicalWorkId == canonicalWorkId &&
                value.Language == language, cancellationToken);
        if (job is null)
            return;

        job.LastSuccessfulAnalysisRunId = analysisRunId;
        job.ProcessedInputHash = null;
        job.ProcessedPolicyVersion = null;
        job.UpdatedAt = DateTime.UtcNow;
        bool canSatisfy = !globalInputChanged &&
            job.DesiredInputHash == capturedGlobalInputHash &&
            job.DesiredPolicyVersion == policyVersion;
        if (canSatisfy)
        {
            job.DesiredInputHash = inputHash;
            job.DesiredPolicyVersion = policyVersion;
        }
        if (job.Status == ArticleSummaryAutomationJobStatus.Running)
        {
            await database.SaveChangesAsync(cancellationToken);
            return;
        }

        if (canSatisfy)
        {
            job.ProcessedInputHash = inputHash;
            job.ProcessedPolicyVersion = policyVersion;
            job.Status = ArticleSummaryAutomationJobStatus.Succeeded;
            job.CompletedAt = job.UpdatedAt;
            job.LastOutcomeCode = "ManualGenerated";
            job.LastOutcomeMessage = "A manual article analysis satisfied the queued input.";
        }
        else
        {
            job.Status = ArticleSummaryAutomationJobStatus.Pending;
            job.Attempts = 0;
            job.NextAttemptAt = job.UpdatedAt;
            job.CompletedAt = null;
            job.LastOutcomeCode = "InputChanged";
            job.LastOutcomeMessage = "New canonical source metadata remains queued after the manual run.";
        }
        job.ExecutionToken = null;
        job.RunningInputHash = null;
        job.RunningPolicyVersion = null;
        await database.SaveChangesAsync(cancellationToken);
    }

    private static ArticleSourceSnapshot CreateSourceSnapshot(
        int canonicalWorkId, SummarizeArticleRequest snapshot, DateTimeOffset createdAt)
    {
        ArticleSourceSnapshot source = new()
        {
            CanonicalWorkId = canonicalWorkId,
            ExtractedTextHash = snapshot.SourceHash,
            SourceKind = snapshot.SourceKind,
            ExtractionVersion = snapshot.ExtractionVersion,
            CreatedAt = createdAt
        };
        source.Pages.AddRange(snapshot.Pages.Select((page, ordinal) => new ArticleSourcePageSnapshot
        {
            Ordinal = ordinal,
            PageNumber = page.PageNumber,
            Text = page.Text
        }));
        source.Spans.AddRange(snapshot.SourceSpans!.Select((span, ordinal) => new ArticleSourceSpanSnapshot
        {
            SourceId = span.SourceId,
            Ordinal = ordinal,
            PageNumber = span.PageNumber,
            StartOffset = span.StartOffset,
            EndOffset = span.EndOffset,
            Text = span.Text
        }));
        return source;
    }

    private static CanonicalArticleAnalysisRun CreateAnalysisRun(
        int canonicalWorkId, ArticleSourceSnapshot source, SavedArticleSummary saved,
        SummarizeArticleRequest snapshot, ArticleSummaryReport report, string? sourceUrl,
        string sourceOrigin, DateTimeOffset sourceAcquiredAt, string policyVersion)
    {
        ArticleVerificationMetadata verification = report.Verification!;
        ArticleCoverage coverage = report.Coverage;
        CanonicalArticleAnalysisRun run = new()
        {
            CanonicalWorkId = canonicalWorkId,
            ArticleSourceSnapshot = source,
            SavedArticleSummary = saved,
            AnalyzedAt = saved.SavedAt,
            SourceAcquiredAt = sourceAcquiredAt,
            SourceUrl = sourceUrl,
            SourceOrigin = sourceOrigin,
            Language = report.Language,
            PolicyVersion = policyVersion,
            Model = report.Model,
            PromptVersion = report.PromptVersion,
            ExtractionMethod = report.ExtractionMethod ??
                ArticleSummaryServiceClient.GetExtractionMethod(snapshot.SourceKind, snapshot.ExtractionVersion),
            ProcessedChunks = coverage.ProcessedChunks,
            TotalChunks = coverage.TotalChunks,
            ProcessedPages = coverage.ProcessedPages,
            TextBearingPages = coverage.TextBearingPages,
            TotalPages = coverage.TotalPages,
            SelectedClaimsOmitted = coverage.SelectedClaimsOmitted,
            IsPartial = coverage.IsPartial,
            ScopeReason = coverage.ScopeReason,
            CandidateClaims = coverage.CandidateClaims,
            AutomaticallyCheckedClaims = coverage.AutomaticallyCheckedClaims,
            SupportedClaims = coverage.SupportedClaims,
            UnsupportedClaims = coverage.UnsupportedClaims,
            UncertainClaims = coverage.UncertainClaims,
            DuplicateOrCappedClaims = coverage.DuplicateOrCappedClaims,
            BudgetUnverifiedClaims = coverage.BudgetUnverifiedClaims,
            OmissionReasonsJson = JsonSerializer.Serialize(coverage.OmissionReasons ?? [], JsonOptions),
            VerificationStatus = verification.Status,
            VerificationModel = verification.Model,
            VerificationPromptVersion = verification.PromptVersion,
            UsesSameModelFamily = verification.UsesSameModelFamily,
            VerificationLimitation = verification.Limitation
        };
        IReadOnlyList<(string Name, IReadOnlyList<ArticleClaim> Claims)> sections =
        [
            ("Purpose", report.Sections.Purpose),
            ("Methods", report.Sections.Methods),
            ("Data", report.Sections.Data),
            ("Findings", report.Sections.Findings),
            ("Limitations", report.Sections.Limitations)
        ];
        Dictionary<string, ArticleSourceSpanSnapshot> spans = source.Spans
            .ToDictionary(span => span.SourceId, StringComparer.Ordinal);
        for (int sectionOrder = 0; sectionOrder < sections.Count; sectionOrder++)
        {
            (string section, IReadOnlyList<ArticleClaim> claims) = sections[sectionOrder];
            for (int ordinal = 0; ordinal < claims.Count; ordinal++)
            {
                ArticleClaim value = claims[ordinal];
                CanonicalArticleClaim claim = new()
                {
                    Section = section,
                    SectionOrder = sectionOrder,
                    Ordinal = ordinal,
                    ExternalClaimId = value.ClaimId,
                    Text = value.Text
                };
                claim.Evidence.AddRange(value.Evidence.Select((evidence, evidenceOrdinal) =>
                {
                    ArticleSourceSpanSnapshot span = spans[evidence.SourceId!];
                    return new CanonicalArticleClaimEvidence
                    {
                        ArticleSourceSpan = span,
                        Ordinal = evidenceOrdinal
                    };
                }));
                run.Claims.Add(claim);
            }
        }
        return run;
    }

    private static bool AddSource(AcademicWork work, string url, string kind, string origin, bool? openAccess)
    {
        if (string.IsNullOrWhiteSpace(url) || work.Sources.Any(x => x.Url == url)) return false;
        work.Sources.Add(new() { Url = url, Kind = kind, Origin = origin, IsOpenAccess = openAccess });
        return true;
    }

    private static SummarizeArticleRequest AbstractSnapshot(string text, string language, string reason)
    {
        string trimmed = text.Trim(); string canonical = trimmed[..Math.Min(trimmed.Length, 24000)];
        IReadOnlyList<ArticlePage> pages = [new(null, canonical)];
        return new(language, "abstract", "", "database-abstract-spans-v2", pages, 1, true,
            trimmed.Length > 24000 ? reason + " The abstract was truncated to 24,000 characters." : reason)
            { SourceSpans = ArticleSourceCatalog.Create(pages) };
    }

    private static string FailureSuffix(IReadOnlyList<string> failures) => failures.Count == 0 ? "" : $" Attempts: {string.Join(" ", failures)}";
    private static string StatusSuffix(ArticleMetadataResult? enrichment) => enrichment is null || string.IsNullOrWhiteSpace(enrichment.Status)
        ? "" : $" DOI metadata status: {enrichment.Status}.";
    private static string SafeFailure(Exception exception) => exception switch
    {
        ArticleSourceException source => source.Message,
        HttpRequestException { StatusCode: not null } request => $"HTTP status {(int)request.StatusCode.Value}.",
        HttpRequestException => "network request failed.", SocketException => "network connection failed.",
        _ => "source processing failed."
    };
    private static CapturedWorkIdentity CaptureIdentity(AcademicWork work)
    {
        string? normalizedDoi = AcademicDoiNormalizer.NormalizeValid(work.Doi);
        return normalizedDoi is not null
            ? new(normalizedDoi, null, null)
            : new(null, work.Provider,
                string.IsNullOrWhiteSpace(work.ProviderWorkId)
                    ? "academic-work:" + work.Id
                    : "provider-work:" + work.ProviderWorkId.Trim().ToLowerInvariant());
    }
    private static SavedArticleSummaryResponse Map(SavedArticleSummary value, ArticleSummaryReport report) =>
        new(value.Id, value.OriginalAcademicWorkId, value.PersonelId, value.SavedAt, value.SourceUrl,
            report.ExtractionMethod is null ? report with
            { ExtractionMethod = ArticleSummaryServiceClient.GetExtractionMethod(value.SourceKind, value.ExtractionVersion) } : report);
    internal sealed record Acquisition(
        SummarizeArticleRequest? Snapshot, string? Url, string? Abstract, string? Origin);
    private sealed record WorkflowResult(
        SavedArticleSummary Saved, ArticleSummaryReport Report, long AnalysisRunId, bool Reused);
    private sealed record CapturedWorkIdentity(
        string? NormalizedDoi, AcademicWorkProvider? Provider, string? ProviderWorkId);

    private ArticleSummaryAutomationOptions AutomationSettings =>
        automationOptions?.CurrentValue ?? new ArticleSummaryAutomationOptions();
}

public sealed class ArticleSummaryBusyException : Exception
{
    public ArticleSummaryBusyException()
        : base("Article summary generation is already in progress.")
    {
    }
}

public sealed class ArticleSummaryAutomationAttemptLostException : Exception
{
    public ArticleSummaryAutomationAttemptLostException()
        : base("The automatic article summary attempt no longer owns its queue generation.")
    {
    }
}

public sealed class ArticleSummaryAutomationInputChangedException : Exception
{
    public string CurrentInputHash { get; }

    public ArticleSummaryAutomationInputChangedException(string currentInputHash)
        : base("The automatic article summary input changed before analysis.")
    {
        CurrentInputHash = currentInputHash;
    }
}
