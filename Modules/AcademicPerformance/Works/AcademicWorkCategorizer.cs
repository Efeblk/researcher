using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Orcid;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Works;

public sealed class AcademicWorkCategorizer
{
    public void Categorize(Researcher researcher)
    {
        List<OrcidWork>? works = researcher.OrcidProfile?.Works;
        int index = 0;

        if (works is null)
        {
            return;
        }

        for (index = 0; index < works.Count; index++)
        {
            works[index].Category = GetOrcidCategory(works[index].WorkType);
            works[index].CategorySource = AcademicWorkCategorySource.Orcid;
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
}
