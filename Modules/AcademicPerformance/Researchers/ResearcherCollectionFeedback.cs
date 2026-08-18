using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.GoogleScholar;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works;
using Microsoft.Extensions.Configuration;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;

public sealed class ResearcherCollectionFeedback
{
    private readonly IConfiguration _configuration;

    public ResearcherCollectionFeedback(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public void Add(
        Researcher researcher,
        Researcher requestedIdentifiers,
        List<string> messages)
    {
        messages.Add("=== VERİ TOPLAMA ÖZETİ ===");

        if (!string.IsNullOrWhiteSpace(requestedIdentifiers.Orcid))
        {
            AddOpenAlexFeedback(researcher.OpenAlex, messages);
        }

        if (!string.IsNullOrWhiteSpace(requestedIdentifiers.GoogleScholarId))
        {
            AddGoogleScholarFeedback(researcher.GoogleScholar, messages);
        }

        if (!string.IsNullOrWhiteSpace(
                requestedIdentifiers.WebOfScienceResearcherId))
        {
            AddWebOfScienceFeedback(researcher.WebOfScience, messages);
        }

        messages.Add(string.Empty);
    }

    private static void AddOpenAlexFeedback(
        OpenAlexData? data,
        List<string> messages)
    {
        List<OpenAlexWork>? works = null;
        int workCount = 0;
        int titleCount = 0;
        int authorCount = 0;
        int sourceCount = 0;
        int doiCount = 0;
        int abstractCount = 0;
        int citationCount = 0;
        int openAccessCount = 0;
        int fullTextCount = 0;
        int rawWorkCount = 0;

        if (data is null)
        {
            messages.Add("[EKSİK] OpenAlex profil bilgileri alınamadı.");
            messages.Add("[EKSİK] OpenAlex çalışma bilgileri alınamadı.");
            messages.Add("[EKSİK] OpenAlex ham verisi alınamadı.");
            return;
        }

        AddProfileFeedback(
            messages,
            "OpenAlex profil bilgileri",
            ("ad", data.DisplayName),
            ("yazar ID", data.AuthorId));

        works = data.Works ?? [];
        workCount = works.Count;
        messages.Add(
            workCount > 0
                ? $"[OK] OpenAlex çalışma bilgileri: {workCount} kayıt toplandı."
                : "[BİLGİ] OpenAlex çalışma bilgileri: kayıt bulunamadı.");

        if (workCount > 0)
        {
            messages.Add($"[BİLGİ] OpenAlex türleri: {CreateOpenAlexTypeSummary(works)}");
            titleCount = works.Count(work => !string.IsNullOrWhiteSpace(work.Title));
            authorCount = works.Count(work => !string.IsNullOrWhiteSpace(work.Authors));
            sourceCount = works.Count(work => !string.IsNullOrWhiteSpace(work.SourceName));
            doiCount = works.Count(work => !string.IsNullOrWhiteSpace(work.Doi));
            abstractCount = works.Count(work => !string.IsNullOrWhiteSpace(work.Abstract));
            citationCount = works.Count(work => work.CitedByCount.HasValue);
            openAccessCount = works.Count(work => work.IsOpenAccess == true);
            fullTextCount = works.Count(work => !string.IsNullOrWhiteSpace(work.FullTextUrl));
            rawWorkCount = works.Count(work => !string.IsNullOrWhiteSpace(work.RawDataJson));

            messages.Add(
                $"{GetCoverageStatus(
                    workCount,
                    titleCount,
                    authorCount,
                    sourceCount,
                    doiCount,
                    abstractCount)} OpenAlex yayın alanları: " +
                $"başlık {titleCount}/{workCount}, yazar {authorCount}/{workCount}, " +
                $"kaynak {sourceCount}/{workCount}, DOI {doiCount}/{workCount}, " +
                $"özet {abstractCount}/{workCount}.");
            messages.Add(
                $"{GetCoverageStatus(workCount, citationCount)} OpenAlex atıf bilgisi: " +
                $"{citationCount}/{workCount}; açık erişim {openAccessCount}, " +
                $"tam metin bağlantısı {fullTextCount}.");
        }

