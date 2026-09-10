using System.Security.Cryptography;
using System.Text;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.SemanticScholar;

public sealed class SemanticScholarEnrichmentService(
    AcademicDbContext dbContext, SemanticScholarClient client,
    IConfiguration configuration, IOptions<SemanticScholarOptions> configured)
{
    public async Task<int> EnrichAsync(string personelId, CancellationToken cancellationToken = default)
    {
        if (!configuration.GetValue("ProviderRequestLimits:SemanticScholar:Enabled", true)) return 0;
        SemanticScholarOptions options = configured.Value;
        DateTime freshAfter = DateTime.UtcNow.AddHours(-options.CacheMaxAgeHours);
        List<string> dois = (await dbContext.AcademicWorks.AsNoTracking()
            .Where(x => x.PersonelId == personelId && x.Doi != null)
            .Select(x => x.Doi!).ToListAsync(cancellationToken))
            .Select(SemanticScholarClient.NormalizeDoi).Where(IsDoi)
            .Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
        List<string> fresh = await dbContext.SemanticScholarPapers.AsNoTracking()
            .Where(x => dois.Contains(x.NormalizedDoi) && x.FetchedAt >= freshAfter &&
                (!x.Found || x.CitationsComplete || x.CitationsFetched >= options.MaximumCitationsPerPaper))
            .Select(x => x.NormalizedDoi).ToListAsync(cancellationToken);
        List<string> pending = dois.Except(fresh, StringComparer.OrdinalIgnoreCase).ToList();
        int completed = 0;
        foreach (string doi in pending.Take(options.MaximumPapersPerRun))
        {
            try { if (await EnrichDoiAsync(doi, freshAfter, options, cancellationToken)) completed++; }
            catch (Exception exception) when (completed > 0 && exception is not OperationCanceledException)
            { throw new SemanticScholarPartialEnrichmentException(completed, exception); }
        }
        if (pending.Count > options.MaximumPapersPerRun)
        {
            DateTime retryAt = DateTime.UtcNow.AddSeconds(5);
            ProviderCallScope.Record("SemanticScholar", true, retryAt, true);
            throw new SemanticScholarPartialEnrichmentException(completed,
                new InvalidOperationException($"{pending.Count - options.MaximumPapersPerRun} DOI remains for a later bounded run."));
        }
        return completed;
    }

    private async Task<bool> EnrichDoiAsync(string doi, DateTime freshAfter,
        SemanticScholarOptions options, CancellationToken cancellationToken)
    {
        string connectionString = configuration.GetConnectionString("AcademicDatabase")!;
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(doi)))[..32];
        await using SqlApplicationLock? gate = await SqlApplicationLock.TryAcquireAsync(
            connectionString, "AcademicCollector.SemanticScholar." + hash, 30000, cancellationToken);
        if (gate is null) throw new TimeoutException("Semantic Scholar DOI cache is busy; retry later.");
        SemanticScholarPaper? existing = await dbContext.SemanticScholarPapers
            .Include(x => x.Citations).ThenInclude(x => x.Contexts)
            .SingleOrDefaultAsync(x => x.NormalizedDoi == doi, cancellationToken);
        if (existing is not null && existing.FetchedAt >= freshAfter &&
            (!existing.Found || existing.CitationsComplete || existing.CitationsFetched >= options.MaximumCitationsPerPaper)) return false;

        SemanticScholarSnapshot snapshot = await client.GetAsync(doi, cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (existing is null)
        {
            existing = snapshot.Paper;
            dbContext.SemanticScholarPapers.Add(existing);
        }
        else
        {
            dbContext.SemanticScholarCitationContexts.RemoveRange(existing.Citations.SelectMany(x => x.Contexts));
            dbContext.SemanticScholarCitations.RemoveRange(existing.Citations);
            Copy(existing, snapshot.Paper);
            existing.Citations.Clear();
            foreach (SemanticScholarCitation citation in snapshot.Citations) existing.Citations.Add(citation);
        }
        if (existing == snapshot.Paper)
            foreach (SemanticScholarCitation citation in snapshot.Citations) existing.Citations.Add(citation);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static bool IsDoi(string value) => value.StartsWith("10.", StringComparison.Ordinal) && value.Contains('/');
    private static void Copy(SemanticScholarPaper target, SemanticScholarPaper source)
    {
        target.PaperId = source.PaperId; target.Found = source.Found; target.FetchedAt = source.FetchedAt;
        target.CitationTotal = source.CitationTotal; target.CitationsFetched = source.CitationsFetched; target.CitationsComplete = source.CitationsComplete;
        target.Title = source.Title; target.Abstract = source.Abstract; target.AuthorsJson = source.AuthorsJson;
        target.Year = source.Year; target.Venue = source.Venue; target.PublicationDate = source.PublicationDate; target.JournalJson = source.JournalJson; target.PublicationTypesJson = source.PublicationTypesJson;
        target.FieldsOfStudyJson = source.FieldsOfStudyJson; target.OpenAccessPdfJson = source.OpenAccessPdfJson;
        target.CitationCount = source.CitationCount; target.ReferenceCount = source.ReferenceCount;
        target.InfluentialCitationCount = source.InfluentialCitationCount; target.Url = source.Url;
        target.TldrJson = source.TldrJson; target.TextAvailability = source.TextAvailability; target.RawDataJson = source.RawDataJson;
    }
}
