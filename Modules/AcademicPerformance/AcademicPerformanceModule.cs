using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.GoogleScholar;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Files;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AcademicCollectorDemo.Modules.AcademicPerformance;

public static class AcademicPerformanceModule
{
    public static IServiceCollection AddAcademicPerformanceModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton(configuration);
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(CreateHttpClient());
        services.AddHttpClient(PdfDownloadOptions.HttpClientName, httpClient =>
        {
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "AcademicCollectorDemo/0.1");
            httpClient.Timeout = Timeout.InfiniteTimeSpan;
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false
        });

        services.AddSingleton<ResearcherIdentifierParser>();
        services.AddSingleton<AcademicWorkCategorizer>();
        services.AddSingleton<ResearcherCollectionFeedback>();
        services.AddSingleton<PdfSourceExtractor>();
        services.AddTransient<OpenAlexClient>();
        services.AddTransient<GoogleScholarClient>();
        services.AddTransient<WebOfScienceClient>();

        services.AddScoped(CreateDbContext);
        services.AddScoped<AcademicDatabaseInitializer>();
        services.AddScoped<ResearcherRepository>();
        services.AddScoped<AcademicWorkSynchronizer>();
        services.AddScoped<AcademicPdfDownloader>();
        services.AddScoped<ResearcherCollectionService>();
        services.AddScoped<ResearcherCollectionHandler>();
        services.AddScoped<DatabaseMaintenance>();
        services.AddSingleton<ResearcherSummaryFactory>();

        return services;
    }

    private static HttpClient CreateHttpClient()
    {
        HttpClient? httpClient = null;

        httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AcademicCollectorDemo/0.1");

        return httpClient;
    }

    private static AcademicDbContext CreateDbContext(IServiceProvider serviceProvider)
    {
        IConfiguration? configuration = null;
        DbContextOptionsBuilder<AcademicDbContext>? optionsBuilder = null;

        configuration = serviceProvider.GetRequiredService<IConfiguration>();
        optionsBuilder = new DbContextOptionsBuilder<AcademicDbContext>();
        DatabaseConfiguration.Configure(optionsBuilder, configuration);

        return new AcademicDbContext(optionsBuilder.Options);
    }
}
