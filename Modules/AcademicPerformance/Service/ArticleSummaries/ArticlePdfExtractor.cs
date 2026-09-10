using AcademicCollector.Analysis.Contracts;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;

public sealed class ArticlePdfExtractor
{
    public const string Version = "pdfpig-layout-spans-v2";
    public const string OcrVersion = "pdfpig-layout-tesseract-ocr-v1";
    private readonly IArticleOcrEngine? _ocr;
    private readonly IArticlePageRenderer? _renderer;
    private readonly IOptions<ArticleSummaryOptions> options;

    public ArticlePdfExtractor(IOptions<ArticleSummaryOptions> options) : this(options, null, null) { }
    public ArticlePdfExtractor(IOptions<ArticleSummaryOptions> options, IArticlePageRenderer? renderer, IArticleOcrEngine? ocr)
    { this.options = options; _renderer = renderer; _ocr = ocr; }

    public SummarizeArticleRequest Extract(byte[] bytes, string language)
    {
        NativeExtraction native = ExtractNative(bytes);
        if (native.Pages.Count == 0)
            throw new ArticleSourceException("The PDF has no extractable text; OCR is required. Use ExtractAsync to enable OCR fallback.");
        return CreateRequest(language, native.Pages, native.TotalPages, false, []);
    }

    public async Task<SummarizeArticleRequest> ExtractAsync(byte[] bytes, string language, CancellationToken cancellationToken)
    {
        NativeExtraction native = ExtractNative(bytes);
        List<ArticlePage> pages = [.. native.Pages];
        int characters = pages.Sum(x => x.Text.Length);
        List<string> limitations = [];
        bool usedOcr = false;
        int[] missing = Enumerable.Range(1, native.TotalPages).Except(pages.Select(x => x.PageNumber!.Value)).ToArray();
        if (missing.Length > 0 && options.Value.OcrEnabled)
        {
            if (_renderer is null || _ocr is null) limitations.Add("OCR is unavailable because its renderer or Tesseract engine is not configured.");
            else
            {
                string directory = Path.Combine(Path.GetTempPath(), "article-ocr-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                using CancellationTokenSource total = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                total.CancelAfter(TimeSpan.FromSeconds(options.Value.OcrTotalTimeoutSeconds));
                try
                {
                    foreach (int pageNumber in missing.Take(options.Value.MaximumOcrPages))
                    {
                        string image = Path.Combine(directory, $"page-{pageNumber}.png");
                        try
                        {
                            await _renderer.RenderAsync(bytes, pageNumber - 1, image, options.Value.OcrDpi, total.Token);
                            if (new FileInfo(image).Length > options.Value.MaximumOcrImageBytes) throw new ArticleSourceException("The rendered OCR page exceeds the configured size limit.");
                            string text = await _ocr.RecognizeAsync(image, MapLanguage(language), TimeSpan.FromSeconds(options.Value.OcrPageTimeoutSeconds), total.Token);
                            if (text.Length >= 30)
                            {
                                characters += text.Length;
                                if (characters > options.Value.MaximumExtractedCharacters)
                                    throw new ArticleSourceException("Extracted PDF and OCR text exceeds the configured limit.");
                                pages.Add(new(pageNumber, text)); usedOcr = true;
                            }
                            else limitations.Add($"OCR produced insufficient text for page {pageNumber}.");
                        }
                        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                        { limitations.Add($"OCR reached its total time limit at page {pageNumber}."); break; }
                        catch (OperationCanceledException) { throw; }
                        catch (ArticleSourceException exception) { limitations.Add($"Page {pageNumber}: {exception.Message}"); }
                        catch (Exception) { limitations.Add($"Page {pageNumber}: OCR is unavailable because the page could not be rendered or read."); }
                    }
                    if (missing.Length > options.Value.MaximumOcrPages) limitations.Add($"OCR was limited to {options.Value.MaximumOcrPages} pages.");
                }
                finally { try { Directory.Delete(directory, recursive: true); } catch { } }
            }
        }
        else if (missing.Length > 0) limitations.Add("OCR is disabled.");
        cancellationToken.ThrowIfCancellationRequested();
        pages.Sort((left, right) => left.PageNumber!.Value.CompareTo(right.PageNumber!.Value));
        if (pages.Count == 0)
            throw new ArticleSourceException(limitations.FirstOrDefault() ?? "The PDF has no readable text after OCR.");
        return CreateRequest(language, pages, native.TotalPages, usedOcr, limitations);
    }

    private NativeExtraction ExtractNative(byte[] bytes)
    {
        try
        {
            using PdfDocument document = PdfDocument.Open(bytes);
            int totalPages = document.NumberOfPages;
            if (totalPages > options.Value.MaximumPages)
                throw new ArticleSourceException($"PDF has {totalPages} pages; the configured limit is {options.Value.MaximumPages}.");
            List<ArticlePage> pages = [];
            int characters = 0;
            foreach (var page in document.GetPages())
            {
                var words = NearestNeighbourWordExtractor.Instance.GetWords(page.Letters);
                var blocks = DocstrumBoundingBoxes.Instance.GetBlocks(words);
                var ordered = UnsupervisedReadingOrderDetector.Instance.Get(blocks);
                string text = string.Join("\n\n", ordered.Select(block =>
                    string.Join("\n", block.TextLines.Select(line => string.Join(" ", line.Words.Select(word => word.Text)))))).Trim();
                if (text.Length == 0)
                    continue;
                characters += text.Length;
                if (characters > options.Value.MaximumExtractedCharacters)
                    throw new ArticleSourceException("Extracted PDF text exceeds the configured limit.");
                pages.Add(new(page.Number, text));
            }
            return new(pages, totalPages);
        }
        catch (ArticleSourceException) { throw; }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ArticleSourceException("The PDF is encrypted, malformed, or unsupported.");
        }
    }

    private static string MapLanguage(string language) => language.StartsWith("tr", StringComparison.OrdinalIgnoreCase) ? "tur" :
        language.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "eng" : language.Split('-', '_')[0];

    private static SummarizeArticleRequest CreateRequest(string language, List<ArticlePage> pages, int totalPages, bool usedOcr, List<string> limitations)
    {
        int unread = totalPages - pages.Count;
        if (unread > 0) limitations.Insert(0, $"{unread} of {totalPages} pages remain unread.");
        if (usedOcr) limitations.Insert(0, "OCR text may contain recognition and reading-order errors.");
        bool partial = unread > 0 || limitations.Count > 0;
        return new(language, "pdf", "", usedOcr ? OcrVersion : Version, pages, totalPages, partial,
            limitations.Count == 0 ? null : string.Join(" ", limitations)) { SourceSpans = ArticleSourceCatalog.Create(pages) };
    }

    private sealed record NativeExtraction(List<ArticlePage> Pages, int TotalPages);
}
