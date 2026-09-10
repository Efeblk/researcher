using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.GoogleScholar;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data;

internal static class ResearcherProviderMetricsSynchronizer
{
    public static void Synchronize(AcademicDbContext dbContext)
    {
        ChangeTracker changeTracker = dbContext.ChangeTracker;
        changeTracker.DetectChanges();

        foreach (EntityEntry<OpenAlexProfile> entry in Changed<OpenAlexProfile>(changeTracker))
        {
            Researcher? researcher = FindResearcher(dbContext, entry.Entity.PersonelId, entry.Entity.Researcher);
            if (researcher is not null)
                ApplyOpenAlex(researcher, entry.State == EntityState.Deleted ? null : entry.Entity);
        }

        foreach (EntityEntry<GoogleScholarProfile> entry in Changed<GoogleScholarProfile>(changeTracker))
        {
            Researcher? researcher = FindResearcher(dbContext, entry.Entity.PersonelId, entry.Entity.Researcher);
            if (researcher is not null)
                ApplyScholar(researcher, entry.State == EntityState.Deleted ? null : entry.Entity);
        }

        foreach (EntityEntry<WebOfScienceProfile> entry in Changed<WebOfScienceProfile>(changeTracker))
        {
            Researcher? researcher = FindResearcher(dbContext, entry.Entity.PersonelId, entry.Entity.Researcher);
            if (researcher is not null)
                ApplyWebOfScience(researcher, entry.State == EntityState.Deleted ? null : entry.Entity);
        }
    }

    public static async Task SynchronizeAsync(
        AcademicDbContext dbContext,
        CancellationToken cancellationToken)
    {
        ChangeTracker changeTracker = dbContext.ChangeTracker;
        changeTracker.DetectChanges();

        foreach (EntityEntry<OpenAlexProfile> entry in Changed<OpenAlexProfile>(changeTracker))
        {
            Researcher? researcher = await FindResearcherAsync(
                dbContext, entry.Entity.PersonelId, entry.Entity.Researcher, cancellationToken);
            if (researcher is not null)
                ApplyOpenAlex(researcher, entry.State == EntityState.Deleted ? null : entry.Entity);
        }

        foreach (EntityEntry<GoogleScholarProfile> entry in Changed<GoogleScholarProfile>(changeTracker))
        {
            Researcher? researcher = await FindResearcherAsync(
                dbContext, entry.Entity.PersonelId, entry.Entity.Researcher, cancellationToken);
            if (researcher is not null)
                ApplyScholar(researcher, entry.State == EntityState.Deleted ? null : entry.Entity);
        }

        foreach (EntityEntry<WebOfScienceProfile> entry in Changed<WebOfScienceProfile>(changeTracker))
        {
            Researcher? researcher = await FindResearcherAsync(
                dbContext, entry.Entity.PersonelId, entry.Entity.Researcher, cancellationToken);
            if (researcher is not null)
                ApplyWebOfScience(researcher, entry.State == EntityState.Deleted ? null : entry.Entity);
        }
    }

    private static IEnumerable<EntityEntry<TProfile>> Changed<TProfile>(ChangeTracker changeTracker)
        where TProfile : class
    {
        return changeTracker.Entries<TProfile>().Where(entry =>
            entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList();
    }

    private static Researcher? FindResearcher(
        AcademicDbContext dbContext,
        string personelId,
        Researcher? navigation)
    {
        return navigation ?? dbContext.ChangeTracker.Entries<Researcher>()
            .Select(entry => entry.Entity)
            .FirstOrDefault(researcher => researcher.PersonelId == personelId) ??
            dbContext.Researchers.FirstOrDefault(researcher => researcher.PersonelId == personelId);
    }

    private static async Task<Researcher?> FindResearcherAsync(
        AcademicDbContext dbContext,
        string personelId,
        Researcher? navigation,
        CancellationToken cancellationToken)
    {
        Researcher? tracked = navigation ?? dbContext.ChangeTracker.Entries<Researcher>()
            .Select(entry => entry.Entity)
            .FirstOrDefault(researcher => researcher.PersonelId == personelId);

        return tracked ?? await dbContext.Researchers.FirstOrDefaultAsync(
            researcher => researcher.PersonelId == personelId,
            cancellationToken);
    }

    private static void ApplyOpenAlex(Researcher researcher, OpenAlexProfile? profile)
    {
        researcher.OpenAlexCitationCount = profile?.CitedByCount;
        researcher.OpenAlexHIndex = profile?.HIndex;
        researcher.OpenAlexI10Index = profile?.I10Index;
        researcher.OpenAlexDocumentsCount = profile?.WorksCount;
        researcher.OpenAlexTwoYearMeanCitedness = profile?.TwoYearMeanCitedness;
        researcher.OpenAlexMetricsUpdatedAt = profile?.LastUpdatedAt;
    }

    private static void ApplyScholar(Researcher researcher, GoogleScholarProfile? profile)
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

    private static void ApplyWebOfScience(Researcher researcher, WebOfScienceProfile? profile)
    {
        researcher.WosCitationCount = profile?.TotalTimesCited;
        researcher.WosHIndex = profile?.HIndex;
        researcher.WosDocumentsCount = profile?.DocumentsCount;
        researcher.WosMetricsUpdatedAt = profile?.LastUpdatedAt;
    }
}
