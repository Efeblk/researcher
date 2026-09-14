using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ResearcherAnalysisService.Products.Data;
using ResearcherAnalysisService.SourceData.Works;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ResearcherAnalysisService.Products.ArticleSummaries;

public sealed class ArticleSummaryAutomationScheduler(
    AnalysisDbContext database,
    IOptionsMonitor<ArticleSummaryAutomationOptions> options)
{
    public async Task ScheduleAsync(
        IEnumerable<int> canonicalWorkIds,
        CancellationToken cancellationToken = default)
    {
        ArticleSummaryAutomationOptions settings = options.CurrentValue;
        if (!settings.Enabled)
            return;

        int[] ids = canonicalWorkIds.Where(id => id > 0).Distinct().ToArray();
        if (ids.Length == 0)
            return;

        List<(int CanonicalWorkId, int AcademicWorkId)> observations =
            await database.CanonicalWorkObservations.AsNoTracking()
                .Where(observation => ids.Contains(observation.CanonicalWorkId))
                .Select(observation => new ValueTuple<int, int>(
                    observation.CanonicalWorkId, observation.AcademicWorkId))
                .ToListAsync(cancellationToken);
        int[] workIds = observations.Select(value => value.AcademicWorkId).Distinct().ToArray();
        if (workIds.Length == 0)
            return;

        Dictionary<int, AcademicWork> works = await database.AcademicWorks.AsNoTracking()
            .Include(work => work.Sources)
            .Where(work => workIds.Contains(work.Id))
            .ToDictionaryAsync(work => work.Id, cancellationToken);
        Dictionary<int, List<AcademicWork>> worksByCanonical = observations
            .Where(value => works.ContainsKey(value.AcademicWorkId))
            .GroupBy(value => value.CanonicalWorkId)
            .ToDictionary(group => group.Key,
                group => group.Select(value => works[value.AcademicWorkId]).ToList());
        DateTime now = DateTime.UtcNow;
        string language = settings.Language;
        string policyVersion = settings.PolicyVersion.Trim();

        foreach ((int canonicalWorkId, List<AcademicWork> canonicalWorks) in worksByCanonical)
        {
            string inputHash = CreateInputHash(canonicalWorks);
            ArticleSummaryAutomationJob? job = await database.ArticleSummaryAutomationJobs
                .SingleOrDefaultAsync(value => value.CanonicalWorkId == canonicalWorkId &&
                    value.Language == language, cancellationToken);
            if (job is null)
            {
                database.ArticleSummaryAutomationJobs.Add(new()
                {
                    CanonicalWorkId = canonicalWorkId,
                    Language = language,
                    Status = ArticleSummaryAutomationJobStatus.Pending,
                    DesiredInputHash = inputHash,
                    DesiredPolicyVersion = policyVersion,
                    NextAttemptAt = now,
                    UpdatedAt = now
                });
                continue;
            }

            if (job.DesiredInputHash == inputHash && job.DesiredPolicyVersion == policyVersion)
                continue;

            job.DesiredInputHash = inputHash;
            job.DesiredPolicyVersion = policyVersion;
            job.UpdatedAt = now;
            job.Attempts = 0;
            if (job.Status == ArticleSummaryAutomationJobStatus.Running)
                continue;

            job.Status = ArticleSummaryAutomationJobStatus.Pending;
            job.NextAttemptAt = now;
            job.CompletedAt = null;
            job.LastOutcomeCode = null;
            job.LastOutcomeMessage = null;
        }

        await database.SaveChangesAsync(cancellationToken);
    }

    internal static string CreateInputHash(IEnumerable<AcademicWork> works)
    {
        List<AcademicWork> ordered = PrepareWorks(works);
        var input = new
        {
            Candidates = ArticleSourceCandidateCatalog.GetCandidates(ordered)
                .Select(candidate => new
                {
                    Origin = candidate.Origin.Trim(),
                    Url = candidate.Url.Trim()
                }),
            RecoveredAbstract = GetRecoveredAbstract(ordered)?.Trim()
        };
        string canonical = JsonSerializer.Serialize(input);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    internal static List<AcademicWork> PrepareWorks(IEnumerable<AcademicWork> works)
    {
        List<AcademicWork> result = works
            .OrderBy(work => work.Provider)
            .ThenBy(work => work.ProviderWorkId ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(work => work.FullTextUrl ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(work => work.Link ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(work => work.Title ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(work => work.Abstract ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(work => work.Id)
            .ToList();
        foreach (AcademicWork work in result)
        {
            work.Sources = work.Sources
                .OrderBy(source => source.Kind, StringComparer.Ordinal)
                .ThenBy(source => source.Origin, StringComparer.Ordinal)
                .ThenBy(source => source.Url, StringComparer.Ordinal)
                .ToList();
        }
        return result;
    }

    internal static string? GetRecoveredAbstract(IEnumerable<AcademicWork> works)
    {
        foreach (AcademicWork work in works)
        {
            string? value = string.IsNullOrWhiteSpace(work.Abstract)
                ? ArticleAbstractReader.FromPayload(work.ProviderPayload, work.Provider.ToString())
                : work.Abstract;
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }
        return null;
    }
}
