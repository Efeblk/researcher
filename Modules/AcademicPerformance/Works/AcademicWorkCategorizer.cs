using System.Text;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.GoogleScholar;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Works;

public sealed class AcademicWorkCategorizer
{
    public void Categorize(Researcher researcher)
    {
        Dictionary<string, AcademicWorkCategory>? openAlexCategoriesByTitle = null;

        CategorizeOpenAlexWorks(researcher.OpenAlex?.Works);
        openAlexCategoriesByTitle = CreateOpenAlexCategoryLookup(
            researcher.OpenAlex?.Works);
        CategorizeGoogleScholarWorks(
            researcher.GoogleScholar?.Works,
            openAlexCategoriesByTitle);
    }

    private static void CategorizeOpenAlexWorks(List<OpenAlexWork>? works)
    {
        int index = 0;
        OpenAlexWork? work = null;

        if (works is null)
        {
            return;
        }

        for (index = 0; index < works.Count; index++)
        {
            work = works[index];
            work.Category = GetOpenAlexCategory(work.Type);
            work.CategorySource = AcademicWorkCategorySource.OpenAlex;
        }
    }

    private static Dictionary<string, AcademicWorkCategory> CreateOpenAlexCategoryLookup(
        List<OpenAlexWork>? works)
    {
        Dictionary<string, AcademicWorkCategory>? categoriesByTitle = null;
        HashSet<string>? conflictingTitles = null;
        int index = 0;
        OpenAlexWork? work = null;
        string? normalizedTitle = null;
        string[]? conflictingTitleValues = null;
        string? conflictingTitle = null;
        int conflictingTitleIndex = 0;
        AcademicWorkCategory existingCategory = AcademicWorkCategory.Unknown;

        categoriesByTitle = new Dictionary<string, AcademicWorkCategory>();
        conflictingTitles = new HashSet<string>();

        if (works is null)
        {
            return categoriesByTitle;
        }

        for (index = 0; index < works.Count; index++)
        {
            work = works[index];
            normalizedTitle = NormalizeTitle(work.Title);

            if (string.IsNullOrWhiteSpace(normalizedTitle))
            {
                continue;
            }

            if (categoriesByTitle.TryGetValue(normalizedTitle, out existingCategory) &&
                existingCategory != work.Category)
            {
                conflictingTitles.Add(normalizedTitle);
                continue;
            }

            categoriesByTitle[normalizedTitle] = work.Category;
        }

        conflictingTitleValues = conflictingTitles.ToArray();

        for (
            conflictingTitleIndex = 0;
            conflictingTitleIndex < conflictingTitleValues.Length;
            conflictingTitleIndex++)
        {
            conflictingTitle = conflictingTitleValues[conflictingTitleIndex];
            categoriesByTitle.Remove(conflictingTitle);
        }

        return categoriesByTitle;
    }

    private static void CategorizeGoogleScholarWorks(
        List<GoogleScholarWork>? works,
        Dictionary<string, AcademicWorkCategory> openAlexCategoriesByTitle)
    {
        int index = 0;
        GoogleScholarWork? work = null;
        string? normalizedTitle = null;
        AcademicWorkCategory openAlexCategory = AcademicWorkCategory.Unknown;

        if (works is null)
        {
            return;
        }

        for (index = 0; index < works.Count; index++)
        {
            work = works[index];
            work.Category = AcademicWorkCategory.Unknown;
            work.CategorySource = AcademicWorkCategorySource.Unknown;
            normalizedTitle = NormalizeTitle(work.Title);

            if (string.IsNullOrWhiteSpace(normalizedTitle) ||
                !openAlexCategoriesByTitle.TryGetValue(
                    normalizedTitle,
                    out openAlexCategory))
            {
                continue;
            }

            work.Category = openAlexCategory;
            work.CategorySource = AcademicWorkCategorySource.MatchedFromOpenAlex;
        }
    }

    private static AcademicWorkCategory GetOpenAlexCategory(string? type)
    {
        string? normalizedType = null;

        normalizedType = type?.Trim().ToLowerInvariant();

        return normalizedType switch
        {
            "article" => AcademicWorkCategory.Article,
            "book" => AcademicWorkCategory.Book,
            "book-chapter" => AcademicWorkCategory.BookChapter,
            "book-review" => AcademicWorkCategory.BookReview,
            "conference-abstract" => AcademicWorkCategory.ConferenceAbstract,
            "conference-paper" => AcademicWorkCategory.ConferencePaper,
            "data-paper" => AcademicWorkCategory.DataPaper,
            "dataset" => AcademicWorkCategory.Dataset,
            "dissertation" => AcademicWorkCategory.Dissertation,
            "editorial" => AcademicWorkCategory.Editorial,
            "erratum" => AcademicWorkCategory.Erratum,
            "letter" => AcademicWorkCategory.Letter,
            "libguides" => AcademicWorkCategory.LibGuide,
            "other" => AcademicWorkCategory.Other,
            "paratext" => AcademicWorkCategory.Paratext,
            "peer-review" => AcademicWorkCategory.PeerReview,
            "preprint" => AcademicWorkCategory.Preprint,
            "reference-entry" => AcademicWorkCategory.ReferenceEntry,
            "report" => AcademicWorkCategory.Report,
            "retraction" => AcademicWorkCategory.Retraction,
            "review" => AcademicWorkCategory.Review,
            "software" => AcademicWorkCategory.Software,
            "software-paper" => AcademicWorkCategory.SoftwarePaper,
            "standard" => AcademicWorkCategory.Standard,
            "supplementary-materials" => AcademicWorkCategory.SupplementaryMaterials,
            _ => AcademicWorkCategory.Unknown
        };
    }

    private static string NormalizeTitle(string? title)
    {
        StringBuilder? normalizedTitle = null;
        int index = 0;
        char character = '\0';

        normalizedTitle = new StringBuilder();

        if (string.IsNullOrWhiteSpace(title))
        {
            return normalizedTitle.ToString();
        }

        for (index = 0; index < title.Length; index++)
        {
            character = title[index];

            if (char.IsLetterOrDigit(character))
            {
                normalizedTitle.Append(char.ToLowerInvariant(character));
            }
        }

        return normalizedTitle.ToString();
    }
}
