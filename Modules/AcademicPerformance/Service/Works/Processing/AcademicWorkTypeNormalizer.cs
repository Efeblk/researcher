using System.Globalization;
using System.Text;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;

public static class AcademicWorkTypeNormalizer
{
    public static AcademicWorkCategory Normalize(
        string? rawType,
        AcademicWorkCategorySource source)
    {
        if (source == AcademicWorkCategorySource.WebOfScience)
        {
            return NormalizeWebOfScience(rawType);
        }

        string type = NormalizeText(rawType);
        AcademicWorkCategory common = CommonCategory(type);
        if (common != AcademicWorkCategory.Unknown)
        {
            return common;
        }

        return source switch
        {
            AcademicWorkCategorySource.Scopus => ScopusCategory(type),
            AcademicWorkCategorySource.Orcid => OrcidCategory(type),
            AcademicWorkCategorySource.TrDizin => TrDizinCategory(type),
            AcademicWorkCategorySource.Crossref => CrossrefCategory(type),
            _ => AcademicWorkCategory.Unknown
        };
    }

    private static AcademicWorkCategory CommonCategory(string type) => type switch
    {
        "article" or "makale" => AcademicWorkCategory.Article,
        "book" or "kitap" => AcademicWorkCategory.Book,
        "book chapter" or "kitap bolumu" => AcademicWorkCategory.BookChapter,
        "book review" or "kitap incelemesi" => AcademicWorkCategory.BookReview,
        "conference abstract" or "meeting abstract" or "bildiri ozeti" => AcademicWorkCategory.ConferenceAbstract,
        "conference paper" or "proceedings paper" or "bildiri" => AcademicWorkCategory.ConferencePaper,
        "data paper" => AcademicWorkCategory.DataPaper,
        "data set" or "dataset" => AcademicWorkCategory.Dataset,
        "dissertation" or "tez" => AcademicWorkCategory.Dissertation,
        "editorial" => AcademicWorkCategory.Editorial,
        "erratum" or "correction" or "duzeltme" => AcademicWorkCategory.Erratum,
        "letter" or "mektup" => AcademicWorkCategory.Letter,
        "paratext" => AcademicWorkCategory.Paratext,
        "peer review" => AcademicWorkCategory.PeerReview,
        "preprint" => AcademicWorkCategory.Preprint,
        "reference entry" => AcademicWorkCategory.ReferenceEntry,
        "report" or "rapor" => AcademicWorkCategory.Report,
        "retraction" or "retracted" => AcademicWorkCategory.Retraction,
        "review" or "derleme" => AcademicWorkCategory.Review,
        "software" => AcademicWorkCategory.Software,
        "standard" => AcademicWorkCategory.Standard,
        "supplementary materials" => AcademicWorkCategory.SupplementaryMaterials,
        _ => AcademicWorkCategory.Unknown
    };

    private static AcademicWorkCategory ScopusCategory(string type) => type switch
    {
        "ar" => AcademicWorkCategory.Article,
        "bk" => AcademicWorkCategory.Book,
        "ch" => AcademicWorkCategory.BookChapter,
        "cp" => AcademicWorkCategory.ConferencePaper,
        "conference review" or "cr" or "re" or "short survey" or "sh" => AcademicWorkCategory.Review,
        "ed" => AcademicWorkCategory.Editorial,
        "le" => AcademicWorkCategory.Letter,
        "note" or "no" => AcademicWorkCategory.Other,
        "er" => AcademicWorkCategory.Erratum,
        _ => AcademicWorkCategory.Unknown
    };

    private static AcademicWorkCategory OrcidCategory(string type) => type switch
    {
        "journal article" or "magazine article" or "newsletter article" or "newspaper article" => AcademicWorkCategory.Article,
        "conference poster" => AcademicWorkCategory.ConferencePaper,
        "dissertation thesis" => AcademicWorkCategory.Dissertation,
        "dictionary entry" or "encyclopedia entry" => AcademicWorkCategory.ReferenceEntry,
        "patent" => AcademicWorkCategory.Patent,
        "working paper" => AcademicWorkCategory.Preprint,
        "standards and policy" or "technical standard" => AcademicWorkCategory.Standard,
        "invention" or "artistic performance" or "journal issue" or "lecture speech" or "license" or "manual"
            or "online resource" or "other" or "physical object" or "registered copyright" or "research technique"
            or "spin off company" or "supervised student publication" or "test" or "trademark" or "translation"
            or "website" => AcademicWorkCategory.Other,
        _ => AcademicWorkCategory.Unknown
    };

    private static AcademicWorkCategory CrossrefCategory(string type) => type switch
    {
        "journal article" or "research" or "paper" => AcademicWorkCategory.Article,
        "monograph" => AcademicWorkCategory.Book,
        "proceedings article" => AcademicWorkCategory.ConferencePaper,
        "compilation" => AcademicWorkCategory.Review,
        _ => AcademicWorkCategory.Unknown
    };

    private static AcademicWorkCategory TrDizinCategory(string type) => type switch
    {
        "research" or "paper" or "journal article" => AcademicWorkCategory.Article,
        "monograph" => AcademicWorkCategory.Book,
        "proceedings article" => AcademicWorkCategory.ConferencePaper,
        "compilation" => AcademicWorkCategory.Review,
        "fact presentation" or "case report" => AcademicWorkCategory.Article,
        "book presentation" => AcademicWorkCategory.BookReview,
        "letter to editor" => AcademicWorkCategory.Letter,
        "meeting summary" => AcademicWorkCategory.ConferenceAbstract,
        "short report" => AcademicWorkCategory.Report,
        "translation" or "other" => AcademicWorkCategory.Other,
        _ => AcademicWorkCategory.Unknown
    };

    private static AcademicWorkCategory NormalizeWebOfScience(string? rawTypes)
    {
        List<AcademicWorkCategory> categories = (rawTypes ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeText)
            .Select(type => type == "editorial material"
                ? AcademicWorkCategory.Editorial
                : CommonCategory(type))
            .ToList();
        AcademicWorkCategory[] precedence =
        [
            AcademicWorkCategory.Article,
            AcademicWorkCategory.Review,
            AcademicWorkCategory.ConferencePaper,
            AcademicWorkCategory.ConferenceAbstract,
            AcademicWorkCategory.BookChapter,
            AcademicWorkCategory.Book,
            AcademicWorkCategory.BookReview,
            AcademicWorkCategory.Editorial,
            AcademicWorkCategory.Letter,
            AcademicWorkCategory.Erratum,
            AcademicWorkCategory.Retraction,
            AcademicWorkCategory.DataPaper
        ];

        foreach (AcademicWorkCategory category in precedence)
        {
            if (categories.Contains(category)) return category;
        }

        return categories.FirstOrDefault(category => category != AcademicWorkCategory.Unknown);
    }

    private static string NormalizeText(string? value)
    {
        StringBuilder result = new();
        bool previousWasSeparator = true;

        foreach (char character in (value ?? string.Empty).Trim().Normalize(NormalizationForm.FormD))
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            char normalized = char.ToLowerInvariant(character);
            normalized = normalized == 'ı' ? 'i' : normalized;
            if (char.IsLetterOrDigit(normalized))
            {
                result.Append(normalized);
                previousWasSeparator = false;
            }
            else if (!previousWasSeparator)
            {
                result.Append(' ');
                previousWasSeparator = true;
            }
        }

        return result.ToString().TrimEnd();
    }
}
