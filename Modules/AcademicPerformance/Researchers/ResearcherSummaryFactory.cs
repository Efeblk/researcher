using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.GoogleScholar;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;

public sealed class ResearcherSummaryFactory
{
    public ResearcherSummary? Create(Researcher? researcher)
    {
        ResearcherSummary? summary = null;

        if (researcher is null)
        {
            return null;
        }

        summary = new ResearcherSummary();
        summary.Id = researcher.Id;
        summary.UniversityPersonnelId = researcher.UniversityPersonnelId;
        summary.FirstName = researcher.FirstName;
        summary.LastName = researcher.LastName;
        summary.AcademicTitle = researcher.AcademicTitle;
        summary.Department = researcher.Department;
        summary.Orcid = researcher.Orcid;
        summary.GoogleScholarId = researcher.GoogleScholarId;
        summary.WebOfScienceResearcherId = researcher.WebOfScienceResearcherId;
        summary.LastUpdatedAt = researcher.LastUpdatedAt;
        summary.OpenAlex = CreateOpenAlexSummary(researcher);
        summary.GoogleScholar = CreateGoogleScholarSummary(researcher);
        summary.WebOfScience = CreateWebOfScienceSummary(researcher);

        return summary;
    }

    private static OpenAlexSummary? CreateOpenAlexSummary(Researcher researcher)
    {
        OpenAlexSummary? summary = null;

        if (researcher.OpenAlex is null)
        {
            return null;
        }

        summary = new OpenAlexSummary();
        summary.AuthorId = researcher.OpenAlex.AuthorId;
        summary.DisplayName = researcher.OpenAlex.DisplayName;
        summary.WorkCount = researcher.OpenAlex.Works?.Count
            ?? researcher.OpenAlex.WorksCount
            ?? 0;
        summary.WorkCategories = CreateOpenAlexCategoryCounts(
            researcher.OpenAlex.Works);
        summary.LastUpdatedAt = researcher.OpenAlex.LastUpdatedAt;

        return summary;
    }

    private static GoogleScholarSummary? CreateGoogleScholarSummary(
        Researcher researcher)
    {
        GoogleScholarSummary? summary = null;

        if (researcher.GoogleScholar is null)
        {
            return null;
        }

        summary = new GoogleScholarSummary();
        summary.ScholarId = researcher.GoogleScholar.ScholarId;
        summary.Name = researcher.GoogleScholar.Name;
        summary.Affiliations = researcher.GoogleScholar.Affiliations;
        summary.WorkCount = researcher.GoogleScholar.Works?.Count ?? 0;
        summary.CitationCount = researcher.GoogleScholar.CitationCount
            ?? CalculateCitationCount(researcher.GoogleScholar.Works);
        summary.HIndex = researcher.GoogleScholar.HIndex
            ?? CalculateHIndex(researcher.GoogleScholar.Works);
        summary.I10Index = researcher.GoogleScholar.I10Index
            ?? CalculateI10Index(researcher.GoogleScholar.Works);
        summary.WorkCategories = CreateGoogleScholarCategoryCounts(
            researcher.GoogleScholar.Works);
        summary.LastUpdatedAt = researcher.GoogleScholar.LastUpdatedAt;

        return summary;
    }

    private static Dictionary<string, int> CreateOpenAlexCategoryCounts(
        List<OpenAlexWork>? works)
    {
        Dictionary<string, int>? categoryCounts = null;
        int index = 0;
        string? categoryName = null;
        int currentCount = 0;

        categoryCounts = [];

        if (works is null)
        {
            return categoryCounts;
        }

        for (index = 0; index < works.Count; index++)
        {
            categoryName = works[index].Category.ToString();
            categoryCounts.TryGetValue(categoryName, out currentCount);
            categoryCounts[categoryName] = currentCount + 1;
        }

        return categoryCounts;
    }

    private static Dictionary<string, int> CreateGoogleScholarCategoryCounts(
        List<GoogleScholarWork>? works)
    {
        Dictionary<string, int>? categoryCounts = null;
        int index = 0;
        string? categoryName = null;
        int currentCount = 0;

        categoryCounts = [];

        if (works is null)
        {
            return categoryCounts;
        }

        for (index = 0; index < works.Count; index++)
        {
            categoryName = works[index].Category.ToString();
            categoryCounts.TryGetValue(categoryName, out currentCount);
            categoryCounts[categoryName] = currentCount + 1;
        }

        return categoryCounts;
    }

    private static WebOfScienceSummary? CreateWebOfScienceSummary(
        Researcher researcher)
    {
        WebOfScienceSummary? summary = null;

        if (researcher.WebOfScience is null)
        {
            return null;
        }

        summary = new WebOfScienceSummary();
        summary.ResearcherId = researcher.WebOfScience.Rid;
        summary.FullName = researcher.WebOfScience.FullName;
        summary.PrimaryAffiliation = researcher.WebOfScience.PrimaryAffiliation;
        summary.DocumentCount = researcher.WebOfScience.DocumentCount;
        summary.CitationCount = researcher.WebOfScience.TotalTimesCited;
        summary.HIndex = researcher.WebOfScience.HIndex;
        summary.LastUpdatedAt = researcher.WebOfScience.LastUpdatedAt;

        return summary;
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
