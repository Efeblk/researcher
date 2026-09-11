using System.Text;
using System.Text.RegularExpressions;
using AcademicCollector.Analysis.Contracts;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Options;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;

public sealed class ArticleHtmlExtractor(IOptions<ArticleSummaryOptions> options)
{
    public const string Version = "html-semantic-body-v1";
    private static readonly string[] BodySelectors = [
        "article .jats-body", ".jats-body", "section.body.main-article-body", "article .article-body", ".article-body",
        "[itemprop='articleBody']", "article[data-article-body]", "#article-body"
    ];
    private static readonly string[] RemoveSelectors = [
        "script", "style", "noscript", "nav", "header", "footer", "aside", "form",
        ".references", "#references", "[role='navigation']", ".related", ".related-articles",
        ".recommended", ".social-share", ".authentication", ".login"
    ];

    public SummarizeArticleRequest Extract(byte[] html, Uri source, string language)
    {
        IDocument document = Parse(html);
        IElement? body = BodySelectors.Select(document.QuerySelector).FirstOrDefault(x => x is not null);
        if (body is null) throw new ArticleSourceException("The HTML page does not expose a supported full article body.");
        foreach (IElement element in body.QuerySelectorAll(string.Join(',', RemoveSelectors))) element.Remove();
        foreach (IElement heading in body.QuerySelectorAll("h1,h2,h3,h4,h5,h6").Where(x => IsExcludedHeading(x.TextContent)).ToArray())
            RemoveExcludedSection(body, heading);
        string text = Normalize(body.TextContent);
        int paragraphs = body.QuerySelectorAll("p").Count(x => Normalize(x.TextContent).Length >= 80);
        int headings = body.QuerySelectorAll("h2,h3,h4").Length;
        if (text.Length < 1500 || paragraphs < 3 || headings < 1)
            throw new ArticleSourceException("The HTML page does not contain a credible full article body.");
        bool partial = text.Length > options.Value.MaximumHtmlCharacters;
        if (partial) text = text[..options.Value.MaximumHtmlCharacters];
        IReadOnlyList<ArticlePage> pages = [new(null, text)];
        return new(language, "html", "", Version, pages, 1, partial,
            partial ? $"HTML article text was truncated at {options.Value.MaximumHtmlCharacters} characters." : null)
            { SourceSpans = ArticleSourceCatalog.Create(pages) };
    }

    public string? TryExtractAbstract(byte[] html, Uri source)
    {
        IDocument document = Parse(html);
        string? value = document.QuerySelector("meta[name='citation_abstract']")?.GetAttribute("content");
        value ??= document.QuerySelector("[itemprop='abstract'],.abstract,#abstract")?.TextContent;
        string normalized = Normalize(value ?? "");
        return normalized.Length is >= 100 and <= 10000 ? normalized : null;
    }

    private static IDocument Parse(byte[] html) => new HtmlParser().ParseDocument(Encoding.UTF8.GetString(html));
    private static void RemoveExcludedSection(IElement body, IElement heading)
    {
        IElement? section = heading.Closest("section,.section");
        if (section is not null && section != body) { section.Remove(); return; }
        int level = int.Parse(heading.TagName[1..]);
        IElement? current = heading;
        while (current is not null)
        {
            IElement? next = current.NextElementSibling;
            current.Remove();
            if (next is not null && Regex.IsMatch(next.TagName, "^H[1-6]$") && int.Parse(next.TagName[1..]) <= level) break;
            current = next;
        }
    }
    private static bool IsExcludedHeading(string value) => Regex.IsMatch(value.Trim(), "^(references|bibliography|related (articles|papers)|recommended|acknowledg(e)?ments?)$", RegexOptions.IgnoreCase);
    private static string Normalize(string value) => Regex.Replace(value, "\\s+", " ").Trim();
}
