using Microsoft.EntityFrameworkCore;
using ResearcherAnalysisService.Products.Data;
using ResearcherAnalysisService.SourceData.Works;

namespace ResearcherAnalysisService.Products.ArticleSummaries;

internal static class CanonicalSourceIdentity
{
    public static async Task<string?> LoadAsync(
        AnalysisDbContext database,
        int canonicalWorkId,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<int, string> identities = await LoadAsync(
            database, [canonicalWorkId], cancellationToken);
        return identities.GetValueOrDefault(canonicalWorkId);
    }

    public static async Task<IReadOnlyDictionary<int, string>> LoadAsync(
        AnalysisDbContext database,
        IReadOnlyCollection<int> canonicalWorkIds,
        CancellationToken cancellationToken)
    {
        int[] ids = canonicalWorkIds.Where(id => id > 0).Distinct().ToArray();
        if (ids.Length == 0)
            return new Dictionary<int, string>();

        List<AcademicWork> works = await database.AcademicWorks.AsNoTracking()
            .Include(work => work.Sources)
            .Include(work => work.CanonicalObservation)
            .Where(work => work.CanonicalObservation != null &&
                ids.Contains(work.CanonicalObservation.CanonicalWorkId))
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        return works.GroupBy(work => work.CanonicalObservation!.CanonicalWorkId)
            .ToDictionary(group => group.Key,
                group => ArticleSummaryAutomationScheduler.CreateInputHash(group));
    }
}
