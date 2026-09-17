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
}
