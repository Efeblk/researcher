using ResearcherAnalysisService.Products.Api.Contracts;
using ResearcherAnalysisService.Products.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ResearcherAnalysisService.Products.ArticleSummaries;

public sealed class ArticleSummaryAutomationStatusService(
    AnalysisDbContext database,
    IOptionsMonitor<ArticleSummaryAutomationOptions> options)
{
    public async Task<ArticleSummaryAutomationStatusResponse?> GetAsync(
        string personelId,
        int canonicalWorkId,
        string language,
        CancellationToken cancellationToken = default)
    {
        bool associated = await database.CanonicalResearcherWorks.AsNoTracking()
            .AnyAsync(value => value.PersonelId == personelId &&
                value.CanonicalWorkId == canonicalWorkId, cancellationToken);
        if (!associated)
            return null;

        ArticleSummaryAutomationJob? job = await database.ArticleSummaryAutomationJobs.AsNoTracking()
            .SingleOrDefaultAsync(value => value.CanonicalWorkId == canonicalWorkId &&
                value.Language == language, cancellationToken);
        CanonicalArticleAnalysisRun? lastSuccess = await database.CanonicalArticleAnalysisRuns.AsNoTracking()
            .Where(value => value.CanonicalWorkId == canonicalWorkId && value.Language == language)
            .OrderByDescending(value => value.Id).FirstOrDefaultAsync(cancellationToken);
        ArticleSummaryAutomationOptions settings = options.CurrentValue;
        return new()
        {
            PersonelId = personelId,
            CanonicalWorkId = canonicalWorkId,
            Language = language,
            AutomationEnabled = settings.Enabled,
            WorkerEnabled = settings.WorkerEnabled,
            Status = job?.Status ?? "NotQueued",
            Attempts = job?.Attempts ?? 0,
            NextAttemptAt = job?.Status is ArticleSummaryAutomationJobStatus.Pending or
                ArticleSummaryAutomationJobStatus.RetryWaiting ? job.NextAttemptAt : null,
            StartedAt = job?.StartedAt,
            CompletedAt = job?.CompletedAt,
            UpdatedAt = job?.UpdatedAt,
            LastOutcomeCode = job?.LastOutcomeCode,
            LastOutcomeMessage = job?.LastOutcomeMessage,
            LastSuccess = lastSuccess is null ? null : new()
            {
                AnalysisRunId = lastSuccess.Id,
                AnalyzedAt = lastSuccess.AnalyzedAt,
                Language = lastSuccess.Language,
                PolicyVersion = lastSuccess.PolicyVersion,
                Model = lastSuccess.Model,
                PromptVersion = lastSuccess.PromptVersion
            }
        };
    }
}
