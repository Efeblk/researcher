using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.GoogleScholar;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Console;

public sealed class ResearcherConsolePresenter
{
    public void PrintDatabaseResearcher(Researcher researcher)
    {
        System.Console.WriteLine($"Kayıt ID              : {researcher.Id}");
        System.Console.WriteLine($"Üniversite personel ID: {researcher.UniversityPersonnelId}");
        System.Console.WriteLine($"Ad                     : {researcher.FirstName}");
        System.Console.WriteLine($"Soyad                  : {researcher.LastName}");
        System.Console.WriteLine($"Akademik unvan         : {researcher.AcademicTitle}");
        System.Console.WriteLine($"Bölüm                  : {researcher.Department}");
        System.Console.WriteLine($"ORCID                  : {researcher.Orcid}");
        System.Console.WriteLine($"Google Scholar ID      : {researcher.GoogleScholarId}");
        System.Console.WriteLine($"Scopus Author ID       : {researcher.ScopusAuthorId}");
        System.Console.WriteLine(
            $"Web of Science ID      : {researcher.WebOfScienceResearcherId}");
        System.Console.WriteLine(
            $"Kayıt son değişiklik    : {FormatUpdateTime(researcher.LastUpdatedAt)}");
        System.Console.WriteLine();

        PrintOpenAlexDatabaseSummary(researcher);
        PrintGoogleScholarDatabaseSummary(researcher);
        PrintScopus(researcher);
        PrintWebOfScience(researcher);
    }

    public void Print(ResearcherCollectResponse response)
    {
        Researcher? researcher = null;
        int index = 0;

        for (index = 0; index < response.Messages.Count; index++)
        {
            System.Console.WriteLine(response.Messages[index]);
        }

        researcher = response.Researcher;

        if (researcher is null)
        {
            return;
        }

        PrintOpenAlexAuthor(researcher);
        PrintOpenAlexWorks(researcher);
        PrintGoogleScholarWorks(researcher);
        PrintScopus(researcher);
        PrintWebOfScience(researcher);
    }

    private static void PrintOpenAlexAuthor(Researcher researcher)
    {
        if (researcher.OpenAlex is null)
        {
            return;
        }

        System.Console.WriteLine($"Akademisyen : {researcher.OpenAlex.DisplayName}");
        System.Console.WriteLine($"ORCID       : {researcher.Orcid}");
        System.Console.WriteLine($"OpenAlex ID : {researcher.OpenAlex.AuthorId}");
        System.Console.WriteLine($"Yayın sayısı: {researcher.OpenAlex.WorksCount}");
        System.Console.WriteLine(
            $"Son güncelleme: {FormatUpdateTime(researcher.OpenAlex.LastUpdatedAt)}");
        System.Console.WriteLine();
    }

    private static void PrintOpenAlexDatabaseSummary(Researcher researcher)
    {
        int? workCount = null;

        if (researcher.OpenAlex is null)
        {
            return;
        }

        workCount = researcher.OpenAlex.Works?.Count ?? researcher.OpenAlex.WorksCount;

        System.Console.WriteLine("OpenAlex özeti:");
        System.Console.WriteLine($"Akademisyen    : {researcher.OpenAlex.DisplayName}");
        System.Console.WriteLine($"OpenAlex ID    : {researcher.OpenAlex.AuthorId}");
        System.Console.WriteLine($"Yayın sayısı   : {workCount}");
        System.Console.WriteLine(
            $"Son güncelleme : {FormatUpdateTime(researcher.OpenAlex.LastUpdatedAt)}");
    }

    private static void PrintOpenAlexWorks(Researcher researcher)
    {
        int index = 0;
        OpenAlexWork? work = null;
        string? doi = null;

        if (researcher.OpenAlex?.Works is null)
        {
            return;
        }

        System.Console.WriteLine("OpenAlex yayınları:");

        for (index = 0; index < researcher.OpenAlex.Works.Count; index++)
        {
            work = researcher.OpenAlex.Works[index];
            doi = work.Doi ?? "DOI bulunamadı";

            System.Console.WriteLine($"{index + 1}. {work.Title}");
            System.Console.WriteLine(
                $"   Yıl: {work.PublicationYear} | Tür: {work.Type} | Atıf: {work.CitedByCount}");
            System.Console.WriteLine($"   {doi}");
        }
    }

    private static void PrintGoogleScholarWorks(Researcher researcher)
    {
        int index = 0;
        GoogleScholarWork? work = null;
        string? publicationSummary = null;

        System.Console.WriteLine();
        System.Console.WriteLine($"Google Scholar ID: {researcher.GoogleScholarId}");
        System.Console.WriteLine(
            $"Google Scholar akademisyen: {researcher.GoogleScholar?.Name}");
        System.Console.WriteLine($"Kurum: {researcher.GoogleScholar?.Affiliations}");
        System.Console.WriteLine(
            $"Son güncelleme: " +
            $"{FormatUpdateTime(researcher.GoogleScholar?.LastUpdatedAt)}");
        System.Console.WriteLine("Google Scholar sonuçları:");

        if (researcher.GoogleScholar?.Works is null)
        {
            System.Console.WriteLine("Google Scholar verisi bulunamadı.");
            return;
        }

        for (index = 0; index < researcher.GoogleScholar.Works.Count; index++)
        {
            work = researcher.GoogleScholar.Works[index];
            publicationSummary = work.Publication ?? "Yayın bilgisi bulunamadı";

            System.Console.WriteLine($"{index + 1}. {work.Title}");
            System.Console.WriteLine($"   Yazarlar: {work.Authors}");
            System.Console.WriteLine($"   {publicationSummary}");
            System.Console.WriteLine($"   Yıl: {work.Year} | Atıf: {work.CitedByCount}");
            System.Console.WriteLine($"   {work.Link}");
        }
    }

