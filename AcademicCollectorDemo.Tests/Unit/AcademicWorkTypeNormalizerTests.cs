using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class AcademicWorkTypeNormalizerTests
{
    [Theory]
    [InlineData("Article", AcademicWorkCategory.Article)]
    [InlineData("MAKALE", AcademicWorkCategory.Article)]
    [InlineData("derleme", AcademicWorkCategory.Review)]
    [InlineData("conference-paper", AcademicWorkCategory.ConferencePaper)]
    [InlineData("BİLDİRİ", AcademicWorkCategory.ConferencePaper)]
    [InlineData("kitap", AcademicWorkCategory.Book)]
    [InlineData("KİTAP_BÖLÜMÜ", AcademicWorkCategory.BookChapter)]
    [InlineData("book/review", AcademicWorkCategory.BookReview)]
    public void Normalize_CommonAliases_IgnoresCaseDiacriticsAndSeparators(
        string type,
        AcademicWorkCategory expected)
    {
        Assert.Equal(expected, AcademicWorkTypeNormalizer.Normalize(
            type, AcademicWorkCategorySource.Orcid));
    }

    [Theory]
    [InlineData(AcademicWorkCategorySource.Orcid)]
    [InlineData(AcademicWorkCategorySource.WebOfScience)]
    [InlineData(AcademicWorkCategorySource.OpenAlex)]
    [InlineData(AcademicWorkCategorySource.Scopus)]
    [InlineData(AcademicWorkCategorySource.TrDizin)]
    [InlineData(AcademicWorkCategorySource.Crossref)]
    public void Normalize_SharedTurkishAlias_AppliesAcrossProviders(
        AcademicWorkCategorySource source)
    {
        Assert.Equal(AcademicWorkCategory.BookChapter, AcademicWorkTypeNormalizer.Normalize(
            "KİTAP_BÖLÜMÜ", source));
    }

    [Theory]
    [InlineData("ar", AcademicWorkCategory.Article)]
    [InlineData("bk", AcademicWorkCategory.Book)]
    [InlineData("ch", AcademicWorkCategory.BookChapter)]
    [InlineData("cp", AcademicWorkCategory.ConferencePaper)]
    [InlineData("cr", AcademicWorkCategory.Review)]
    [InlineData("sh", AcademicWorkCategory.Review)]
    public void Normalize_ScopusShortCodes_PreservesProviderMappings(
        string type,
        AcademicWorkCategory expected)
    {
        Assert.Equal(expected, AcademicWorkTypeNormalizer.Normalize(
            type, AcademicWorkCategorySource.Scopus));
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("bk")]
    [InlineData("ch")]
    public void Normalize_ScopusShortCodes_DoNotLeakToOtherProviders(string type)
    {
        Assert.Equal(AcademicWorkCategory.Unknown, AcademicWorkTypeNormalizer.Normalize(
            type, AcademicWorkCategorySource.Orcid));
    }

    [Theory]
    [InlineData("BOOK_PRESENTATION", AcademicWorkCategory.BookReview)]
    [InlineData("MEETING_SUMMARY", AcademicWorkCategory.ConferenceAbstract)]
    [InlineData("RETRACTED", AcademicWorkCategory.Retraction)]
    [InlineData("LETTER_TO_EDITOR", AcademicWorkCategory.Letter)]
    [InlineData("SHORT_REPORT", AcademicWorkCategory.Report)]
    [InlineData("OTHER", AcademicWorkCategory.Other)]
    public void Normalize_TrDizinOfficialTypes_UsesExplicitMappings(
        string type,
        AcademicWorkCategory expected)
    {
        Assert.Equal(expected, AcademicWorkTypeNormalizer.Normalize(
            type, AcademicWorkCategorySource.TrDizin));
    }

    [Fact]
    public void Normalize_OrcidPatent_UsesPatentCategory()
    {
        Assert.Equal(AcademicWorkCategory.Patent, AcademicWorkTypeNormalizer.Normalize(
            "patent", AcademicWorkCategorySource.Orcid));
    }

    [Theory]
    [InlineData("Review, Article", AcademicWorkCategory.Article)]
    [InlineData("Book Review, Book", AcademicWorkCategory.Book)]
    [InlineData("Book Review", AcademicWorkCategory.BookReview)]
    [InlineData("Editorial Material, Letter", AcademicWorkCategory.Editorial)]
    [InlineData("derleme, makale", AcademicWorkCategory.Article)]
    [InlineData("kitap_bölümü", AcademicWorkCategory.BookChapter)]
    public void Normalize_WebOfScienceMultipleTypes_PreservesEstablishedPrecedence(
        string types,
        AcademicWorkCategory expected)
    {
        Assert.Equal(expected, AcademicWorkTypeNormalizer.Normalize(
            types, AcademicWorkCategorySource.WebOfScience));
    }

    [Fact]
    public void Normalize_UnknownValue_RemainsUnknown()
    {
        Assert.Equal(AcademicWorkCategory.Unknown, AcademicWorkTypeNormalizer.Normalize(
            "unmapped-provider-value", AcademicWorkCategorySource.Crossref));
    }

    [Theory]
    [MemberData(nameof(OfficialProviderTypes))]
    public void Normalize_OfficialProviderTypes_UsesExplicitConservativeMapping(
        AcademicWorkCategorySource source,
        string type,
        AcademicWorkCategory expected)
    {
        Assert.Equal(expected, AcademicWorkTypeNormalizer.Normalize(type, source));
    }

    [Theory]
    [InlineData("makale", AcademicWorkCategory.Article)]
    [InlineData("kitap", AcademicWorkCategory.Book)]
    [InlineData("kitap bölümü", AcademicWorkCategory.BookChapter)]
    [InlineData("kitap incelemesi", AcademicWorkCategory.BookReview)]
    [InlineData("bildiri özeti", AcademicWorkCategory.ConferenceAbstract)]
    [InlineData("bildiri", AcademicWorkCategory.ConferencePaper)]
    [InlineData("veri makalesi", AcademicWorkCategory.DataPaper)]
    [InlineData("veri seti", AcademicWorkCategory.Dataset)]
    [InlineData("tez", AcademicWorkCategory.Dissertation)]
    [InlineData("editör yazısı", AcademicWorkCategory.Editorial)]
    [InlineData("düzeltme", AcademicWorkCategory.Erratum)]
    [InlineData("mektup", AcademicWorkCategory.Letter)]
    [InlineData("kütüphane rehberi", AcademicWorkCategory.LibGuide)]
    [InlineData("diğer", AcademicWorkCategory.Other)]
    [InlineData("patent", AcademicWorkCategory.Patent)]
    [InlineData("yan metin", AcademicWorkCategory.Paratext)]
    [InlineData("hakemlik", AcademicWorkCategory.PeerReview)]
    [InlineData("önbaskı", AcademicWorkCategory.Preprint)]
    [InlineData("referans maddesi", AcademicWorkCategory.ReferenceEntry)]
    [InlineData("rapor", AcademicWorkCategory.Report)]
    [InlineData("geri çekme bildirimi", AcademicWorkCategory.Retraction)]
    [InlineData("derleme", AcademicWorkCategory.Review)]
    [InlineData("yazılım", AcademicWorkCategory.Software)]
    [InlineData("yazılım makalesi", AcademicWorkCategory.SoftwarePaper)]
    [InlineData("standart", AcademicWorkCategory.Standard)]
    [InlineData("ek materyal", AcademicWorkCategory.SupplementaryMaterials)]
    public void Normalize_TurkishTaxonomyAlias_AppliesAcrossProviders(
        string type,
        AcademicWorkCategory expected)
    {
        foreach (AcademicWorkCategorySource source in Enum.GetValues<AcademicWorkCategorySource>())
        {
            Assert.Equal(expected, AcademicWorkTypeNormalizer.Normalize(type, source));
        }
    }

    [Fact]
    public void Normalize_EveryCanonicalCategory_HasAKnownAlias()
    {
        AcademicWorkCategory[] normalized =
        [
            AcademicWorkTypeNormalizer.Normalize("unknown input", AcademicWorkCategorySource.OpenAlex),
            .. Enum.GetValues<AcademicWorkCategory>()
                .Where(category => category != AcademicWorkCategory.Unknown)
                .Select(category => AcademicWorkTypeNormalizer.Normalize(TurkishAlias(category), AcademicWorkCategorySource.OpenAlex))
        ];

        Assert.Equal(Enum.GetValues<AcademicWorkCategory>().Order(), normalized.Order());
    }

    [Theory]
    [InlineData("Review", AcademicWorkCategory.Review)]
    [InlineData("Peer Review", AcademicWorkCategory.PeerReview)]
    [InlineData("Data Paper", AcademicWorkCategory.DataPaper)]
    [InlineData("Dataset", AcademicWorkCategory.Dataset)]
    [InlineData("Retraction", AcademicWorkCategory.Retraction)]
    [InlineData("Article", AcademicWorkCategory.Article)]
    public void Normalize_RelatedKinds_RemainDistinct(string type, AcademicWorkCategory expected)
    {
        Assert.Equal(expected, AcademicWorkTypeNormalizer.Normalize(type, AcademicWorkCategorySource.OpenAlex));
    }

    [Theory]
    [InlineData("Retracted Publication")]
    [InlineData("Early Access")]
    [InlineData("Publication with Expression of Concern")]
    public void Normalize_WebOfScienceStatusWithoutDocumentType_RemainsUnknown(string status)
    {
        Assert.Equal(AcademicWorkCategory.Unknown, AcademicWorkTypeNormalizer.Normalize(
            status, AcademicWorkCategorySource.WebOfScience));
    }

    [Theory]
    [InlineData(AcademicWorkCategorySource.Orcid)]
    [InlineData(AcademicWorkCategorySource.OpenAlex)]
    [InlineData(AcademicWorkCategorySource.Crossref)]
    [InlineData(AcademicWorkCategorySource.Scopus)]
    public void Normalize_AmbiguousRetractedStatusOutsideTrDizin_RemainsUnknown(
        AcademicWorkCategorySource source)
    {
        Assert.Equal(AcademicWorkCategory.Unknown, AcademicWorkTypeNormalizer.Normalize("retracted", source));
    }

    [Fact]
    public void Normalize_WebOfScienceSemicolonTypes_IgnoresStatusAndUsesDocumentType()
    {
        Assert.Equal(AcademicWorkCategory.DataPaper, AcademicWorkTypeNormalizer.Normalize(
            "Retracted Publication; Data Paper", AcademicWorkCategorySource.WebOfScience));
    }

    [Theory]
    [InlineData("Software Review", AcademicWorkCategory.Other)]
    [InlineData("Fiction, Creative Prose", AcademicWorkCategory.Other)]
    [InlineData("TV Review, Radio Review", AcademicWorkCategory.Other)]
    [InlineData("Video Review", AcademicWorkCategory.Other)]
    [InlineData("Meeting", AcademicWorkCategory.Other)]
    [InlineData("Item Withdrawal", AcademicWorkCategory.Other)]
    [InlineData("Expression of Concern", AcademicWorkCategory.Other)]
    [InlineData("Abstract", AcademicWorkCategory.Other)]
    [InlineData("Correction, Addition", AcademicWorkCategory.Erratum)]
    public void Normalize_WebOfScienceRecognizedTypes_DoNotFallThrough(
        string type,
        AcademicWorkCategory expected)
    {
        Assert.Equal(expected, AcademicWorkTypeNormalizer.Normalize(
            type, AcademicWorkCategorySource.WebOfScience));
    }

    public static TheoryData<AcademicWorkCategorySource, string, AcademicWorkCategory> OfficialProviderTypes()
    {
        TheoryData<AcademicWorkCategorySource, string, AcademicWorkCategory> data = new();

        Add(data, AcademicWorkCategorySource.OpenAlex, AcademicWorkCategory.Other, "other");
        Add(data, AcademicWorkCategorySource.OpenAlex, AcademicWorkCategory.LibGuide, "libguides");
        Add(data, AcademicWorkCategorySource.OpenAlex, AcademicWorkCategory.SoftwarePaper, "software-paper");
        Add(data, AcademicWorkCategorySource.OpenAlex, AcademicWorkCategory.Article, "article");
        Add(data, AcademicWorkCategorySource.OpenAlex, AcademicWorkCategory.Book, "book");
        Add(data, AcademicWorkCategorySource.OpenAlex, AcademicWorkCategory.BookChapter, "book-chapter");
        Add(data, AcademicWorkCategorySource.OpenAlex, AcademicWorkCategory.BookReview, "book-review");
        Add(data, AcademicWorkCategorySource.OpenAlex, AcademicWorkCategory.ConferenceAbstract, "conference-abstract");
        Add(data, AcademicWorkCategorySource.OpenAlex, AcademicWorkCategory.ConferencePaper, "conference-paper");
        Add(data, AcademicWorkCategorySource.OpenAlex, AcademicWorkCategory.DataPaper, "data-paper");
        Add(data, AcademicWorkCategorySource.OpenAlex, AcademicWorkCategory.Dataset, "dataset");
        Add(data, AcademicWorkCategorySource.OpenAlex, AcademicWorkCategory.Dissertation, "dissertation");
        Add(data, AcademicWorkCategorySource.OpenAlex, AcademicWorkCategory.Editorial, "editorial");
        Add(data, AcademicWorkCategorySource.OpenAlex, AcademicWorkCategory.Erratum, "erratum");
        Add(data, AcademicWorkCategorySource.OpenAlex, AcademicWorkCategory.Letter, "letter");
        Add(data, AcademicWorkCategorySource.OpenAlex, AcademicWorkCategory.Paratext, "paratext");
        Add(data, AcademicWorkCategorySource.OpenAlex, AcademicWorkCategory.PeerReview, "peer-review");
        Add(data, AcademicWorkCategorySource.OpenAlex, AcademicWorkCategory.Preprint, "preprint");
        Add(data, AcademicWorkCategorySource.OpenAlex, AcademicWorkCategory.ReferenceEntry, "reference-entry");
        Add(data, AcademicWorkCategorySource.OpenAlex, AcademicWorkCategory.Report, "report");
        Add(data, AcademicWorkCategorySource.OpenAlex, AcademicWorkCategory.Retraction, "retraction");
        Add(data, AcademicWorkCategorySource.OpenAlex, AcademicWorkCategory.Review, "review");
        Add(data, AcademicWorkCategorySource.OpenAlex, AcademicWorkCategory.Software, "software");
        Add(data, AcademicWorkCategorySource.OpenAlex, AcademicWorkCategory.Standard, "standard");
        Add(data, AcademicWorkCategorySource.OpenAlex, AcademicWorkCategory.SupplementaryMaterials, "supplementary-materials");

        Add(data, AcademicWorkCategorySource.Crossref, AcademicWorkCategory.BookChapter, "book-section", "book-part", "book-chapter");
        Add(data, AcademicWorkCategorySource.Crossref, AcademicWorkCategory.Book, "monograph", "book", "edited-book", "reference-book");
        Add(data, AcademicWorkCategorySource.Crossref, AcademicWorkCategory.Article, "journal-article");
        Add(data, AcademicWorkCategorySource.Crossref, AcademicWorkCategory.ConferencePaper, "proceedings-article");
        Add(data, AcademicWorkCategorySource.Crossref, AcademicWorkCategory.Report, "report", "report-component");
        Add(data, AcademicWorkCategorySource.Crossref, AcademicWorkCategory.PeerReview, "peer-review");
        Add(data, AcademicWorkCategorySource.Crossref, AcademicWorkCategory.ReferenceEntry, "reference-entry");
        Add(data, AcademicWorkCategorySource.Crossref, AcademicWorkCategory.Dissertation, "dissertation");
        Add(data, AcademicWorkCategorySource.Crossref, AcademicWorkCategory.Dataset, "dataset", "database");
        Add(data, AcademicWorkCategorySource.Crossref, AcademicWorkCategory.Standard, "standard");
        Add(data, AcademicWorkCategorySource.Crossref, AcademicWorkCategory.Other, "book-track", "journal-volume", "book-set", "journal", "component", "proceedings-series", "report-series", "proceedings", "grant", "book-series", "journal-issue", "posted-content", "other");

        Add(data, AcademicWorkCategorySource.Scopus, AcademicWorkCategory.Article, "ar", "bz");
        Add(data, AcademicWorkCategorySource.Scopus, AcademicWorkCategory.Other, "ab", "no", "pr");
        Add(data, AcademicWorkCategorySource.Scopus, AcademicWorkCategory.Book, "bk");
        Add(data, AcademicWorkCategorySource.Scopus, AcademicWorkCategory.BookChapter, "ch");
        Add(data, AcademicWorkCategorySource.Scopus, AcademicWorkCategory.ConferencePaper, "cp");
        Add(data, AcademicWorkCategorySource.Scopus, AcademicWorkCategory.Review, "cr", "re", "sh");
        Add(data, AcademicWorkCategorySource.Scopus, AcademicWorkCategory.DataPaper, "dp");
        Add(data, AcademicWorkCategorySource.Scopus, AcademicWorkCategory.Editorial, "ed");
        Add(data, AcademicWorkCategorySource.Scopus, AcademicWorkCategory.Erratum, "er");
        Add(data, AcademicWorkCategorySource.Scopus, AcademicWorkCategory.Letter, "le");

        Add(data, AcademicWorkCategorySource.Orcid, AcademicWorkCategory.Other, "annotation", "artistic-performance", "blog-post", "cartographic-material", "clinical-study", "conference-output", "conference-proceedings", "data-management-plan", "design", "disclosure", "image", "invention", "journal-issue", "learning-object", "lecture-speech", "license", "manual", "moving-image", "musical-composition", "online-resource", "other", "physical-object", "registered-copyright", "research-technique", "research-tool", "sound", "spin-off-company", "supervised-student-publication", "test", "trademark", "transcription", "translation", "website");
        Add(data, AcademicWorkCategorySource.Orcid, AcademicWorkCategory.Article, "journal-article", "magazine-article", "newsletter-article", "newspaper-article");
        Add(data, AcademicWorkCategorySource.Orcid, AcademicWorkCategory.Book, "book", "edited-book");
        Add(data, AcademicWorkCategorySource.Orcid, AcademicWorkCategory.BookChapter, "book-chapter");
        Add(data, AcademicWorkCategorySource.Orcid, AcademicWorkCategory.BookReview, "book-review");
        Add(data, AcademicWorkCategorySource.Orcid, AcademicWorkCategory.ConferenceAbstract, "conference-abstract");
        Add(data, AcademicWorkCategorySource.Orcid, AcademicWorkCategory.ConferencePaper, "conference-paper", "conference-poster", "conference-presentation");
        Add(data, AcademicWorkCategorySource.Orcid, AcademicWorkCategory.Dataset, "data-set");
        Add(data, AcademicWorkCategorySource.Orcid, AcademicWorkCategory.Dissertation, "dissertation-thesis");
        Add(data, AcademicWorkCategorySource.Orcid, AcademicWorkCategory.Patent, "patent");
        Add(data, AcademicWorkCategorySource.Orcid, AcademicWorkCategory.Preprint, "preprint", "working-paper");
        Add(data, AcademicWorkCategorySource.Orcid, AcademicWorkCategory.ReferenceEntry, "dictionary-entry", "encyclopedia-entry");
        Add(data, AcademicWorkCategorySource.Orcid, AcademicWorkCategory.Report, "report");
        Add(data, AcademicWorkCategorySource.Orcid, AcademicWorkCategory.Software, "software");
        Add(data, AcademicWorkCategorySource.Orcid, AcademicWorkCategory.Standard, "standards-and-policy", "technical-standard");
        Add(data, AcademicWorkCategorySource.Orcid, AcademicWorkCategory.Unknown, "undefined");

        Add(data, AcademicWorkCategorySource.WebOfScience, AcademicWorkCategory.Article, "Article");
        Add(data, AcademicWorkCategorySource.WebOfScience, AcademicWorkCategory.Review, "Review", "Review Article");
        Add(data, AcademicWorkCategorySource.WebOfScience, AcademicWorkCategory.ConferencePaper, "Proceedings Paper");
        Add(data, AcademicWorkCategorySource.WebOfScience, AcademicWorkCategory.ConferenceAbstract, "Meeting Abstract");
        Add(data, AcademicWorkCategorySource.WebOfScience, AcademicWorkCategory.BookChapter, "Book Chapter");
        Add(data, AcademicWorkCategorySource.WebOfScience, AcademicWorkCategory.Book, "Book");
        Add(data, AcademicWorkCategorySource.WebOfScience, AcademicWorkCategory.BookReview, "Book Review");
        Add(data, AcademicWorkCategorySource.WebOfScience, AcademicWorkCategory.Editorial, "Editorial Material");
        Add(data, AcademicWorkCategorySource.WebOfScience, AcademicWorkCategory.Letter, "Letter");
        Add(data, AcademicWorkCategorySource.WebOfScience, AcademicWorkCategory.Erratum, "Correction", "Correction, Addition");
        Add(data, AcademicWorkCategorySource.WebOfScience, AcademicWorkCategory.Retraction, "Retraction");
        Add(data, AcademicWorkCategorySource.WebOfScience, AcademicWorkCategory.DataPaper, "Data Paper");
        Add(data, AcademicWorkCategorySource.WebOfScience, AcademicWorkCategory.Other, "Abstract", "Art Exhibit Review", "Bibliography", "Biographical-Item", "Chronology", "Creative Prose", "Dance Performance Review", "Database Review", "Discussion", "Excerpt", "Fiction", "Film Review", "Hardware Review", "Item About an Individual", "Item Withdrawal", "Meeting", "Meeting Summary", "Music Performance Review", "Music Score", "Music Score Review", "News Item", "Note", "Poetry", "Radio Review", "Record Review", "Script", "Software Review", "Theater Review", "TV Review", "Video Review", "Expression of Concern");

        Add(data, AcademicWorkCategorySource.TrDizin, AcademicWorkCategory.Article, "RESEARCH", "PAPER", "JOURNAL_ARTICLE", "FACT_PRESENTATION", "CASE_REPORT");
        Add(data, AcademicWorkCategorySource.TrDizin, AcademicWorkCategory.Book, "MONOGRAPH");
        Add(data, AcademicWorkCategorySource.TrDizin, AcademicWorkCategory.ConferencePaper, "PROCEEDINGS_ARTICLE");
        Add(data, AcademicWorkCategorySource.TrDizin, AcademicWorkCategory.Review, "COMPILATION");
        Add(data, AcademicWorkCategorySource.TrDizin, AcademicWorkCategory.BookReview, "BOOK_PRESENTATION");
        Add(data, AcademicWorkCategorySource.TrDizin, AcademicWorkCategory.Letter, "LETTER_TO_EDITOR");
        Add(data, AcademicWorkCategorySource.TrDizin, AcademicWorkCategory.ConferenceAbstract, "MEETING_SUMMARY");
        Add(data, AcademicWorkCategorySource.TrDizin, AcademicWorkCategory.Report, "SHORT_REPORT");
        Add(data, AcademicWorkCategorySource.TrDizin, AcademicWorkCategory.Retraction, "RETRACTED");
        Add(data, AcademicWorkCategorySource.TrDizin, AcademicWorkCategory.Other, "TRANSLATION", "OTHER");

        return data;
    }

    private static void Add(
        TheoryData<AcademicWorkCategorySource, string, AcademicWorkCategory> data,
        AcademicWorkCategorySource source,
        AcademicWorkCategory category,
        params string[] types)
    {
        foreach (string type in types)
        {
            data.Add(source, type, category);
        }
    }

    private static string TurkishAlias(AcademicWorkCategory category) => category switch
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
        AcademicWorkCategory.Editorial => "editör yazısı",
        AcademicWorkCategory.Erratum => "düzeltme",
        AcademicWorkCategory.Letter => "mektup",
        AcademicWorkCategory.LibGuide => "kütüphane rehberi",
        AcademicWorkCategory.Other => "diğer",
        AcademicWorkCategory.Patent => "patent",
        AcademicWorkCategory.Paratext => "yan metin",
        AcademicWorkCategory.PeerReview => "hakemlik",
        AcademicWorkCategory.Preprint => "önbaskı",
        AcademicWorkCategory.ReferenceEntry => "referans maddesi",
        AcademicWorkCategory.Report => "rapor",
        AcademicWorkCategory.Retraction => "geri çekme bildirimi",
        AcademicWorkCategory.Review => "derleme",
        AcademicWorkCategory.Software => "yazılım",
        AcademicWorkCategory.SoftwarePaper => "yazılım makalesi",
        AcademicWorkCategory.Standard => "standart",
        AcademicWorkCategory.SupplementaryMaterials => "ek materyal",
        _ => "unknown input"
    };
}
