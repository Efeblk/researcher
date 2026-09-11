using System.ComponentModel.DataAnnotations;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;

public sealed class ArticleSummaryOptions
{
    [Range(1, 10)] public int MaximumRedirects { get; set; } = 4;
    [Range(2, 32)] public int MaximumSourceRequests { get; set; } = 8;
    [Range(1, 2)] public int MaximumLandingDepth { get; set; } = 1;
    [Range(65536, 52428800)] public int MaximumDownloadBytes { get; set; } = 12 * 1024 * 1024;
    [Range(1, 200)] public int MaximumPages { get; set; } = 40;
    [Range(10000, 1000000)] public int MaximumExtractedCharacters { get; set; } = 160000;
    [Range(5, 120)] public int FetchTimeoutSeconds { get; set; } = 30;
    [Range(30, 1800)] public int TotalTimeoutSeconds { get; set; } = 600;
    public bool OcrEnabled { get; set; } = true;
    [Range(1, 200)] public int MaximumOcrPages { get; set; } = 20;
    [Range(72, 400)] public int OcrDpi { get; set; } = 200;
    [Range(5, 300)] public int OcrPageTimeoutSeconds { get; set; } = 45;
    [Range(1, 1200)] public int OcrTotalTimeoutSeconds { get; set; } = 180;
    [Range(1024, 52428800)] public int MaximumOcrImageBytes { get; set; } = 12 * 1024 * 1024;
    [Range(1000000, 100000000)] public int MaximumOcrPixels { get; set; } = 25000000;
    [Range(1000, 20000)] public int MaximumOcrDimensionPixels { get; set; } = 10000;
    [Range(1000, 1000000)] public int MaximumHtmlCharacters { get; set; } = 160000;
    public string TesseractPath { get; set; } = "tesseract";
}
