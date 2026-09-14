using System.Text.Json;
using System.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;
using AcademicCollectorDemo.Modules.AcademicPerformance.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.SemanticScholar;

public sealed class SemanticScholarWorkSourceSynchronizer(
    AcademicDbContext dbContext,
    CanonicalWorkSynchronizer? canonicalWorkSynchronizer = null,
    ArticleSummaryAutomationScheduler? articleSummaryScheduler = null,
    PublicationMetricsRefreshScheduler? publicationMetricsScheduler = null)
{
    private sealed record OpenAccessPdf(string Url, bool IsOpenAccess);

    public async Task<int> SyncAsync(string personelId, CancellationToken cancellationToken = default)
    {
        List<AcademicWork> works = await dbContext.AcademicWorks.Include(x => x.Sources)
            .Where(x => x.PersonelId == personelId && x.Doi != null).ToListAsync(cancellationToken);
        Dictionary<string, List<AcademicWork>> worksByDoi = works
            .Select(x => (Work: x, Doi: SemanticScholarClient.NormalizeDoi(x.Doi)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Doi))
            .GroupBy(x => x.Doi, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Select(value => value.Work).ToList(), StringComparer.OrdinalIgnoreCase);
        if (worksByDoi.Count == 0) return 0;

        List<SemanticScholarPaper> papers = await dbContext.SemanticScholarPapers.AsNoTracking()
            .Where(x => x.Found && worksByDoi.Keys.Contains(x.NormalizedDoi) && x.OpenAccessPdfJson != null)
            .ToListAsync(cancellationToken);
        int added = 0;
        foreach (SemanticScholarPaper paper in papers)
        {
            OpenAccessPdf? pdf = ReadOpenAccessPdf(paper.OpenAccessPdfJson);
            if (pdf is null || !worksByDoi.TryGetValue(paper.NormalizedDoi, out List<AcademicWork>? matching)) continue;
            foreach (AcademicWork work in matching)
            {
                if (work.Sources.Any(x => x.Origin == "SemanticScholar.OpenAccessPdf" && x.Url == pdf.Url)) continue;
                work.Sources.Add(new AcademicWorkSource
                {
                    Url = pdf.Url, Kind = "Pdf", Origin = "SemanticScholar.OpenAccessPdf",
                    IsOpenAccess = pdf.IsOpenAccess
                });
                added++;
            }
        }
        if (added > 0)
        {
            IDbContextTransaction? ownedTransaction = null;
            if (dbContext.Database.CurrentTransaction is null)
            {
                ownedTransaction = await dbContext.Database.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted, cancellationToken);
            }
            try
            {
                if (canonicalWorkSynchronizer is not null)
                    await canonicalWorkSynchronizer.AcquireWriteGateAsync(cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                if (publicationMetricsScheduler is not null)
                    await publicationMetricsScheduler.ScheduleAsync(personelId, cancellationToken);
                if (articleSummaryScheduler is not null)
                {
                    int[] workIds = works.Select(work => work.Id).ToArray();
                    int[] canonicalWorkIds = await dbContext.CanonicalWorkObservations.AsNoTracking()
                        .Where(observation => workIds.Contains(observation.AcademicWorkId))
                        .Select(observation => observation.CanonicalWorkId)
                        .Distinct().ToArrayAsync(cancellationToken);
                    await articleSummaryScheduler.ScheduleAsync(canonicalWorkIds, cancellationToken);
                }
                if (ownedTransaction is not null)
                    await ownedTransaction.CommitAsync(cancellationToken);
            }
            catch
            {
                if (ownedTransaction is not null)
                    await ownedTransaction.RollbackAsync(CancellationToken.None);
                throw;
            }
            finally
            {
                if (ownedTransaction is not null)
                    await ownedTransaction.DisposeAsync();
            }
        }
        return added;
    }

    private static OpenAccessPdf? ReadOpenAccessPdf(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("url", out JsonElement value) || value.ValueKind != JsonValueKind.String)
                return null;
            string? url = value.GetString()?.Trim();
            if (url is null || url.Length > 2000 || !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
                uri.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(uri.UserInfo)) return null;
            bool isOpenAccess = !document.RootElement.TryGetProperty("status", out JsonElement status) ||
                status.ValueKind != JsonValueKind.String ||
                !string.Equals(status.GetString(), "CLOSED", StringComparison.OrdinalIgnoreCase);
            return new(uri.AbsoluteUri, isOpenAccess);
        }
        catch (JsonException) { return null; }
    }
}
