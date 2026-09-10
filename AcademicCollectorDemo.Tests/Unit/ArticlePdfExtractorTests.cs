using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;
using AcademicCollector.Analysis.Contracts;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class ArticlePdfExtractorTests
{
    [Fact]
    public void Extract_TextAndBlankPage_ReturnsPageEvidenceAndPartialCoverage()
    {
        PdfDocumentBuilder builder = new();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        builder.AddPage(PageSize.A4).AddText("Synthetic purpose and finding for extraction.", 12, new PdfPoint(40, 700), font);
        builder.AddPage(PageSize.A4);
        byte[] pdf = builder.Build();
        ArticlePdfExtractor extractor = new(Options.Create(new ArticleSummaryOptions()));

        var result = extractor.Extract(pdf, "tr");

        Assert.Single(result.Pages);
        Assert.Equal(1, result.Pages[0].PageNumber);
        Assert.Contains("Synthetic purpose", result.Pages[0].Text);
        Assert.Equal(2, result.TotalSourcePages);
        Assert.True(result.IsPartial);
        Assert.Contains("1 of 2", result.ScopeReason);
    }

    [Fact]
    public void Extract_NoText_ReportsOcrUnsupported()
    {
        PdfDocumentBuilder builder = new();
        builder.AddPage(PageSize.A4);
        ArticlePdfExtractor extractor = new(Options.Create(new ArticleSummaryOptions()));

        ArticleSourceException exception = Assert.Throws<ArticleSourceException>(() => extractor.Extract(builder.Build(), "en"));

        Assert.Contains("OCR", exception.Message);
    }

    [Fact]
    public async Task ExtractAsync_MixedPdf_InsertsOcrAtMissingPageInOrder()
    {
        PdfDocumentBuilder builder = new();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        builder.AddPage(PageSize.A4).AddText("Native first page has meaningful text.", 12, new PdfPoint(40, 700), font);
        builder.AddPage(PageSize.A4);
        builder.AddPage(PageSize.A4).AddText("Native third page has meaningful text.", 12, new PdfPoint(40, 700), font);
        ArticlePdfExtractor extractor = new(Options.Create(new ArticleSummaryOptions()), new FakeRenderer(), new FakeOcr("OCR second page contains enough synthetic recognized text for acceptance."));

        SummarizeArticleRequest result = await extractor.ExtractAsync(builder.Build(), "en", default);

        Assert.Equal([1, 2, 3], result.Pages.Select(x => x.PageNumber));
        Assert.Contains("ocr", result.ExtractionVersion, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("recognition", result.ScopeReason, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.IsPartial);
    }

    [Fact]
    public async Task ExtractAsync_TextlessPdfWithoutOcrDependency_ReportsUnavailable()
    {
        PdfDocumentBuilder builder = new(); builder.AddPage(PageSize.A4);
        ArticlePdfExtractor extractor = new(Options.Create(new ArticleSummaryOptions()));

        ArticleSourceException exception = await Assert.ThrowsAsync<ArticleSourceException>(() => extractor.ExtractAsync(builder.Build(), "en", default));

        Assert.Contains("unavailable", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExtractAsync_OcrEmpty_ReportsInsufficientText()
    {
        PdfDocumentBuilder builder = new(); builder.AddPage(PageSize.A4);
        ArticlePdfExtractor extractor = new(Options.Create(new ArticleSummaryOptions()), new FakeRenderer(), new FakeOcr(""));

        ArticleSourceException exception = await Assert.ThrowsAsync<ArticleSourceException>(() => extractor.ExtractAsync(builder.Build(), "en", default));

        Assert.Contains("insufficient", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExtractAsync_Cancelled_PropagatesCancellation()
    {
        PdfDocumentBuilder builder = new(); builder.AddPage(PageSize.A4);
        using CancellationTokenSource cancellation = new(); cancellation.Cancel();
        ArticlePdfExtractor extractor = new(Options.Create(new ArticleSummaryOptions()), new FakeRenderer(), new FakeOcr("unused"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => extractor.ExtractAsync(builder.Build(), "en", cancellation.Token));
    }

    private sealed class FakeRenderer : IArticlePageRenderer
    {
        public Task RenderAsync(byte[] pdf, int zeroBasedPage, string outputPath, int dpi, CancellationToken cancellationToken)
        { File.WriteAllBytes(outputPath, [1, 2, 3]); return Task.CompletedTask; }
    }
    private sealed class FakeOcr(string text) : IArticleOcrEngine
    {
        public Task<string> RecognizeAsync(string imagePath, string language, TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult(text);
    }

    [Fact]
    public void Extract_TwoColumns_PreservesWordsAndLayoutOrderedBlocks()
    {
        PdfDocumentBuilder builder = new();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.A4);
        page.AddText("Left column first sentence.", 12, new PdfPoint(40, 720), font);
        page.AddText("Left column second sentence.", 12, new PdfPoint(40, 690), font);
        page.AddText("Right column first sentence.", 12, new PdfPoint(330, 720), font);
        page.AddText("Right column second sentence.", 12, new PdfPoint(330, 690), font);

        SummarizeArticleRequest result = new ArticlePdfExtractor(
            Options.Create(new ArticleSummaryOptions())).Extract(builder.Build(), "en");

        string text = result.Pages.Single().Text;
        Assert.Contains("Left column first sentence.", text);
        Assert.Contains("Right column first sentence.", text);
        Assert.DoesNotContain("Leftcolumn", text);
        Assert.Equal(text, string.Concat(result.SourceSpans!.Select(x => x.Text)));
        Assert.True(ArticleSourceCatalog.IsValid(result.Pages, result.SourceSpans, "pdf"));
    }
}
