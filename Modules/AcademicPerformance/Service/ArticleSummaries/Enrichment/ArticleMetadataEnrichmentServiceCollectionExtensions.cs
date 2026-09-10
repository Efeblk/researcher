using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Crossref;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries.Enrichment;

public static class ArticleMetadataEnrichmentServiceCollectionExtensions
{
    internal const string UnpaywallHttpClient = "ArticleMetadataEnrichment.Unpaywall";

    public static IServiceCollection AddArticleMetadataEnrichment(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.AddOptions<ArticleMetadataEnrichmentOptions>()
            .Bind(configuration.GetSection("ArticleMetadataEnrichment"))
            .ValidateDataAnnotations();
        services.AddHttpClient(UnpaywallHttpClient, client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AcademicCollectorDemo/0.1");
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false
        });
        services.TryAddTransient<OpenAlexClient>();
        services.TryAddTransient<CrossrefClient>();
        services.TryAddScoped<ArticleMetadataEnricher>();
        return services;
    }
}
