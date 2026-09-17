using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Metrics;

public sealed class ResearcherMetricsService(
    AcademicDbContext dbContext,
    CanonicalWorkSynchronizer canonicalWorkSynchronizer)
{
    public async Task<DateTime> RecalculateAsync(
        string personelId,
        CancellationToken cancellationToken = default,
        IProgress<ResearcherMetricsProgress>? progress = null)
    {
        if (dbContext.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Metric recalculation requires all pending changes to be saved first.");

        Report(progress, "connecting-database", "Metrik işlemi için veritabanı bağlantısı hazırlanıyor.");
        await using IDbContextTransaction transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);
        Report(progress, "waiting-global-gate", "Diğer veri yazma işlemlerinin tamamlanması bekleniyor.");
        await canonicalWorkSynchronizer.AcquireWriteGateAsync(cancellationToken);
        Report(progress, "waiting-researcher-lock", "Akademisyene ait etkin işlemlerin tamamlanması bekleniyor.");
        await canonicalWorkSynchronizer.AcquireResearcherLockAsync(personelId, cancellationToken);
        // The lock may have waited behind a collector using another DbContext; reload saved state.
        dbContext.ChangeTracker.Clear();

        Report(progress, "loading", "Kayıtlı akademisyen ve sağlayıcı verileri yükleniyor.");
        Researcher researcher = await dbContext.Researchers
            .SingleOrDefaultAsync(value => value.PersonelId == personelId, cancellationToken)
            ?? throw new ArgumentException("Akademisyen kaydı bulunamadı.");

        OpenAlexMetrics? openAlex = await dbContext.OpenAlexProfiles.AsNoTracking()
            .Where(value => value.PersonelId == personelId)
            .Select(value => new OpenAlexMetrics(value.CitedByCount, value.HIndex, value.I10Index,
                value.WorksCount, value.TwoYearMeanCitedness, value.LastUpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);
        ScholarMetrics? scholar = await dbContext.GoogleScholarProfiles.AsNoTracking()
            .Where(value => value.PersonelId == personelId)
            .Select(value => new ScholarMetrics(value.CitationCount, value.HIndex, value.I10Index,
                value.DocumentsCount, value.CitationCountRecent, value.HIndexRecent,
                value.I10IndexRecent, value.MetricsSinceYear, value.LastUpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);
        ScopusMetrics? scopus = await dbContext.ScopusProfiles.AsNoTracking()
            .Where(value => value.PersonelId == personelId)
            .Select(value => new ScopusMetrics(value.CitationCount, value.HIndex,
                value.DocumentsCount, value.LastUpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);
        WebOfScienceMetrics? webOfScience = await dbContext.WebOfScienceProfiles.AsNoTracking()
            .Where(value => value.PersonelId == personelId)
            .Select(value => new WebOfScienceMetrics(value.Id, value.TotalTimesCited, value.HIndex,
                value.DocumentsCount, value.LastUpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);
        List<int?> timesCited = webOfScience is null
            ? []
            : await dbContext.WebOfScienceWorks.AsNoTracking()
                .Where(value => value.WebOfScienceProfileId == webOfScience.Id)
                .Select(value => value.TimesCited)
                .ToListAsync(cancellationToken);

        object?[] before = MetricValues(researcher);

        Report(progress, "calculating-openalex", "OpenAlex metrikleri hesaplanıyor.");
        ApplyOpenAlex(researcher, openAlex);
        Report(progress, "calculating-google-scholar", "Google Scholar metrikleri hesaplanıyor.");
        ApplyScholar(researcher, scholar);
        Report(progress, "calculating-scopus", "Scopus metrikleri hesaplanıyor.");
        ApplyScopus(researcher, scopus);
        Report(progress, "calculating-web-of-science", "Web of Science metrikleri hesaplanıyor.");
        ApplyWebOfScience(researcher, webOfScience, timesCited);

        if (!before.SequenceEqual(MetricValues(researcher)))
        {
            // Analysis already consumes this event as the researcher-level refresh signal.
            dbContext.CollectionChanges.Add(new()
            {
                EventId = Guid.NewGuid(),
                ChangeKind = "ResearcherCollected",
                PersonelId = personelId,
                OccurredAtUtc = DateTime.UtcNow,
                PayloadVersion = 1
            });
        }

        DateTime recalculatedAt = DateTime.UtcNow;
        Report(progress, "saving", "Hesaplanan metrikler kaydediliyor.");
        await dbContext.SaveChangesAsync(cancellationToken);
        Report(progress, "committing", "Metrik güncellemesi tamamlanıyor.");
        await transaction.CommitAsync(cancellationToken);
        return recalculatedAt;
    }

    private static void Report(
        IProgress<ResearcherMetricsProgress>? progress, string stage, string message) =>
        progress?.Report(new() { Stage = stage, Message = message });

    private static void ApplyOpenAlex(Researcher researcher, OpenAlexMetrics? profile)
    {
        researcher.OpenAlexCitationCount = profile?.CitedByCount;
        researcher.OpenAlexHIndex = profile?.HIndex;
        researcher.OpenAlexI10Index = profile?.I10Index;
        researcher.OpenAlexDocumentsCount = profile?.WorksCount;
        researcher.OpenAlexTwoYearMeanCitedness = profile?.TwoYearMeanCitedness;
        researcher.OpenAlexMetricsUpdatedAt = profile?.LastUpdatedAt;
    }

    private static void ApplyScholar(Researcher researcher, ScholarMetrics? profile)
    {
        researcher.ScholarCitationCount = profile?.CitationCount;
        researcher.ScholarHIndex = profile?.HIndex;
        researcher.ScholarI10Index = profile?.I10Index;
        researcher.ScholarDocumentsCount = profile?.DocumentsCount;
        researcher.ScholarCitationCountRecent = profile?.CitationCountRecent;
        researcher.ScholarHIndexRecent = profile?.HIndexRecent;
        researcher.ScholarI10IndexRecent = profile?.I10IndexRecent;
        researcher.ScholarMetricsSinceYear = profile?.MetricsSinceYear;
        researcher.ScholarMetricsUpdatedAt = profile?.LastUpdatedAt;
    }

    private static void ApplyScopus(Researcher researcher, ScopusMetrics? profile)
    {
        researcher.ScopusCitationCount = profile?.CitationCount;
        researcher.ScopusHIndex = profile?.HIndex;
        researcher.ScopusDocumentsCount = profile?.DocumentsCount;
        researcher.ScopusMetricsUpdatedAt = profile?.LastUpdatedAt;
    }

    private void ApplyWebOfScience(
        Researcher researcher, WebOfScienceMetrics? profile, IReadOnlyCollection<int?> timesCited)
    {
        int? totalTimesCited = CalculateTotalTimesCited(timesCited);
        int? hIndex = CalculateHIndex(timesCited);

        if (profile is not null)
        {
            WebOfScienceProfile update = new()
            {
                Id = profile.Id,
                PersonelId = researcher.PersonelId,
                TotalTimesCited = profile.TotalTimesCited,
                HIndex = profile.HIndex,
                DocumentsCount = profile.DocumentsCount,
                LastUpdatedAt = profile.LastUpdatedAt
            };
            dbContext.Attach(update);
            update.TotalTimesCited = totalTimesCited;
            update.HIndex = hIndex;
            update.DocumentsCount = timesCited.Count;
        }

        researcher.WosCitationCount = totalTimesCited;
        researcher.WosHIndex = hIndex;
        researcher.WosDocumentsCount = profile is null ? null : timesCited.Count;
        researcher.WosMetricsUpdatedAt = profile?.LastUpdatedAt;
    }

    private static int? CalculateHIndex(IReadOnlyCollection<int?> citationCounts)
    {
        if (citationCounts.Count == 0 || citationCounts.Any(count => !count.HasValue))
            return null;

        List<int> sorted = citationCounts.Select(count => count!.Value)
            .OrderByDescending(count => count).ToList();
        for (int index = 0; index < sorted.Count; index++)
            if (sorted[index] < index + 1)
                return index;
        return sorted.Count;
    }

    private static int? CalculateTotalTimesCited(IReadOnlyCollection<int?> citationCounts)
    {
        if (citationCounts.Count == 0 || citationCounts.Any(count => !count.HasValue))
            return null;
        return citationCounts.Sum(count => count!.Value);
    }

    internal static int? CalculateHIndex(IReadOnlyCollection<WebOfScienceWork> works)
        => CalculateHIndex(works.Select(work => work.TimesCited).ToList());

    internal static int? CalculateTotalTimesCited(IReadOnlyCollection<WebOfScienceWork> works)
        => CalculateTotalTimesCited(works.Select(work => work.TimesCited).ToList());

    private sealed record OpenAlexMetrics(
        int? CitedByCount, int? HIndex, int? I10Index, int? WorksCount,
        decimal? TwoYearMeanCitedness, DateTime LastUpdatedAt);

    private sealed record ScholarMetrics(
        int? CitationCount, int? HIndex, int? I10Index, int DocumentsCount,
        int? CitationCountRecent, int? HIndexRecent, int? I10IndexRecent,
        int? MetricsSinceYear, DateTime LastUpdatedAt);

    private sealed record ScopusMetrics(
        int? CitationCount, int? HIndex, int? DocumentsCount, DateTime LastUpdatedAt);

    private sealed record WebOfScienceMetrics(
        int Id, int? TotalTimesCited, int? HIndex, int DocumentsCount, DateTime LastUpdatedAt);

    private static object?[] MetricValues(Researcher value) =>
    [
        value.WosCitationCount, value.WosHIndex, value.WosDocumentsCount, value.WosMetricsUpdatedAt,
        value.OpenAlexCitationCount, value.OpenAlexHIndex, value.OpenAlexI10Index,
        value.OpenAlexDocumentsCount, value.OpenAlexTwoYearMeanCitedness, value.OpenAlexMetricsUpdatedAt,
        value.ScopusCitationCount, value.ScopusHIndex, value.ScopusDocumentsCount, value.ScopusMetricsUpdatedAt,
        value.ScholarCitationCount, value.ScholarHIndex, value.ScholarI10Index,
        value.ScholarDocumentsCount, value.ScholarCitationCountRecent, value.ScholarHIndexRecent,
        value.ScholarI10IndexRecent, value.ScholarMetricsSinceYear, value.ScholarMetricsUpdatedAt
    ];
}
