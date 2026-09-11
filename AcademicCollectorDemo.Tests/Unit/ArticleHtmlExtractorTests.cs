using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;
using Microsoft.Extensions.Options;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class ArticleHtmlExtractorTests
{
    [Fact]
    public void Extract_SemanticArticleBody_ReturnsCleanSingleHtmlPage()
    {
        string paragraphs = string.Join("", Enumerable.Range(1, 12).Select(i => $"<p>Section paragraph {i} describes the study methods, observations, evidence, analysis, limitations, and reproducible findings in sufficient detail for readers and independent evaluation.</p>"));
        byte[] html = System.Text.Encoding.UTF8.GetBytes($"<article><div itemprop='articleBody'><h2>Methods</h2>{paragraphs}<section class='references'><h2>References</h2><p>Related citation menu content that must be removed completely.</p></section></div></article>");

        var result = Create().Extract(html, new Uri("https://example.org/article"), "en");

        Assert.Equal("html", result.SourceKind);
        Assert.Single(result.Pages);
        Assert.Null(result.Pages[0].PageNumber);
        Assert.DoesNotContain("Related citation", result.Pages[0].Text);
        Assert.Equal(1, result.TotalSourcePages);
    }

    [Theory]
    [InlineData("<main><h2>Abstract</h2><p>This is only an abstract shown outside a supported article body.</p></main>")]
    [InlineData("<div itemprop='articleBody'><h2>Abstract</h2><p>Short abstract only.</p><form>Buy access</form></div>")]
    public void Extract_AbstractOrGenericPage_Rejects(string value) => Assert.Throws<ArticleSourceException>(() =>
        Create().Extract(System.Text.Encoding.UTF8.GetBytes(value), new Uri("https://example.org/article"), "en"));

    [Fact]
    public void TryExtractAbstract_Metadata_ReturnsAbstract()
    {
        string abstractText = new('a', 120);
        string? result = Create().TryExtractAbstract(System.Text.Encoding.UTF8.GetBytes($"<meta name='citation_abstract' content='{abstractText}'>"), new Uri("https://example.org"));
        Assert.Equal(abstractText, result);
    }

    [Fact]
    public void TryExtractAbstract_GenericSeoDescription_Rejects()
    {
        string description = new('a', 120);
        string? result = Create().TryExtractAbstract(System.Text.Encoding.UTF8.GetBytes($"<meta name='description' content='{description}'><meta property='og:description' content='{description}'>"), new Uri("https://example.org"));
        Assert.Null(result);
    }

    private static ArticleHtmlExtractor Create() => new(Options.Create(new ArticleSummaryOptions()));
}
