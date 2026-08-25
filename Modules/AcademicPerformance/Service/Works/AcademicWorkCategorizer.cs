using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Orcid;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Works;

public sealed class AcademicWorkCategorizer
{
    public void Categorize(Researcher researcher)
    {
        List<OrcidWork>? orcidWorks = researcher.OrcidProfile?.Works;
        List<WebOfScienceWork>? webOfScienceWorks =
            researcher.WebOfScienceProfile?.Works;
        int index = 0;

        if (orcidWorks is not null)
        {
            for (index = 0; index < orcidWorks.Count; index++)
            {
                orcidWorks[index].Category = GetOrcidCategory(
                    orcidWorks[index].WorkType);
                orcidWorks[index].CategorySource = AcademicWorkCategorySource.Orcid;
            }
        }

        if (webOfScienceWorks is null)
        {
            return;
        }

        for (index = 0; index < webOfScienceWorks.Count; index++)
        {
            webOfScienceWorks[index].Category = GetWebOfScienceCategory(
                webOfScienceWorks[index].WorkTypes);
            webOfScienceWorks[index].CategorySource =
                AcademicWorkCategorySource.WebOfScience;
        }
    }

    public AcademicWorkCategory GetOrcidCategory(string? type)
    {
        string? normalizedType = type?.Trim().ToLowerInvariant();

        return normalizedType switch
        {
            "article" => AcademicWorkCategory.Article,
            "journal-article" => AcademicWorkCategory.Article,
            "magazine-article" => AcademicWorkCategory.Article,
            "newsletter-article" => AcademicWorkCategory.Article,
            "newspaper-article" => AcademicWorkCategory.Article,
            "book" => AcademicWorkCategory.Book,
            "book-chapter" => AcademicWorkCategory.BookChapter,
            "book-review" => AcademicWorkCategory.BookReview,
            "conference-abstract" => AcademicWorkCategory.ConferenceAbstract,
            "conference-paper" => AcademicWorkCategory.ConferencePaper,
            "conference-poster" => AcademicWorkCategory.ConferencePaper,
            "data-paper" => AcademicWorkCategory.DataPaper,
            "data-set" => AcademicWorkCategory.Dataset,
            "dataset" => AcademicWorkCategory.Dataset,
            "dissertation" => AcademicWorkCategory.Dissertation,
            "dissertation-thesis" => AcademicWorkCategory.Dissertation,
            "dictionary-entry" => AcademicWorkCategory.ReferenceEntry,
            "encyclopedia-entry" => AcademicWorkCategory.ReferenceEntry,
            "editorial" => AcademicWorkCategory.Editorial,
            "erratum" => AcademicWorkCategory.Erratum,
            "letter" => AcademicWorkCategory.Letter,
            "invention" => AcademicWorkCategory.Other,
            "artistic-performance" => AcademicWorkCategory.Other,
            "journal-issue" => AcademicWorkCategory.Other,
            "lecture-speech" => AcademicWorkCategory.Other,
            "license" => AcademicWorkCategory.Other,
            "manual" => AcademicWorkCategory.Other,
            "online-resource" => AcademicWorkCategory.Other,
            "other" => AcademicWorkCategory.Other,
            "patent" => AcademicWorkCategory.Other,
            "physical-object" => AcademicWorkCategory.Other,
            "preprint" => AcademicWorkCategory.Preprint,
            "registered-copyright" => AcademicWorkCategory.Other,
            "report" => AcademicWorkCategory.Report,
            "review" => AcademicWorkCategory.Review,
            "research-technique" => AcademicWorkCategory.Other,
            "software" => AcademicWorkCategory.Software,
            "spin-off-company" => AcademicWorkCategory.Other,
            "standards-and-policy" => AcademicWorkCategory.Standard,
            "supervised-student-publication" => AcademicWorkCategory.Other,
            "technical-standard" => AcademicWorkCategory.Standard,
            "test" => AcademicWorkCategory.Other,
            "trademark" => AcademicWorkCategory.Other,
            "translation" => AcademicWorkCategory.Other,
            "website" => AcademicWorkCategory.Other,
            "working-paper" => AcademicWorkCategory.Preprint,
            _ => AcademicWorkCategory.Unknown
        };
    }

    public AcademicWorkCategory GetWebOfScienceCategory(string? types)
    {
        List<string>? normalizedTypes = null;

        normalizedTypes = (types ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(type => type.Trim().ToLowerInvariant())
            .ToList();

        if (normalizedTypes.Contains("article"))
        {
            return AcademicWorkCategory.Article;
        }

        if (normalizedTypes.Contains("review"))
        {
            return AcademicWorkCategory.Review;
        }

        if (normalizedTypes.Contains("proceedings paper"))
        {
            return AcademicWorkCategory.ConferencePaper;
        }

        if (normalizedTypes.Contains("meeting abstract"))
        {
            return AcademicWorkCategory.ConferenceAbstract;
        }

        if (normalizedTypes.Contains("book chapter"))
        {
            return AcademicWorkCategory.BookChapter;
        }

        if (normalizedTypes.Contains("book"))
        {
            return AcademicWorkCategory.Book;
        }

        if (normalizedTypes.Contains("book review"))
        {
            return AcademicWorkCategory.BookReview;
        }

        if (normalizedTypes.Contains("editorial material"))
        {
            return AcademicWorkCategory.Editorial;
        }

        if (normalizedTypes.Contains("letter"))
        {
            return AcademicWorkCategory.Letter;
        }

        if (normalizedTypes.Contains("correction"))
        {
            return AcademicWorkCategory.Erratum;
        }

        if (normalizedTypes.Contains("retraction"))
        {
            return AcademicWorkCategory.Retraction;
        }

        if (normalizedTypes.Contains("data paper"))
        {
            return AcademicWorkCategory.DataPaper;
        }

        return AcademicWorkCategory.Unknown;
    }
}
