using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Orcid;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.GoogleScholar;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Collection;

public sealed class ResearcherCollectionFeedback
{
    public void Add(
        Researcher researcher,
        Researcher requestedIdentifiers,
        List<string> messages)
    {
        messages.Add("=== VERİ TOPLAMA ÖZETİ ===");

        if (!string.IsNullOrWhiteSpace(requestedIdentifiers.Orcid))
        {
            AddOrcidFeedback(researcher.OrcidProfile, messages);
            AddOpenAlexFeedback(researcher.OpenAlexProfile, messages);
        }

        if (!string.IsNullOrWhiteSpace(requestedIdentifiers.GoogleScholarId))
        {
            AddGoogleScholarFeedback(researcher.GoogleScholarProfile, messages);
        }

        if (!string.IsNullOrWhiteSpace(
                requestedIdentifiers.WebOfScienceResearcherId))
        {
            AddWebOfScienceFeedback(researcher.WebOfScienceProfile, messages);
        }

        messages.Add(string.Empty);
    }

    private static void AddOpenAlexFeedback(
        OpenAlexProfile? profile,
        List<string> messages)
    {
        if (profile is null)
        {
            messages.Add("[EKSİK] OpenAlex karşılaştırma verisi alınamadı.");
            return;
        }

        int collectedWorksCount = profile.Works?.Count ?? 0;
        messages.Add(
            $"[OK] OpenAlex karşılaştırması: {profile.DisplayName ?? "Ad bilinmiyor"}.");
        messages.Add(
            $"[BİLGİ] OpenAlex metrikleri: {profile.WorksCount} yayın, " +
            $"{profile.CitedByCount} atıf, h-index " +
            $"{profile.HIndex?.ToString() ?? "—"}, i10-index " +
            $"{profile.I10Index?.ToString() ?? "—"}.");
        messages.Add(
            $"[OK] OpenAlex ayrı yayın tablosu: {collectedWorksCount} kayıt alındı.");
        messages.Add(
            "[BİLGİ] OpenAlex verileri ortak yayın listesine eklenmedi; " +
            "yalnız karşılaştırma için ayrı tutuldu.");
    }

    private static void AddGoogleScholarFeedback(
        GoogleScholarProfile? profile,
        List<string> messages)
    {
        if (profile is null)
        {
            messages.Add("[EKSİK] Google Scholar profili alınamadı.");
            return;
        }

        messages.Add(
            !string.IsNullOrWhiteSpace(profile.DisplayName)
                ? $"[OK] Google Scholar profili: {profile.DisplayName}."
                : "[KISMİ] Google Scholar profili alındı; ad bilgisi bulunamadı.");
        messages.Add(
            $"[OK] Google Scholar yayınları: {profile.DocumentsCount} kayıt toplandı.");
        messages.Add(
            $"[OK] Google Scholar metrikleri: toplam atıf " +
            $"{profile.CitationCount?.ToString() ?? "—"}, h-index " +
            $"{profile.HIndex?.ToString() ?? "—"}, i10-index " +
            $"{profile.I10Index?.ToString() ?? "—"}.");
        messages.Add(
            profile.MetricsSinceYear.HasValue
                ? $"[BİLGİ] Yakın dönem metrik başlangıcı: " +
                  $"{profile.MetricsSinceYear}."
                : "[BİLGİ] Yakın dönem metrik başlangıç yılı alınamadı.");
        messages.Add(
            !string.IsNullOrWhiteSpace(profile.RawDataJson)
                ? "[OK] SearchApi ham Google Scholar yanıtları saklandı."
                : "[KISMİ] SearchApi ham yanıtı saklanamadı.");
    }

