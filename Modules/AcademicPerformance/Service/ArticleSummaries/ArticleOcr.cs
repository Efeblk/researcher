using System.Diagnostics;
using PDFtoImage;
using UglyToad.PdfPig;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;

public interface IArticlePageRenderer
{
    Task RenderAsync(byte[] pdf, int zeroBasedPage, string outputPath, int dpi, CancellationToken cancellationToken);
}

public interface IArticleOcrEngine
{
    Task<string> RecognizeAsync(string imagePath, string language, TimeSpan timeout, CancellationToken cancellationToken);
}

public sealed class PdfToImageArticlePageRenderer(Microsoft.Extensions.Options.IOptions<ArticleSummaryOptions> options) : IArticlePageRenderer
{
    public async Task RenderAsync(byte[] pdf, int zeroBasedPage, string outputPath, int dpi, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using (PdfDocument document = PdfDocument.Open(pdf))
        {
            var page = document.GetPage(zeroBasedPage + 1);
            long width = checked((long)Math.Ceiling(page.Width * dpi / 72d));
            long height = checked((long)Math.Ceiling(page.Height * dpi / 72d));
            if (width <= 0 || height <= 0 || width > options.Value.MaximumOcrDimensionPixels ||
                height > options.Value.MaximumOcrDimensionPixels || checked(width * height) > options.Value.MaximumOcrPixels)
                throw new ArticleSourceException("The PDF page dimensions exceed the configured OCR rendering limit.");
        }
        await using MemoryStream stream = new(pdf, writable: false);
#pragma warning disable CA1416 // PDFtoImage supports every runtime targeted by this server application.
        await Task.Run(() => Conversion.SavePng(outputPath, stream, zeroBasedPage, leaveOpen: true,
            options: new RenderOptions(Dpi: dpi, Grayscale: true)), cancellationToken);
#pragma warning restore CA1416
    }
}

public sealed class TesseractCliArticleOcrEngine(Microsoft.Extensions.Options.IOptions<ArticleSummaryOptions> options) : IArticleOcrEngine
{
    public async Task<string> RecognizeAsync(string imagePath, string language, TimeSpan timeout, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProcessStartInfo start = new() { FileName = options.Value.TesseractPath, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(imagePath); start.ArgumentList.Add("stdout"); start.ArgumentList.Add("-l"); start.ArgumentList.Add(language);
        using Process process = new() { StartInfo = start };
        try { if (!process.Start()) throw new ArticleSourceException("OCR is unavailable: Tesseract could not be started."); }
        catch (Exception exception) when (exception is not ArticleSourceException) { throw new ArticleSourceException("OCR is unavailable: Tesseract could not be started."); }
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(deadline.Token);
            Task<string> stderr = process.StandardError.ReadToEndAsync(deadline.Token);
            await process.WaitForExitAsync(deadline.Token);
            string text = await stdout; string error = await stderr;
            if (process.ExitCode != 0) throw new ArticleSourceException(error.Contains("data", StringComparison.OrdinalIgnoreCase)
                ? "OCR is unavailable: the requested Tesseract language data is missing." : "OCR failed for a rendered PDF page.");
            return text.Trim();
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            cancellationToken.ThrowIfCancellationRequested();
            throw new ArticleSourceException("OCR timed out for a PDF page.");
        }
    }
}
