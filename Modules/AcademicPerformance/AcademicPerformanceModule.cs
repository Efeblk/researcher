using AcademicCollectorDemo.Modules.AcademicPerformance.Application;
using AcademicCollectorDemo.Modules.AcademicPerformance.Background;
using AcademicCollectorDemo.Modules.AcademicPerformance.Bulk;
using AcademicCollectorDemo.Modules.AcademicPerformance.Bulk.SqlImport;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Crossref;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.GoogleScholar;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Orcid;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.RateLimiting;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.SemanticScholar;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.TrDizin;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Scopus;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis.Collection;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis.Persistence;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Collection;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Persistence;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Metrics;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Persistence;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Enrichment;

namespace AcademicCollectorDemo.Modules.AcademicPerformance;

public static class AcademicPerformanceModule
{
    public static IServiceCollection AddAcademicPerformanceModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton(provider => CreateHttpClient(configuration,
            provider.GetRequiredService<ILogger<ProviderRateLimitHandler>>()));
        services.AddArticleMetadataEnrichment(configuration);
        services.AddSingleton<Integrations.Status.ProviderStatusService>();
        services.AddHttpClient("ProviderStatus", client => client.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddOptions<BulkCollectionOptions>().Bind(configuration.GetSection("BulkCollection"))
            .Validate(value => value.MaximumBatchSize is >= 1 and <= 10000 &&
                value.MaximumAttempts is >= 1 and <= 10 && value.PollSeconds is >= 1 and <= 60 &&
                value.RetrySeconds is >= 1 and <= 3600, "Invalid bulk collection limits.");
        services.AddOptions<BulkSqlSourceOptions>().Bind(configuration.GetSection("BulkSqlSource"))
            .Validate(value => value.CommandTimeoutSeconds is >= 1 and <= 300, "Invalid SQL query timeout.");
        services.AddScoped<BulkCollectionService>();
        services.AddSingleton<BulkResearcherInputNormalizer>();
        services.AddScoped<BulkSqlImporter>();
        services.AddScoped<BulkJobProcessor>();
        services.AddHostedService<BulkCollectionWorker>();
        services.AddAcademicDatabase(configuration);

        services.AddSingleton<ResearcherIdentifierParser>();
        services.AddSingleton<ResearcherProviderInputNormalizer>();
        services.AddSingleton<AcademicWorkCategorizer>();
        services.AddSingleton<ResearcherCollectionFeedback>();
        services.AddTransient<OrcidClient>();
        services.AddTransient<GoogleScholarClient>();
        services.AddTransient<OpenAlexClient>();
        services.AddTransient<ScopusClient>();
        services.AddTransient<WebOfScienceClient>();
        services.AddTransient<YoksisClient>();
        services.AddTransient<TrDizinClient>();
        services.AddTransient<CrossrefClient>();
        services.AddScoped<CrossrefEnrichmentService>();
        services.AddOptions<SemanticScholarOptions>().Bind(configuration.GetSection("SemanticScholar"))
            .ValidateDataAnnotations();
        services.AddTransient<SemanticScholarClient>();
        services.AddScoped<SemanticScholarEnrichmentService>();
        services.AddScoped<SemanticScholarWorkSourceSynchronizer>();
        services.AddTransient<YoksisCollectionService>();
        services.AddScoped<YoksisRecordSynchronizer>();
        services.AddScoped<YoksisAcademicWorkSynchronizer>();
        services.AddScoped<YoksisCollectionHandler>();

        services.AddScoped<ResearcherRepository>();
        services.AddScoped<AcademicWorkSynchronizer>();
        services.AddScoped<AcademicWorkResearchContextSynchronizer>();
        services.AddScoped<CanonicalWorkSynchronizer>();
        services.AddScoped<CanonicalWorkQueryService>();
        services.AddScoped<PublicationSummarySynchronizer>();
        services.AddScoped<ResearcherCollectionService>();
        services.AddScoped<ResearcherCollectionHandler>();
        services.AddScoped<ResearcherMetricsService>();
        services.AddScoped<IAcademicPerformanceApplicationService, AcademicPerformanceApplicationService>();
        return services;
    }

    private static HttpClient CreateHttpClient(IConfiguration configuration,
        ILogger<ProviderRateLimitHandler> logger)
    {
        List<ProviderRequestPolicy> policies = [];
        foreach (var (name, key, defaultUrl) in new[]
        {
            ("Orcid", "Orcid:ApiBaseUrl", "https://pub.orcid.org/v3.0"),
            ("SearchApi", "SearchApi:ApiBaseUrl", "https://www.searchapi.io/api/v1/search"),
            ("OpenAlex", "OpenAlex:ApiBaseUrl", "https://api.openalex.org"),
            ("Scopus", "Scopus:ApiBaseUrl", "https://api.elsevier.com/content/"),
            ("WebOfScience", "WebOfScience:ApiBaseUrl", "https://api.clarivate.com/apis/wos-starter/v1"),
            ("Yoksis", "Yoksis:ServiceUrl", "https://servisler.yok.gov.tr/ws/OzgecmisV2"),
            ("TrDizin", "TrDizin:ApiBaseUrl", "https://search.trdizin.gov.tr"),
            ("Crossref", "Crossref:ApiBaseUrl", "https://api.crossref.org"),
            ("SemanticScholar", "SemanticScholar:ApiBaseUrl", "https://api.semanticscholar.org/graph/v1")
        })
        {
            int interval = configuration.GetValue($"ProviderRequestLimits:{name}:MinimumIntervalMilliseconds", 1000);
            int dailyLimit = ProviderRequestPolicy.GetEffectiveDailyRequestLimit(configuration, name);
            int rateLimitCooldownSeconds = configuration.GetValue(
                $"ProviderRequestLimits:{name}:RateLimitCooldownSeconds", 0);
            if (interval is < 1 or > 60000 || dailyLimit < 0 || rateLimitCooldownSeconds is < 0 or > 3600)
                throw new InvalidOperationException($"Invalid request limits for {name}.");
            policies.Add(new()
            {
                Enabled = configuration.GetValue($"ProviderRequestLimits:{name}:Enabled", true),
                Name = name,
                Host = new Uri(configuration[key] ?? defaultUrl).Host,
                MinimumIntervalMilliseconds = interval,
                DailyRequestLimit = dailyLimit,
                RateLimitCooldownSeconds = rateLimitCooldownSeconds
            });
        }
        ProviderRateLimitHandler handler = new(
            configuration.GetConnectionString("AcademicDatabase")!, policies, logger)
        {
            InnerHandler = new HttpClientHandler { AllowAutoRedirect = false }
        };
        HttpClient httpClient = new(handler);
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AcademicCollectorDemo/0.1");
        return httpClient;
    }
}