    private static void AddWebOfScienceFeedback(
        WebOfScienceProfile? profile,
        List<string> messages)
    {
        if (profile is null)
        {
            messages.Add("[EKSİK] Web of Science yayınları alınamadı.");
            return;
        }

        List<WebOfScienceWork>? works = profile.Works ?? [];
        int doiCount = works.Count(work => !string.IsNullOrWhiteSpace(work.Doi));
        int citationCount = works.Count(work => work.TimesCited.HasValue);

        messages.Add(
            !string.IsNullOrWhiteSpace(profile.DisplayName)
                ? $"[OK] Yayınlardaki araştırmacı adı: {profile.DisplayName}."
                : "[KISMİ] Yayınlar alındı; araştırmacı adı eşleştirilemedi.");
        messages.Add(
            works.Count > 0
                ? $"[OK] Web of Science WOS + WOK yayınları: " +
                  $"{works.Count} tekilleştirilmiş kayıt toplandı."
                : "[BİLGİ] Bu ResearcherID için yayın bulunamadı.");
        messages.Add(
            $"[BİLGİ] Web of Science yayın alanları: DOI {doiCount}/{works.Count}, " +
            $"atıf sayısı {citationCount}/{works.Count}.");
        messages.Add(
            citationCount == works.Count && works.Count > 0
                ? $"[OK] Yayın atıflarından hesaplanan metrikler: h-index " +
                  $"{profile.HIndex}, toplam atıf {profile.TotalTimesCited}."
                : "[BİLGİ] Abonelik bu sorguda atıf sayılarını vermediği için " +
                  "h-index ve toplam atıf hesaplanamadı.");
        messages.Add(
            "[BİLGİ] Starter API v1 profil, kurum ve hakemlik verisi sağlamaz.");
        messages.Add(
            !string.IsNullOrWhiteSpace(profile.DocumentPagesJson)
                ? "[OK] WOS ve WOK ham yayın sayfaları ayrı ayrı saklandı."
                : "[KISMİ] Starter API ham yayın yanıtları saklanamadı.");
    }

    private static void AddOrcidFeedback(
        OrcidProfile? profile,
        List<string> messages)
    {
        if (profile is null)
        {
            messages.Add("[EKSİK] Resmî ORCID kaydı alınamadı.");
            return;
        }

        List<OrcidWork> works = profile.Works ?? [];
        int titleCount = works.Count(work => !string.IsNullOrWhiteSpace(work.Title));
        int doiCount = works.Count(work => !string.IsNullOrWhiteSpace(work.Doi));
        int authorCount = works.Count(work => !string.IsNullOrWhiteSpace(work.Authors));
        int journalCount = works.Count(work => !string.IsNullOrWhiteSpace(work.JournalTitle));

        messages.Add(
            !string.IsNullOrWhiteSpace(profile.DisplayName)
                ? $"[OK] ORCID profili: {profile.DisplayName}."
                : "[KISMİ] ORCID profili alındı; ad bilgisi herkese açık değil.");
        messages.Add(
            $"[BİLGİ] ORCID faaliyetleri: {profile.EmploymentsCount} istihdam, " +
            $"{profile.EducationsCount} eğitim, {profile.FundingsCount} fonlama, " +
            $"{profile.PeerReviewsCount} hakemlik grubu.");
        messages.Add(
            works.Count > 0
                ? $"[OK] ORCID eserleri: {works.Count} tekilleştirilmiş kayıt toplandı."
                : "[BİLGİ] ORCID kaydında herkese açık eser bulunamadı.");

        if (works.Count > 0)
        {
            messages.Add($"[BİLGİ] ORCID türleri: {CreateTypeSummary(works)}");
            messages.Add(
                $"[BİLGİ] Eser alanları: başlık {titleCount}/{works.Count}, " +
                $"yazar {authorCount}/{works.Count}, yayın yeri {journalCount}/{works.Count}, " +
                $"DOI {doiCount}/{works.Count}.");
        }

        messages.Add(
            "[BİLGİ] ORCID atıf sayısı, h-index ve i10-index sağlamaz; " +
            "bu alanlar boş bırakıldı.");
        messages.Add(
            !string.IsNullOrWhiteSpace(profile.RawDataJson) &&
            works.All(work => !string.IsNullOrWhiteSpace(work.RawDataJson))
                ? "[OK] ORCID profil ve tam eser JSON yanıtları saklandı."
                : "[KISMİ] ORCID ham JSON verisinin bir bölümü eksik.");
    }

    private static string CreateTypeSummary(List<OrcidWork> works)
    {
        return string.Join(", ", works
            .GroupBy(work => work.Category)
            .OrderByDescending(group => group.Count())
            .Select(group => $"{group.Count()} {GetCategoryName(group.Key)}"));
    }

    private static string GetCategoryName(AcademicWorkCategory category)
    {
        return category switch
        {
            AcademicWorkCategory.Article => "makale",
            AcademicWorkCategory.Book => "kitap",
            AcademicWorkCategory.BookChapter => "kitap bölümü",
            AcademicWorkCategory.ConferenceAbstract => "bildiri özeti",
            AcademicWorkCategory.ConferencePaper => "bildiri",
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
