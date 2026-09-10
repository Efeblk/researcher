using AcademicCollector.Analysis.Contracts;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;

public sealed class ArticlePdfExtractor(IOptions<ArticleSummaryOptions> options)
{
    public const string Version = "pdfpig-layout-spans-v2";

    public SummarizeArticleRequest Extract(byte[] bytes, string language)
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
            if (pages.Count == 0)
                throw new ArticleSourceException("The PDF has no extractable text; scanned PDFs require OCR, which is not supported.");
            bool partial = pages.Count != totalPages;
            string? reason = partial ? $"{totalPages - pages.Count} of {totalPages} pages had no extractable text and were not summarized." : null;
            return new(language, "pdf", "", Version, pages, totalPages, partial, reason)
                { SourceSpans = ArticleSourceCatalog.Create(pages) };
        }
        catch (ArticleSourceException) { throw; }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ArticleSourceException("The PDF is encrypted, malformed, or unsupported.");
        }
    }
}