        messages.Add(
            !string.IsNullOrWhiteSpace(data.RawDataJson) &&
            !string.IsNullOrWhiteSpace(data.WorksResponsePagesJson) &&
            rawWorkCount == workCount
                ? $"[OK] OpenAlex ham JSON: profil, sayfalar ve {rawWorkCount} çalışma saklandı."
                : $"[KISMİ] OpenAlex ham JSON: {rawWorkCount}/{workCount} çalışma saklandı.");
    }

    private void AddGoogleScholarFeedback(
        GoogleScholarData? data,
        List<string> messages)
    {
        List<GoogleScholarWork>? works = null;
        bool collectArticleDetails = false;
        int workCount = 0;
        int interestCount = 0;
        int titleCount = 0;
        int authorCount = 0;
        int publicationCount = 0;
        int yearCount = 0;
        int citationCount = 0;
        int categorizedCount = 0;
        int rawWorkCount = 0;
        int detailCount = 0;

        if (data is null)
        {
            messages.Add("[EKSİK] Google Scholar profil bilgileri alınamadı.");
            messages.Add("[EKSİK] Google Scholar atıf, h-index ve i10-index alınamadı.");
            messages.Add("[EKSİK] Google Scholar çalışma bilgileri alınamadı.");
            messages.Add("[EKSİK] Google Scholar ham verisi alınamadı.");
            return;
        }

        AddProfileFeedback(
            messages,
            "Google Scholar profil bilgileri",
            ("ad", data.Name),
            ("kurum", data.Affiliations),
            ("e-posta", data.Email));
        AddGoogleScholarMetricsFeedback(data, messages);

        interestCount = data.Interests?.Count ?? 0;
        messages.Add($"[BİLGİ] Google Scholar ilgi alanları: {interestCount} kayıt.");

        works = data.Works ?? [];
        workCount = works.Count;
        messages.Add(
            workCount > 0
                ? $"[OK] Google Scholar çalışma bilgileri: {workCount} kayıt toplandı."
                : "[BİLGİ] Google Scholar çalışma bilgileri: kayıt bulunamadı.");

        if (workCount > 0)
        {
            titleCount = works.Count(work => !string.IsNullOrWhiteSpace(work.Title));
            authorCount = works.Count(work => !string.IsNullOrWhiteSpace(work.Authors));
            publicationCount = works.Count(
                work => !string.IsNullOrWhiteSpace(work.Publication));
            yearCount = works.Count(work => !string.IsNullOrWhiteSpace(work.Year));
            citationCount = works.Count(work => work.CitedByCount.HasValue);
            categorizedCount = works.Count(
                work => work.Category != AcademicWorkCategory.Unknown);
            rawWorkCount = works.Count(work => !string.IsNullOrWhiteSpace(work.RawDataJson));
            detailCount = works.Count(
                work => !string.IsNullOrWhiteSpace(work.DetailRawDataJson));

            messages.Add(
                $"{GetCoverageStatus(
                    workCount,
                    titleCount,
                    authorCount,
                    publicationCount,
                    yearCount,
                    citationCount)} Google Scholar yayın alanları: " +
                $"başlık {titleCount}/{workCount}, yazar {authorCount}/{workCount}, " +
                $"yayın {publicationCount}/{workCount}, yıl {yearCount}/{workCount}, " +
                $"atıf {citationCount}/{workCount}.");
            messages.Add(
                categorizedCount == workCount
                    ? $"[OK] Google Scholar türleri: {categorizedCount}/{workCount} sınıflandırıldı."
                    : $"[KISMİ] Google Scholar türleri: {categorizedCount}/{workCount} " +
                      "OpenAlex ile eşleştirilerek sınıflandırıldı.");
        }

        bool.TryParse(
            _configuration["GoogleScholar:CollectArticleDetails"],
            out collectArticleDetails);

        if (!collectArticleDetails)
        {
            messages.Add("[ATLANDI] Google Scholar yayın ayrıntıları: ayar kapalı.");
        }
        else
        {
            messages.Add(
                detailCount == workCount
                    ? $"[OK] Google Scholar yayın ayrıntıları: {detailCount}/{workCount} toplandı."
                    : $"[KISMİ] Google Scholar yayın ayrıntıları: " +
                      $"{detailCount}/{workCount} toplandı.");
        }

        messages.Add(
            !string.IsNullOrWhiteSpace(data.RawDataJson) &&
            !string.IsNullOrWhiteSpace(data.ResponsePagesJson) &&
            rawWorkCount == workCount
                ? $"[OK] Google Scholar ham JSON: profil, sayfalar ve " +
                  $"{rawWorkCount} çalışma saklandı."
                : $"[KISMİ] Google Scholar ham JSON: {rawWorkCount}/{workCount} çalışma saklandı.");
    }

    private static void AddWebOfScienceFeedback(
        WebOfScienceData? data,
        List<string> messages)
    {
        if (data is null)
        {
            messages.Add("[EKSİK] Web of Science profil bilgileri alınamadı.");
            messages.Add("[EKSİK] Web of Science çalışma ve atıf metrikleri alınamadı.");
            return;
        }

        AddProfileFeedback(
            messages,
            "Web of Science profil bilgileri",
            ("ad", data.FullName),
            ("kurum", data.PrimaryAffiliation),
            ("ülke", data.Country));

        if (data.DocumentCount.HasValue &&
            data.TotalTimesCited.HasValue &&
            data.TotalCitingPublications.HasValue &&
            data.HIndex.HasValue)
        {
            messages.Add(
                $"[OK] Web of Science metrikleri: çalışma {data.DocumentCount}, " +
                $"atıf {data.TotalTimesCited}, atıf yapan yayın " +
                $"{data.TotalCitingPublications}, h-index {data.HIndex}.");
            return;
        }

        messages.Add("[KISMİ] Web of Science çalışma veya atıf metrikleri eksik.");
    }

    private static void AddGoogleScholarMetricsFeedback(
        GoogleScholarData data,
        List<string> messages)
    {
        List<string>? values = null;
        List<string>? missingValues = null;

        values = [];
        missingValues = [];
        AddMetric(values, missingValues, "atıf", data.CitationCount);
        AddMetric(values, missingValues, "h-index", data.HIndex);
        AddMetric(values, missingValues, "i10-index", data.I10Index);

        if (missingValues.Count == 0)
        {
            messages.Add($"[OK] Google Scholar metrikleri: {string.Join(", ", values)}.");
            return;
        }

        messages.Add(
            values.Count == 0
                ? $"[EKSİK] Google Scholar metrikleri alınamadı: " +
                  $"{string.Join(", ", missingValues)}."
                : $"[KISMİ] Google Scholar metrikleri: {string.Join(", ", values)}; " +
                  $"eksik: {string.Join(", ", missingValues)}.");
    }

    private static void AddMetric(
        List<string> values,
        List<string> missingValues,
        string name,
        int? value)
    {
        if (value.HasValue)
        {
            values.Add($"{name} {value}");
            return;
        }

        missingValues.Add(name);
    }

    private static void AddProfileFeedback(
        List<string> messages,
        string categoryName,
        params (string Name, string? Value)[] fields)
    {
        List<string>? foundFields = null;
        List<string>? missingFields = null;
        int index = 0;

        foundFields = [];
        missingFields = [];

        for (index = 0; index < fields.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(fields[index].Value))
            {
                missingFields.Add(fields[index].Name);
            }
            else
            {
                foundFields.Add(fields[index].Name);
            }
        }

        if (missingFields.Count == 0)
        {
            messages.Add($"[OK] {categoryName}: {string.Join(", ", foundFields)} toplandı.");
            return;
        }

        messages.Add(
            foundFields.Count == 0
                ? $"[EKSİK] {categoryName} alınamadı."
                : $"[KISMİ] {categoryName}: {string.Join(", ", foundFields)} toplandı; " +
                  $"eksik: {string.Join(", ", missingFields)}.");
    }

    private static string GetCoverageStatus(
        int totalCount,
        params int[] collectedCounts)
    {
        int index = 0;

        for (index = 0; index < collectedCounts.Length; index++)
        {
            if (collectedCounts[index] < totalCount)
            {
                return "[KISMİ]";
            }
        }

        return "[OK]";
    }

    private static string CreateOpenAlexTypeSummary(List<OpenAlexWork> works)
    {
        IEnumerable<string>? parts = null;

        parts = works
            .GroupBy(work => work.Category)
            .OrderByDescending(group => group.Count())
            .Select(group => $"{group.Count()} {GetCategoryName(group.Key)}");

        return string.Join(", ", parts);
    }

    private static string GetCategoryName(AcademicWorkCategory category)
    {
        return category switch
        {
            AcademicWorkCategory.Article => "makale",
            AcademicWorkCategory.Book => "kitap",
            AcademicWorkCategory.BookChapter => "kitap bölümü",
            AcademicWorkCategory.BookReview => "kitap incelemesi",
            AcademicWorkCategory.ConferenceAbstract => "bildiri özeti",
            AcademicWorkCategory.ConferencePaper => "bildiri",
            AcademicWorkCategory.DataPaper => "veri makalesi",
            AcademicWorkCategory.Dataset => "veri seti",
            AcademicWorkCategory.Dissertation => "tez",
            AcademicWorkCategory.Review => "derleme",
            AcademicWorkCategory.Preprint => "ön baskı",
            AcademicWorkCategory.Report => "rapor",
            AcademicWorkCategory.Software => "yazılım",
            AcademicWorkCategory.Unknown => "belirsiz",
            _ => category.ToString()
        };
    }
}
