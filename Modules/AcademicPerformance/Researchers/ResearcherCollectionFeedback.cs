using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Orcid;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;

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
        }

        messages.Add(string.Empty);
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
