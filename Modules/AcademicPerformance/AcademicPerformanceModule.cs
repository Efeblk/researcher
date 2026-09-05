using AcademicCollectorDemo.Modules.AcademicPerformance.Background;
using AcademicCollectorDemo.Modules.AcademicPerformance.Bulk;
using AcademicCollectorDemo.Modules.AcademicPerformance.Bulk.SqlImport;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.RateLimiting;
using AcademicCollectorDemo.Modules.AcademicPerformance.Application;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Orcid;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.GoogleScholar;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis.Collection;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis.Persistence;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Collection;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Persistence;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AcademicCollectorDemo.Modules.AcademicPerformance;

public static class AcademicPerformanceModule
{
    public static IServiceCollection AddAcademicPerformanceModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton(_ => CreateHttpClient(configuration));
        services.AddOptions<BulkCollectionOptions>().Bind(configuration.GetSection("BulkCollection"))
            .Validate(value => value.MaximumBatchSize is >= 1 and <= 10000 &&
                value.MaximumAttempts is >= 1 and <= 10 && value.PollSeconds is >= 1 and <= 60 &&
                value.RetrySeconds is >= 1 and <= 3600, "Invalid bulk collection limits.");
        services.AddOptions<BulkSqlSourceOptions>().Bind(configuration.GetSection("BulkSqlSource"))
            .Validate(value => value.CommandTimeoutSeconds is >= 1 and <= 300, "Invalid SQL query timeout.");
        services.AddScoped<BulkCollectionService>();
        services.AddScoped<BulkSqlImporter>();
        services.AddScoped<BulkJobProcessor>();
        services.AddHostedService<BulkCollectionWorker>();
        services.AddAcademicDatabase(configuration);

        services.AddSingleton<ResearcherIdentifierParser>();
        services.AddSingleton<AcademicWorkCategorizer>();
        services.AddSingleton<ResearcherCollectionFeedback>();
        services.AddTransient<OrcidClient>();
        services.AddTransient<GoogleScholarClient>();
        services.AddTransient<OpenAlexClient>();
        services.AddTransient<WebOfScienceClient>();
        services.AddTransient<YoksisClient>();
        services.AddTransient<YoksisCollectionService>();
        services.AddScoped<YoksisRecordSynchronizer>();
        services.AddScoped<YoksisAcademicWorkSynchronizer>();
        services.AddScoped<YoksisCollectionHandler>();

        services.AddScoped<ResearcherRepository>();
        services.AddScoped<AcademicWorkSynchronizer>();
        services.AddScoped<PublicationSummarySynchronizer>();
        services.AddScoped<ResearcherCollectionService>();
        services.AddScoped<ResearcherCollectionHandler>();
        services.AddScoped<IAcademicPerformanceApplicationService,
            AcademicPerformanceApplicationService>();
        return services;
    }

    private static HttpClient CreateHttpClient(IConfiguration configuration)
    {
        List<ProviderRequestPolicy> policies = [];
        foreach (var (name, key, defaultUrl) in new[]
        {
            ("Orcid", "Orcid:ApiBaseUrl", "https://pub.orcid.org/v3.0"),
            ("SearchApi", "SearchApi:ApiBaseUrl", "https://www.searchapi.io/api/v1/search"),
            ("OpenAlex", "OpenAlex:ApiBaseUrl", "https://api.openalex.org"),
            ("WebOfScience", "WebOfScience:ApiBaseUrl", "https://api.clarivate.com/apis/wos-starter/v1"),
            ("Yoksis", "Yoksis:ServiceUrl", "https://servisler.yok.gov.tr/ws/OzgecmisV2")
        })
        {
            int interval = configuration.GetValue($"ProviderRequestLimits:{name}:MinimumIntervalMilliseconds", 1000);
            int dailyLimit = configuration.GetValue($"ProviderRequestLimits:{name}:DailyRequestLimit", 0);
            if (interval is < 1 or > 60000 || dailyLimit < 0)
                throw new InvalidOperationException($"Invalid request limits for {name}.");
            policies.Add(new()
            {
                Name = name,
                Host = new Uri(configuration[key] ?? defaultUrl).Host,
                MinimumIntervalMilliseconds = interval,
                DailyRequestLimit = dailyLimit
            });
        }
        ProviderRateLimitHandler handler = new(
            configuration.GetConnectionString("AcademicDatabase")!, policies)
        {
            InnerHandler = new HttpClientHandler { AllowAutoRedirect = false }
        };
        HttpClient httpClient = new(handler);
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AcademicCollectorDemo/0.1");

        return httpClient;
    }
}
