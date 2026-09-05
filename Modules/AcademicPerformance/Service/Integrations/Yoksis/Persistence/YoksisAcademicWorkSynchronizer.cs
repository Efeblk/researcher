using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using Microsoft.EntityFrameworkCore;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis.Persistence;

public sealed class YoksisAcademicWorkSynchronizer
{
    private static readonly Regex YearPattern = new(
        @"\b(?:19|20)\d{2}\b",
        RegexOptions.CultureInvariant);

    private readonly AcademicDbContext _dbContext;

    public YoksisAcademicWorkSynchronizer(AcademicDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<int> SyncAsync(
        int researcherId,
        YoksisCollectResponse response,
        bool isIncremental = false)
    {
        List<AcademicWork>? existingWorks = null;
        List<AcademicWork>? incomingWorks = null;
        HashSet<int>? matchedExistingIds = null;
        HashSet<string>? completedSourceTypes = null;

        existingWorks = await _dbContext.AcademicWorks
            .Where(work =>
                work.ResearcherId == researcherId &&
                work.Provider == AcademicWorkProvider.Yoksis)
            .ToListAsync();
        incomingWorks = CreateWorks(researcherId, response);
        matchedExistingIds = [];
        completedSourceTypes = isIncremental ? [] : GetCompletedSourceTypes(response);

        foreach (AcademicWork incomingWork in incomingWorks)
        {
            AcademicWork? existingWork = null;

            existingWork = existingWorks.FirstOrDefault(work =>
                !matchedExistingIds.Contains(work.Id) &&
                work.ProviderWorkId == incomingWork.ProviderWorkId);

            if (existingWork is null)
            {
                _dbContext.AcademicWorks.Add(incomingWork);
                continue;
            }

            CopyValues(incomingWork, existingWork);
            matchedExistingIds.Add(existingWork.Id);
        }

        foreach (AcademicWork existingWork in existingWorks)
        {
            if (!matchedExistingIds.Contains(existingWork.Id) &&
                !string.IsNullOrWhiteSpace(existingWork.SourceType) &&
                completedSourceTypes.Contains(existingWork.SourceType))
            {
                _dbContext.AcademicWorks.Remove(existingWork);
            }
        }

        await _dbContext.SaveChangesAsync();
        return incomingWorks.Count;
    }

    private static List<AcademicWork> CreateWorks(
        int researcherId,
        YoksisCollectResponse response)
    {
        List<AcademicWork>? works = null;

        works = [];

        foreach (YoksisOperationResult category in response.Categories)
        {
            foreach (Dictionary<string, string?> record in category.Records)
            {
                AcademicWork? work = null;

                work = CreateWork(
                    researcherId,
                    category.OperationName,
                    record);

                if (work is not null)
                {
                    works.Add(work);
                }
            }
        }

        return works
            .GroupBy(work => work.ProviderWorkId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
    }

    private static AcademicWork? CreateWork(
        int researcherId,
        string? operationName,
        Dictionary<string, string?> record)
    {
        AcademicWork? work = null;

        work = operationName switch
        {
            "getMakaleBilgisiDetayV1" => CreateArticle(researcherId, record),
            "getBildiriBilgisiDetayV1" => CreateConferencePaper(
                researcherId,
                record),
            "getKitapBilgisiDetayV1" => CreateBook(researcherId, record),
            "getPatentBilgisiDetayV1" => CreatePatent(researcherId, record),
            _ => null
        };

        if (work is null || string.IsNullOrWhiteSpace(work.Title))
        {
            return null;
        }

        work.Provider = AcademicWorkProvider.Yoksis;
        work.CategorySource = AcademicWorkCategorySource.Yoksis;
        work.SourceName = "YÖKSİS";
        if (string.IsNullOrWhiteSpace(work.SourceId))
        {
            // Without a provider ID, preserve distinct source records. Sorting
            // fields makes the fallback stable when SOAP field order changes.
            string canonicalRecord = JsonSerializer.Serialize(record
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value));
            work.ProviderWorkId = $"{work.SourceType}:generated:" +
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRecord)))
                    .ToLowerInvariant();
        }
        work.ProviderPayload = JsonSerializer.Serialize(record);
        work.SyncedAt = DateTime.UtcNow;
        return work;
    }

    private static AcademicWork CreateArticle(
        int researcherId,
        Dictionary<string, string?> record)
    {
        AcademicWork? work = null;

        work = CreateBaseWork(
            researcherId,
            "Makale",
            Get(record, "YAYIN_ID"),
            Get(record, "MAKALE_ADI"),
            Get(record, "YIL"),
            Get(record, "DOI"),
            Get(record, "YAZAR_ADI"),
            Get(record, "ERISIM_LINKI"));
        work.Category = AcademicWorkCategory.Article;
        work.RawType = Get(record, "MAKALE_TURU_AD") ??
            Get(record, "MAKALE_TURU_ID");
        work.Publication = Get(record, "DERGI_ADI");
        work.Volume = Get(record, "CILT");
        work.Issue = Get(record, "SAYI");
        work.FirstPage = Get(record, "ILK_SAYFA");
        work.LastPage = Get(record, "SON_SAYFA");
        work.Keywords = Get(record, "ANAHTAR_KELIME");
        work.Language = Get(record, "YAYIN_DILI_ADI") ??
            Get(record, "YAYIN_DILI");
        work.CitedByCount = ParseInteger(Get(record, "ATIF_SAYISI"));
        SetAccessInformation(work, record);
        return work;
    }

    private static AcademicWork CreateConferencePaper(
        int researcherId,
        Dictionary<string, string?> record)
    {
        AcademicWork? work = null;
        string? dateText = null;

        dateText = Get(record, "BASIM_TARIHI") ??
            Get(record, "ETKINLIK_BAS_TARIHI");
        work = CreateBaseWork(
            researcherId,
            "Bildiri",
            Get(record, "YAYIN_ID"),
            Get(record, "BILDIRI_ADI"),
            dateText,
            Get(record, "DOI"),
            Get(record, "YAZAR_ADI"),
            Get(record, "ERISIM_LINKI"));
        work.Category = AcademicWorkCategory.ConferencePaper;
        work.RawType = Get(record, "BILDIRI_TUR") ??
            Get(record, "BILDIRI_TUR_ID");
        work.Publication = Get(record, "ETKINLIK_ADI");
        work.Volume = Get(record, "CILT");
        work.Issue = Get(record, "SAYI");
        work.FirstPage = Get(record, "ILK_SAYFA");
        work.LastPage = Get(record, "SON_SAYFA");
        work.Keywords = Get(record, "ANAHTAR_KELIME");
        work.Language = Get(record, "YAYIN_DILI_ADI") ??
            Get(record, "YAYIN_DILI");
        work.CitedByCount = ParseInteger(Get(record, "ATIF_SAYISI"));
        SetAccessInformation(work, record);
        return work;
    }

    private static AcademicWork CreateBook(
        int researcherId,
        Dictionary<string, string?> record)
    {
        AcademicWork? work = null;
        string? bookTitle = null;
        string? chapterTitle = null;

        bookTitle = Get(record, "KITAP_ADI");
        chapterTitle = Get(record, "BOLUM_ADI");
        work = CreateBaseWork(
            researcherId,
            "Kitap",
            Get(record, "YAYIN_ID"),
            chapterTitle ?? bookTitle,
            Get(record, "YIL"),
            doi: null,
            Get(record, "YAZAR_ADI"),
            Get(record, "ERISIM_LINKI"));
        work.Category = string.IsNullOrWhiteSpace(chapterTitle)
            ? AcademicWorkCategory.Book
            : AcademicWorkCategory.BookChapter;
        work.RawType = Get(record, "KITAP_TUR") ??
            Get(record, "KITAP_TUR_ID");
        work.Publication = chapterTitle is null
            ? Get(record, "YAYIN_EVI")
            : bookTitle;
        work.FirstPage = Get(record, "BOLUM_ILK_SAYFA");
        work.LastPage = Get(record, "BOLUM_SON_SAYFA");
        work.Keywords = Get(record, "ANAHTAR_KELIME");
        work.Language = Get(record, "YAYIN_DILI_ADI") ??
            Get(record, "YAYIN_DILI");
        work.CitedByCount = ParseInteger(Get(record, "ATIF_SAYISI"));
        SetAccessInformation(work, record);
        return work;
    }

    private static HashSet<string> GetCompletedSourceTypes(
        YoksisCollectResponse response)
    {
        HashSet<string>? sourceTypes = null;

        sourceTypes = [];

        foreach (YoksisOperationResult category in response.Categories)
        {
            if (!category.IsSuccess)
            {
                continue;
            }

            switch (category.OperationName)
            {
                case "getMakaleBilgisiDetayV1":
                    sourceTypes.Add("Makale");
                    break;
                case "getBildiriBilgisiDetayV1":
                    sourceTypes.Add("Bildiri");
                    break;
                case "getKitapBilgisiDetayV1":
                    sourceTypes.Add("Kitap");
                    break;
                case "getPatentBilgisiDetayV1":
                    sourceTypes.Add("Patent");
                    break;
            }
        }

        return sourceTypes;
    }

    private static AcademicWork CreatePatent(
        int researcherId,
        Dictionary<string, string?> record)
    {
        AcademicWork? work = null;

        work = CreateBaseWork(
            researcherId,
            "Patent",
            Get(record, "PATENT_ID"),
            Get(record, "PATENT_ADI"),
            Get(record, "PATENT_TARIHI"),
            doi: null,
            Get(record, "BULUS_SAHIPLERI"),
            link: null);
        work.Category = AcademicWorkCategory.Patent;
        work.RawType = Get(record, "KATEGORI") ??
            Get(record, "DOSYA_TIPI");
        work.Publication = Get(record, "KURUM_AD");
        return work;
    }

    private static AcademicWork CreateBaseWork(
        int researcherId,
        string sourceType,
        string? sourceId,
        string? title,
        string? dateText,
        string? doi,
        string? authors,
        string? link)
    {
        AcademicWork? work = null;

        work = new AcademicWork();
        work.ResearcherId = researcherId;
        work.ProviderWorkId = string.IsNullOrWhiteSpace(sourceId)
            ? string.Empty
            : $"{sourceType}:{sourceId}";
        work.Title = title;
        work.PublicationDate = ParseDate(dateText);
        work.PublicationYear = ParseYear(dateText) ??
            work.PublicationDate?.Year;
        work.Doi = doi;
        work.Authors = authors;
        work.Link = link;
        work.SourceId = sourceId;
        work.SourceType = sourceType;
        work.SourceUrl = link;
        return work;
    }

    private static void SetAccessInformation(
        AcademicWork work,
        Dictionary<string, string?> record)
    {
        string? accessType = null;

        accessType = Get(record, "ERISIM_TURU_AD") ??
            Get(record, "ERISIM_TURU");
        work.OpenAccessStatus = accessType;
        work.IsOpenAccess = accessType?.Contains(
            "açık",
            StringComparison.CurrentCultureIgnoreCase);
        work.HasFullText = !string.IsNullOrWhiteSpace(work.Link);
        work.FullTextUrl = work.Link;
    }

    private static string? Get(
        Dictionary<string, string?> record,
        string fieldName)
    {
        return record.GetValueOrDefault(fieldName);
    }

    private static int? ParseInteger(string? value)
    {
        int number = 0;

        return int.TryParse(
            value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out number)
            ? number
            : null;
    }

    private static int? ParseYear(string? value)
    {
        Match? match = null;
        int year = 0;

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (int.TryParse(value, out year) && year is >= 1900 and <= 2100)
        {
            return year;
        }

        match = YearPattern.Match(value);
        return match.Success && int.TryParse(match.Value, out year)
            ? year
            : null;
    }

    private static DateTime? ParseDate(string? value)
    {
        DateTime date = DateTime.MinValue;
        string[] formats =
        [
            "dd/MM/yyyy",
            "dd.MM.yyyy",
            "yyyy-MM-dd",
            "dd/MM/yyyy HH:mm:ss",
            "dd.MM.yyyy HH:mm:ss"
        ];

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTime.TryParseExact(
            value.Trim(),
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date)
            ? date
            : null;
    }

    private static void CopyValues(AcademicWork source, AcademicWork target)
    {
        target.ResearcherId = source.ResearcherId;
        target.Provider = source.Provider;
        target.ProviderWorkId = source.ProviderWorkId;
        target.Title = source.Title;
        target.PublicationYear = source.PublicationYear;
        target.PublicationDate = source.PublicationDate;
        target.Doi = source.Doi;
        target.RawType = source.RawType;
        target.Category = source.Category;
        target.CategorySource = source.CategorySource;
        target.CitedByCount = source.CitedByCount;
        target.Authors = source.Authors;
        target.Keywords = source.Keywords;
        target.Language = source.Language;
        target.Publication = source.Publication;
        target.Volume = source.Volume;
        target.Issue = source.Issue;
        target.FirstPage = source.FirstPage;
        target.LastPage = source.LastPage;
        target.Link = source.Link;
        target.SourceId = source.SourceId;
        target.SourceName = source.SourceName;
        target.SourceType = source.SourceType;
        target.SourceUrl = source.SourceUrl;
        target.IsOpenAccess = source.IsOpenAccess;
        target.OpenAccessStatus = source.OpenAccessStatus;
        target.HasFullText = source.HasFullText;
        target.FullTextUrl = source.FullTextUrl;
        target.ProviderPayload = source.ProviderPayload;
        target.SyncedAt = source.SyncedAt;
    }
}
