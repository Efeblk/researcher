using Microsoft.Extensions.Configuration;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Works.Files;

public sealed class PdfDownloadOptions
{
    public const string SectionName = "PdfDownload";
    public const string HttpClientName = "AcademicPdfDownloads";

    public bool Enabled { get; set; }
    public int MaxFileSizeMb { get; set; }
    public int RequestTimeoutSeconds { get; set; }
    public int MaxRedirects { get; set; }
    public string? StorageRoot { get; set; } = null;

    public PdfDownloadOptions()
    {
        Enabled = false;
        MaxFileSizeMb = 50;
        RequestTimeoutSeconds = 60;
        MaxRedirects = 5;
        StorageRoot = "Storage";
    }

    public static PdfDownloadOptions Create(IConfiguration configuration)
    {
        PdfDownloadOptions? options = null;

        options = new PdfDownloadOptions();
        configuration.GetSection(SectionName).Bind(options);

        if (options.MaxFileSizeMb <= 0)
        {
            options.MaxFileSizeMb = 50;
        }

        if (options.RequestTimeoutSeconds <= 0)
        {
            options.RequestTimeoutSeconds = 60;
        }

        if (options.MaxRedirects < 0)
        {
            options.MaxRedirects = 5;
        }

        if (string.IsNullOrWhiteSpace(options.StorageRoot))
        {
            options.StorageRoot = "Storage";
        }

        return options;
    }
}