    private static void PrintGoogleScholarDatabaseSummary(Researcher researcher)
    {
        GoogleScholarData? googleScholar = null;
        int publicationCount = 0;
        int citationCount = 0;
        int hIndex = 0;
        int i10Index = 0;

        googleScholar = researcher.GoogleScholar;

        if (googleScholar is null)
        {
            return;
        }

        publicationCount = googleScholar.Works?.Count ?? 0;
        citationCount = googleScholar.CitationCount
            ?? CalculateCitationCount(googleScholar.Works);
        hIndex = googleScholar.HIndex
            ?? CalculateHIndex(googleScholar.Works);
        i10Index = googleScholar.I10Index
            ?? CalculateI10Index(googleScholar.Works);

        System.Console.WriteLine();
        System.Console.WriteLine("Google Scholar özeti:");
        System.Console.WriteLine($"Google Scholar ID: {googleScholar.ScholarId}");
        System.Console.WriteLine($"Akademisyen      : {googleScholar.Name}");
        System.Console.WriteLine($"Kurum            : {googleScholar.Affiliations}");
        System.Console.WriteLine($"Yayın sayısı     : {publicationCount}");
        System.Console.WriteLine($"Toplam atıf      : {citationCount}");
        System.Console.WriteLine($"H-index          : {hIndex}");
        System.Console.WriteLine($"i10-index        : {i10Index}");
        System.Console.WriteLine(
            $"Son güncelleme   : {FormatUpdateTime(googleScholar.LastUpdatedAt)}");
    }

    private static void PrintScopus(Researcher researcher)
    {
        if (researcher.Scopus is null)
        {
            return;
        }

        System.Console.WriteLine();
        System.Console.WriteLine($"Scopus Author ID : {researcher.Scopus.AuthorId}");
        System.Console.WriteLine(
            $"Akademisyen      : {researcher.Scopus.GivenName} {researcher.Scopus.Surname}");
        System.Console.WriteLine($"Kurum            : {researcher.Scopus.AffiliationName}");
        System.Console.WriteLine(
            $"Şehir / ülke     : {researcher.Scopus.AffiliationCity} / " +
            $"{researcher.Scopus.AffiliationCountry}");
        System.Console.WriteLine($"Yayın sayısı     : {researcher.Scopus.DocumentCount}");
        System.Console.WriteLine($"Atıf sayısı      : {researcher.Scopus.CitationCount}");
        System.Console.WriteLine($"Atıf yapan yayın : {researcher.Scopus.CitedByCount}");
        System.Console.WriteLine($"H-index          : {researcher.Scopus.HIndex}");
        System.Console.WriteLine(
            $"Son güncelleme   : {FormatUpdateTime(researcher.Scopus.LastUpdatedAt)}");
    }

    private static void PrintWebOfScience(Researcher researcher)
    {
        if (researcher.WebOfScience is null)
        {
            return;
        }

        System.Console.WriteLine();
        System.Console.WriteLine($"ResearcherID     : {researcher.WebOfScience.Rid}");
        System.Console.WriteLine($"Akademisyen      : {researcher.WebOfScience.FullName}");
        System.Console.WriteLine(
            $"Kurum            : {researcher.WebOfScience.PrimaryAffiliation}");
        System.Console.WriteLine($"Ülke             : {researcher.WebOfScience.Country}");
        System.Console.WriteLine($"Profil sahipli mi: {researcher.WebOfScience.IsClaimed}");
        System.Console.WriteLine($"Yayın sayısı     : {researcher.WebOfScience.DocumentCount}");
        System.Console.WriteLine(
            $"Atıf sayısı      : {researcher.WebOfScience.TotalTimesCited}");
        System.Console.WriteLine(
            $"Atıf yapan yayın : {researcher.WebOfScience.TotalCitingPublications}");
        System.Console.WriteLine($"H-index          : {researcher.WebOfScience.HIndex}");
        System.Console.WriteLine(
            $"Son güncelleme   : " +
            $"{FormatUpdateTime(researcher.WebOfScience.LastUpdatedAt)}");
    }

    private static string FormatUpdateTime(DateTime? updateTime)
    {
        DateTime updateTimeUtc = DateTime.MinValue;

        if (!updateTime.HasValue)
        {
            return "Henüz güncellenmedi";
        }

        updateTimeUtc = updateTime.Value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(updateTime.Value, DateTimeKind.Utc)
            : updateTime.Value.ToUniversalTime();

        return updateTimeUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
    }

    private static int CalculateCitationCount(List<GoogleScholarWork>? works)
    {
        int citationCount = 0;
        int index = 0;

        if (works is null)
        {
            return citationCount;
        }

        for (index = 0; index < works.Count; index++)
        {
            citationCount += works[index].CitedByCount ?? 0;
        }

        return citationCount;
    }

    private static int CalculateHIndex(List<GoogleScholarWork>? works)
    {
        List<int>? citationCounts = null;
        int hIndex = 0;
        int index = 0;

        if (works is null)
        {
            return hIndex;
        }

        citationCounts = works
            .Select(work => work.CitedByCount ?? 0)
            .OrderByDescending(citationCount => citationCount)
            .ToList();

        for (index = 0; index < citationCounts.Count; index++)
        {
            if (citationCounts[index] < index + 1)
            {
                break;
            }

            hIndex = index + 1;
        }

        return hIndex;
    }

    private static int CalculateI10Index(List<GoogleScholarWork>? works)
    {
        int i10Index = 0;
        int index = 0;

        if (works is null)
        {
            return i10Index;
        }

        for (index = 0; index < works.Count; index++)
        {
            if ((works[index].CitedByCount ?? 0) >= 10)
            {
                i10Index++;
            }
        }

        return i10Index;
    }
}
