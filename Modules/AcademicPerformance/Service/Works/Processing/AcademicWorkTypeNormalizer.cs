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
        "article" or "makale" or "arastirma makalesi" => AcademicWorkCategory.Article,
        "book" or "kitap" => AcademicWorkCategory.Book,
        "book chapter" or "kitap bolumu" => AcademicWorkCategory.BookChapter,
        "book review" or "kitap incelemesi" or "kitap tanitimi" => AcademicWorkCategory.BookReview,
        "conference abstract" or "meeting abstract" or "bildiri ozeti" => AcademicWorkCategory.ConferenceAbstract,
        "conference paper" or "proceedings paper" or "bildiri" or "kongre bildirisi" => AcademicWorkCategory.ConferencePaper,
        "data paper" or "veri makalesi" => AcademicWorkCategory.DataPaper,
        "data set" or "dataset" or "veri seti" or "veriseti" => AcademicWorkCategory.Dataset,
        "dissertation" or "thesis" or "tez" => AcademicWorkCategory.Dissertation,
        "editorial" or "editorial material" or "editor yazisi" => AcademicWorkCategory.Editorial,
        "erratum" or "correction" or "duzeltme" => AcademicWorkCategory.Erratum,
        "letter" or "mektup" => AcademicWorkCategory.Letter,
        "libguide" or "libguides" or "kutuphane rehberi" => AcademicWorkCategory.LibGuide,
        "other" or "diger" => AcademicWorkCategory.Other,
        "patent" => AcademicWorkCategory.Patent,
        "paratext" or "yan metin" => AcademicWorkCategory.Paratext,
        "peer review" or "hakemlik" or "hakem degerlendirmesi" => AcademicWorkCategory.PeerReview,
        "preprint" or "on baski" or "onbaski" => AcademicWorkCategory.Preprint,
        "reference entry" or "referans maddesi" => AcademicWorkCategory.ReferenceEntry,
        "report" or "rapor" => AcademicWorkCategory.Report,
        "retraction" or "geri cekme" or "geri cekme bildirimi" or "geri cekilme bildirimi" => AcademicWorkCategory.Retraction,
        "review" or "derleme" or "literatur derlemesi" => AcademicWorkCategory.Review,
        "software" or "yazilim" => AcademicWorkCategory.Software,
        "software paper" or "yazilim makalesi" => AcademicWorkCategory.SoftwarePaper,
        "standard" or "standart" => AcademicWorkCategory.Standard,
        "supplementary materials" or "supplementary material" or "ek materyal" or "ek malzeme" => AcademicWorkCategory.SupplementaryMaterials,
        _ => AcademicWorkCategory.Unknown
    };

    private static AcademicWorkCategory ScopusCategory(string type) => type switch
    {
        "ar" or "business article" or "bz" => AcademicWorkCategory.Article,
        "abstract report" or "ab" => AcademicWorkCategory.Other,
        "bk" => AcademicWorkCategory.Book,
        "ch" => AcademicWorkCategory.BookChapter,
        "cp" => AcademicWorkCategory.ConferencePaper,
        "conference review" or "cr" or "re" or "short survey" or "sh" => AcademicWorkCategory.Review,
        "data paper" or "dp" => AcademicWorkCategory.DataPaper,
        "ed" => AcademicWorkCategory.Editorial,
        "le" => AcademicWorkCategory.Letter,
        "note" or "no" or "press release" or "pr" => AcademicWorkCategory.Other,
        "er" => AcademicWorkCategory.Erratum,
        _ => AcademicWorkCategory.Unknown
    };

    private static AcademicWorkCategory OrcidCategory(string type) => type switch
    {
        "journal article" or "article journal" or "magazine article" or "newsletter article" or "newspaper article" => AcademicWorkCategory.Article,
        "chapter" => AcademicWorkCategory.BookChapter,
        "conference poster" or "conference presentation" => AcademicWorkCategory.ConferencePaper,
        "dissertation thesis" => AcademicWorkCategory.Dissertation,
        "dictionary entry" or "encyclopedia entry" => AcademicWorkCategory.ReferenceEntry,
        "edited book" => AcademicWorkCategory.Book,
        "patent" => AcademicWorkCategory.Patent,
        "working paper" => AcademicWorkCategory.Preprint,
        "standards and policy" or "technical standard" => AcademicWorkCategory.Standard,
        "annotation" or "artistic performance" or "blog post" or "cartographic material" or "clinical study"
            or "conference output" or "conference proceedings"
            or "data management plan" or "design" or "disclosure" or "image" or "invention" or "journal issue"
            or "learning object" or "lecture speech" or "license" or "manual" or "moving image" or "musical composition"
            or "online resource" or "physical object" or "registered copyright" or "research technique" or "research tool"
            or "sound" or "spin off company" or "supervised student publication" or "test" or "trademark"
            or "transcription" or "translation" or "website" => AcademicWorkCategory.Other,
        "undefined" => AcademicWorkCategory.Unknown,
        _ => AcademicWorkCategory.Unknown
    };

    private static AcademicWorkCategory CrossrefCategory(string type) => type switch
    {
        "journal article" or "research" or "paper" => AcademicWorkCategory.Article,
        "monograph" or "edited book" or "reference book" => AcademicWorkCategory.Book,
        "book section" or "book part" => AcademicWorkCategory.BookChapter,
        "proceedings article" => AcademicWorkCategory.ConferencePaper,
        "dissertation" => AcademicWorkCategory.Dissertation,
        "peer review" => AcademicWorkCategory.PeerReview,
        "report component" => AcademicWorkCategory.Report,
        "database" => AcademicWorkCategory.Dataset,
        "compilation" => AcademicWorkCategory.Review,
        "book track" or "journal volume" or "book set" or "journal" or "component" or "proceedings series"
            or "report series" or "proceedings" or "grant" or "book series" or "journal issue"
            or "posted content" => AcademicWorkCategory.Other,
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
        "retracted" => AcademicWorkCategory.Retraction,
        "translation" or "other" => AcademicWorkCategory.Other,
        _ => AcademicWorkCategory.Unknown
    };

    private static AcademicWorkCategory NormalizeWebOfScience(string? rawTypes)
    {
        List<AcademicWorkCategory> categories = (rawTypes ?? string.Empty)
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeText)
            .Select(WebOfScienceCategory)
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
            AcademicWorkCategory.DataPaper,
            AcademicWorkCategory.Dataset,
            AcademicWorkCategory.SoftwarePaper,
            AcademicWorkCategory.Software,
            AcademicWorkCategory.Other
        ];

        foreach (AcademicWorkCategory category in precedence)
        {
            if (categories.Contains(category)) return category;
        }

        return categories.FirstOrDefault(category => category != AcademicWorkCategory.Unknown);
    }

    private static AcademicWorkCategory WebOfScienceCategory(string type)
    {
        AcademicWorkCategory common = CommonCategory(type);
        if (common != AcademicWorkCategory.Unknown)
        {
            return common;
        }

        return type switch
        {
            "article early access" => AcademicWorkCategory.Article,
            "review article" => AcademicWorkCategory.Review,
            "correction addition" => AcademicWorkCategory.Erratum,
            "software review" => AcademicWorkCategory.Other,
            "art exhibit review" or "bibliography" or "biographical item" or "chronology" or "database review"
                or "creative prose" or "dance performance review" or "discussion" or "excerpt" or "fiction"
                or "fiction creative prose" or "film review"
                or "hardware review" or "item about an individual" or "meeting summary" or "music performance review"
                or "music score" or "music score review" or "news item" or "note" or "poetry" or "radio review"
                or "record review" or "script" or "theater review" or "tv review" or "tv review radio review"
                or "video review" or "meeting" or "item withdrawal" or "expression of concern" or "abstract"
                or "abstract of a published item" or "reprint" => AcademicWorkCategory.Other,
            // These are publication statuses, not document types. A companion document type still wins by precedence.
            "early access" or "retracted publication" or "withdrawn publication"
                or "publication with expression of concern" => AcademicWorkCategory.Unknown,
            _ => AcademicWorkCategory.Unknown
        };
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
