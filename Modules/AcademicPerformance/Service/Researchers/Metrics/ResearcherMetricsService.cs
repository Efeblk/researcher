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
            .Include(value => value.OpenAlexProfile)
            .Include(value => value.GoogleScholarProfile)
            .Include(value => value.ScopusProfile)
            .Include(value => value.WebOfScienceProfile)
                .ThenInclude(profile => profile!.Works)
            .SingleOrDefaultAsync(value => value.PersonelId == personelId, cancellationToken)
            ?? throw new ArgumentException("Akademisyen kaydı bulunamadı.");

        object?[] before = MetricValues(researcher);

        Report(progress, "calculating-openalex", "OpenAlex metrikleri hesaplanıyor.");
        ApplyOpenAlex(researcher);
        Report(progress, "calculating-google-scholar", "Google Scholar metrikleri hesaplanıyor.");
        ApplyScholar(researcher);
        Report(progress, "calculating-scopus", "Scopus metrikleri hesaplanıyor.");
        ApplyScopus(researcher);
        Report(progress, "calculating-web-of-science", "Web of Science metrikleri hesaplanıyor.");
        ApplyWebOfScience(researcher);

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

    private static void ApplyOpenAlex(Researcher researcher)
    {
        var profile = researcher.OpenAlexProfile;
        researcher.OpenAlexCitationCount = profile?.CitedByCount;
        researcher.OpenAlexHIndex = profile?.HIndex;
        researcher.OpenAlexI10Index = profile?.I10Index;
        researcher.OpenAlexDocumentsCount = profile?.WorksCount;
        researcher.OpenAlexTwoYearMeanCitedness = profile?.TwoYearMeanCitedness;
        researcher.OpenAlexMetricsUpdatedAt = profile?.LastUpdatedAt;
    }

    private static void ApplyScholar(Researcher researcher)
    {
        var profile = researcher.GoogleScholarProfile;
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

    private static void ApplyScopus(Researcher researcher)
    {
        var profile = researcher.ScopusProfile;
        researcher.ScopusCitationCount = profile?.CitationCount;
        researcher.ScopusHIndex = profile?.HIndex;
        researcher.ScopusDocumentsCount = profile?.DocumentsCount;
        researcher.ScopusMetricsUpdatedAt = profile?.LastUpdatedAt;
    }

    private static void ApplyWebOfScience(Researcher researcher)
    {
        WebOfScienceProfile? profile = researcher.WebOfScienceProfile;
        List<WebOfScienceWork> works = profile?.Works ?? [];
        int? totalTimesCited = CalculateTotalTimesCited(works);
        int? hIndex = CalculateHIndex(works);

        if (profile is not null)
        {
            profile.TotalTimesCited = totalTimesCited;
            profile.HIndex = hIndex;
            profile.DocumentsCount = works.Count;
        }

        researcher.WosCitationCount = totalTimesCited;
        researcher.WosHIndex = hIndex;
        researcher.WosDocumentsCount = profile?.DocumentsCount;
        researcher.WosMetricsUpdatedAt = profile?.LastUpdatedAt;
    }

    internal static int? CalculateHIndex(IReadOnlyCollection<WebOfScienceWork> works)
    {
        if (works.Count == 0 || works.Any(work => !work.TimesCited.HasValue))
            return null;

        List<int> citationCounts = works.Select(work => work.TimesCited!.Value)
            .OrderByDescending(count => count).ToList();
        for (int index = 0; index < citationCounts.Count; index++)
            if (citationCounts[index] < index + 1)
                return index;
        return citationCounts.Count;
    }

    internal static int? CalculateTotalTimesCited(IReadOnlyCollection<WebOfScienceWork> works)
    {
        if (works.Count == 0 || works.Any(work => !work.TimesCited.HasValue))
            return null;
        return works.Sum(work => work.TimesCited!.Value);
    }

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
