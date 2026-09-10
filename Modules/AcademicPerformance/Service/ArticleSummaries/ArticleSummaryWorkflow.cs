using System.Security.Cryptography;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Data;
using AcademicCollector.Analysis.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;

public sealed class ArticleSummaryWorkflow(AcademicDbContext database, SafeArticleFetcher fetcher,
    ArticlePdfExtractor extractor, ArticleSummaryServiceClient client, IOptions<ArticleSummaryOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<SavedArticleSummaryResponse?> SummarizeAsync(string personelId, int academicWorkId, string language, CancellationToken cancellationToken)
    {
        AcademicWork? work = await database.AcademicWorks.AsNoTracking().SingleOrDefaultAsync(x => x.Id == academicWorkId && x.PersonelId == personelId, cancellationToken);
        if (work is null) return null;
        using CancellationTokenSource total = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        total.CancelAfter(TimeSpan.FromSeconds(options.Value.TotalTimeoutSeconds));
        SummarizeArticleRequest snapshot;
        string? sourceUrl = null;
        SummarizeArticleRequest? acquired = null;
        foreach (string candidate in ValidUrls(work.FullTextUrl, work.OpenAccessUrl, work.Link))
        {
            try
            {
                var fetched = await fetcher.FetchPdfAsync(new Uri(candidate), total.Token);
                acquired = extractor.Extract(fetched.Bytes, language);
                sourceUrl = fetched.FinalUri.ToString();
                break;
            }
            catch (Exception exception) when (exception is ArticleSourceException or HttpRequestException or SocketException) { }
            catch (OperationCanceledException) when (!total.IsCancellationRequested) { }
        }
        if (acquired is not null) snapshot = acquired;
        else if (!string.IsNullOrWhiteSpace(work.Abstract))
            snapshot = AbstractSnapshot(work.Abstract!, language, ValidUrls(work.FullTextUrl, work.OpenAccessUrl, work.Link).Any()
                ? "Saved full-text sources were unavailable or unusable; only the saved database abstract was summarized."
                : "No saved full-text URL exists; only the saved database abstract was summarized.");
        else
            throw new ArticleSourceException("No usable full-text source or abstract is saved for this article.");

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
        await using (var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, total.Token))
        {
            bool stillOwned = await database.AcademicWorks.AnyAsync(x => x.Id == work.Id && x.PersonelId == personelId, total.Token);
            if (!stillOwned) throw new ArticleSourceException("The article changed during summarization; no report was saved.");
            database.ArticleSummaries.Add(saved);
            await database.SaveChangesAsync(total.Token);
            await transaction.CommitAsync(total.Token);
        }
        return Map(saved, report);
    }

    public async Task<SavedArticleSummaryResponse?> GetLatestAsync(string personelId, int academicWorkId, CancellationToken cancellationToken)
    {
        SavedArticleSummary? saved = await database.ArticleSummaries.AsNoTracking()
            .Where(x => x.PersonelId == personelId && x.OriginalAcademicWorkId == academicWorkId)
            .OrderByDescending(x => x.Id).FirstOrDefaultAsync(cancellationToken);
        return saved is null ? null : Map(saved, JsonSerializer.Deserialize<ArticleSummaryReport>(saved.ReportJson, JsonOptions)!);
    }

    private static SummarizeArticleRequest AbstractSnapshot(string text, string language, string reason)
    {
        string canonical = text.Trim()[..Math.Min(text.Trim().Length, 24000)];
        IReadOnlyList<ArticlePage> pages = [new(null, canonical)];
        return new(language, "abstract", "", "database-abstract-spans-v2", pages, 1, true,
            text.Trim().Length > 24000 ? reason + " The abstract was truncated to 24,000 characters." : reason)
            { SourceSpans = ArticleSourceCatalog.Create(pages) };
    }
    private static IEnumerable<string> ValidUrls(params string?[] values) => values.Where(value =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo))
        .Select(value => value!).Distinct(StringComparer.OrdinalIgnoreCase);
    private static SavedArticleSummaryResponse Map(SavedArticleSummary value, ArticleSummaryReport report) =>
        new(value.Id, value.OriginalAcademicWorkId, value.PersonelId, value.SavedAt, value.SourceUrl, report);
}
