using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class AcademicWorkTitleNormalizerTests
{
    [Theory]
    [InlineData("İSTATİSTİK\u200B&amp; BİLİMİ: CO\u00ADOP\u200DERATION α")]
    [InlineData("istatistik & bilimi: cooperation &#945;")]
    [InlineData("&lt;strong&gt;ISTATISTIK&lt;/strong&gt; &amp; BILIMI:&lt;br&gt;COOPERATION α")]
    public void Normalize_ProviderPresentationVariants_ReturnsSameTitle(string title)
    {
        Assert.Equal("istatistik bilimi cooperation α", AcademicWorkTitleNormalizer.Normalize(title));
    }

    [Fact]
    public void Normalize_PairedInlineMarkup_RemovesTagsWithoutSeparatingText()
    {
        Assert.Equal("microscope x2", AcademicWorkTitleNormalizer.Normalize(
            "micro&lt;em class='source'&gt;scope&lt;/em&gt; x&lt;sup&gt;2&lt;/sup&gt;"));
    }

    [Fact]
    public void Normalize_LoneOrInvalidAngleNotation_PreservesMathematicalContent()
    {
        Assert.NotEqual(
            AcademicWorkTitleNormalizer.Normalize("Response of <i> + y"),
            AcademicWorkTitleNormalizer.Normalize("Response of + y"));
        Assert.NotEqual(
            AcademicWorkTitleNormalizer.Normalize("a < i + j > b"),
            AcademicWorkTitleNormalizer.Normalize("a b"));
    }

    [Fact]
    public void Normalize_DifferentMathSymbols_RemainDifferent()
    {
        Assert.NotEqual(
            AcademicWorkTitleNormalizer.Normalize("A + B"),
            AcademicWorkTitleNormalizer.Normalize("A − B"));
    }

    [Fact]
    public void Normalize_TurkishDotlessIProviderCasing_ReturnsSameTitle()
    {
        Assert.Equal(
            AcademicWorkTitleNormalizer.Normalize("IŞIK"),
            AcademicWorkTitleNormalizer.Normalize("ışık"));
    }

    [Fact]
    public void Normalize_ComposedDecomposedAndFullwidthForms_ReturnsSameTitle()
    {
        Assert.Equal(
            AcademicWorkTitleNormalizer.Normalize("Café Ａ"),
            AcademicWorkTitleNormalizer.Normalize("Cafe\u0301 A"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("<strong></strong>")]
    [InlineData("&lt;em&gt;&lt;/em&gt;")]
    public void Normalize_EmptyOrMarkupOnlyTitle_ReturnsNull(string? title)
    {
        Assert.Null(AcademicWorkTitleNormalizer.Normalize(title));
    }
}
