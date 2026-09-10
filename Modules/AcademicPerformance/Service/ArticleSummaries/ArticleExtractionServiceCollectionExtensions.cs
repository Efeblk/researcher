using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;

public static class ArticleExtractionServiceCollectionExtensions
{
    public static IServiceCollection AddArticleExtraction(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ArticleSummaryOptions>().Bind(configuration.GetSection("ArticleSummary")).ValidateDataAnnotations();
        services.AddScoped<SafeArticleFetcher>();
        services.AddScoped<ArticleHtmlExtractor>();
        services.AddScoped<ArticlePdfExtractor>();
        services.AddSingleton<IArticlePageRenderer, PdfToImageArticlePageRenderer>();
        services.AddSingleton<IArticleOcrEngine, TesseractCliArticleOcrEngine>();
        return services;
    }
}
