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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;

public sealed class ArticleSummaryWorkflow(AcademicDbContext database, SafeArticleFetcher fetcher,
    ArticlePdfExtractor pdfExtractor, ArticleHtmlExtractor htmlExtractor, ArticleMetadataEnricher metadataEnricher,
    ArticleSummaryServiceClient client, IOptions<ArticleSummaryOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<SavedArticleSummaryResponse?> SummarizeAsync(string personelId, int academicWorkId,
        string language, CancellationToken cancellationToken)
    {
        AcademicWork? work = await database.AcademicWorks.AsNoTracking().Include(x => x.Sources)
            .SingleOrDefaultAsync(x => x.Id == academicWorkId && x.PersonelId == personelId, cancellationToken);
        if (work is null) return null;
        using CancellationTokenSource total = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        total.CancelAfter(TimeSpan.FromSeconds(options.Value.TotalTimeoutSeconds));

        List<AcademicWork> sourceWorks = await LoadSameDoiWorksAsync(work, total.Token);
        string? recoveredAbstract = sourceWorks.Select(x => x.Abstract).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        recoveredAbstract ??= sourceWorks.Select(x => ArticleAbstractReader.FromPayload(x.ProviderPayload, x.Provider.ToString()))
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
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
        snapshot = snapshot with { SourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant() };
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
        await PersistAsync(work.Id, personelId, recoveredAbstract, enrichment?.Sources ?? [], sourceUrl,
            snapshot.SourceKind, saved, total.Token);
        return Map(saved, report);
    }

    public async Task<SavedArticleSummaryResponse?> GetLatestAsync(string personelId, int academicWorkId, CancellationToken cancellationToken)
    {
        SavedArticleSummary? saved = await database.ArticleSummaries.AsNoTracking()
            .Where(x => x.PersonelId == personelId && x.OriginalAcademicWorkId == academicWorkId)
            .OrderByDescending(x => x.Id).FirstOrDefaultAsync(cancellationToken);
        return saved is null ? null : Map(saved, JsonSerializer.Deserialize<ArticleSummaryReport>(saved.ReportJson, JsonOptions)!);
    }

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
                    return new(await pdfExtractor.ExtractAsync(fetched.Bytes, language, cancellationToken), fetched.FinalUri.ToString(), discoveredAbstract);
                discoveredAbstract ??= htmlExtractor.TryExtractAbstract(fetched.Bytes, fetched.FinalUri);
                return new(htmlExtractor.Extract(fetched.Bytes, fetched.FinalUri, language), fetched.FinalUri.ToString(), discoveredAbstract);
            }
            catch (Exception exception) when (exception is ArticleSourceException or HttpRequestException or SocketException)
            { failures.Add($"{candidate.Origin}: {SafeFailure(exception)}"); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested && !totalToken.IsCancellationRequested)
            {
                failures.Add("Source acquisition budget expired; the recovered abstract remains available as fallback.");
                return new(null, null, discoveredAbstract);
            }
        }
        return new(null, null, discoveredAbstract);
    }

    private async Task PersistAsync(int workId, string personelId, string? recoveredAbstract,
        IReadOnlyList<AcademicWorkSource> enrichedSources, string? sourceUrl, string sourceKind,
        SavedArticleSummary saved, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        AcademicWork? owned = await database.AcademicWorks.Include(x => x.Sources)
            .SingleOrDefaultAsync(x => x.Id == workId && x.PersonelId == personelId, cancellationToken);
        if (owned is null) throw new ArticleSourceException("The article changed during summarization; no report was saved.");
        if (string.IsNullOrWhiteSpace(owned.Abstract) && !string.IsNullOrWhiteSpace(recoveredAbstract)) owned.Abstract = recoveredAbstract;
        foreach (AcademicWorkSource source in enrichedSources) AddSource(owned, source.Url, source.Kind, source.Origin, source.IsOpenAccess);
        if (!string.IsNullOrWhiteSpace(sourceUrl))
        {
            if (sourceKind == "pdf") { owned.FullTextUrl = sourceUrl; AddSource(owned, sourceUrl, "Pdf", "ResolvedPdf", null); }
            else if (sourceKind == "html") AddSource(owned, sourceUrl, "Html", "ResolvedHtml", null);
        }
        database.ArticleSummaries.Add(saved);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static void AddSource(AcademicWork work, string url, string kind, string origin, bool? openAccess)
    {
        if (string.IsNullOrWhiteSpace(url) || work.Sources.Any(x => x.Url == url)) return;
        work.Sources.Add(new() { Url = url, Kind = kind, Origin = origin, IsOpenAccess = openAccess });
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
    private static SavedArticleSummaryResponse Map(SavedArticleSummary value, ArticleSummaryReport report) =>
        new(value.Id, value.OriginalAcademicWorkId, value.PersonelId, value.SavedAt, value.SourceUrl,
            report.ExtractionMethod is null ? report with
            { ExtractionMethod = ArticleSummaryServiceClient.GetExtractionMethod(value.SourceKind, value.ExtractionVersion) } : report);
    internal sealed record Acquisition(SummarizeArticleRequest? Snapshot, string? Url, string? Abstract);
}
