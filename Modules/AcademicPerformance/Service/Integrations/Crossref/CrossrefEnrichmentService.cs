using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Crossref;

public sealed class CrossrefEnrichmentService(AcademicDbContext dbContext, CrossrefClient client,
    IConfiguration configuration)
{
    public async Task<int> EnrichAsync(string personelId, CancellationToken cancellationToken = default)
    {
        if (!configuration.GetValue("ProviderRequestLimits:Crossref:Enabled", true)) return 0;
        int cacheHours = configuration.GetValue("Crossref:CacheMaxAgeHours", 720);
        DateTime freshAfter = DateTime.UtcNow.AddHours(-Math.Max(1, cacheHours));
        List<string> dois = (await dbContext.AcademicWorks.AsNoTracking()
            .Where(x => x.PersonelId == personelId && x.Provider != Works.Models.AcademicWorkProvider.Crossref && x.Doi != null)
            .Select(x => x.Doi!).ToListAsync(cancellationToken))
            .Select(CrossrefClient.NormalizeDoi).Where(IsDoi).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        List<CrossrefWork> cached = await dbContext.CrossrefWorks.Where(x => x.PersonelId == personelId).ToListAsync(cancellationToken);
        int fetched = 0;
        foreach (string doi in dois)
        {
            CrossrefWork? existing = cached.FirstOrDefault(x => x.Doi == doi);
            if (existing is not null && existing.FetchedAt >= freshAfter) continue;
            CrossrefWork incoming = await client.GetAsync(personelId, doi, cancellationToken);
            if (existing is null) { dbContext.CrossrefWorks.Add(incoming); cached.Add(incoming); }
            else Merge(existing, incoming);
            await dbContext.SaveChangesAsync(cancellationToken);
            fetched++;
        }
        return fetched;
    }

    private static bool IsDoi(string value) => value.StartsWith("10.", StringComparison.Ordinal) && value.Contains('/');
    private static void Merge(CrossrefWork target, CrossrefWork source)
    {
        target.FetchedAt = source.FetchedAt; target.Found = source.Found;
        if (!source.Found) return;
        target.Title = source.Title ?? target.Title; target.Authors = source.Authors ?? target.Authors;
        target.ContainerTitle = source.ContainerTitle ?? target.ContainerTitle; target.Type = source.Type ?? target.Type;
        target.PublicationYear = source.PublicationYear ?? target.PublicationYear;
        target.PublicationDate = source.PublicationDate ?? target.PublicationDate;
        target.CitedByCount = source.CitedByCount ?? target.CitedByCount; target.Url = source.Url ?? target.Url;
        target.RawDataJson = source.RawDataJson;
    }
}
