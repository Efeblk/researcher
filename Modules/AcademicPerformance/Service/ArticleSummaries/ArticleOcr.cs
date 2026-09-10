using System.Diagnostics;
using System.Runtime.Versioning;
using PDFtoImage;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;

public interface IArticlePageRenderer
{
    Task RenderAsync(byte[] pdf, int zeroBasedPage, string outputPath, int dpi, CancellationToken cancellationToken);
}

public interface IArticleOcrEngine
{
    Task<string> RecognizeAsync(string imagePath, string language, TimeSpan timeout, CancellationToken cancellationToken);
}

public sealed class PdfToImageArticlePageRenderer : IArticlePageRenderer
{
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    public async Task RenderAsync(byte[] pdf, int zeroBasedPage, string outputPath, int dpi, CancellationToken cancellationToken)
    {
        await using MemoryStream stream = new(pdf, writable: false);
        await Task.Run(() => Conversion.SavePng(outputPath, stream, zeroBasedPage, leaveOpen: true,
            options: new RenderOptions(Dpi: dpi, Grayscale: true)), cancellationToken);
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
