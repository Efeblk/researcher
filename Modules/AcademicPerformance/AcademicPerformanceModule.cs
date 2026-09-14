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
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.TrDizin;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Crossref;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.SemanticScholar;
using AcademicCollectorDemo.Modules.AcademicPerformance.Knowledge;
using AcademicCollectorDemo.Modules.AcademicPerformance.GraphProjection;
using AcademicCollectorDemo.Modules.AcademicPerformance.ProductAccess;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Collection;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Persistence;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Persistence;
using AcademicCollectorDemo.Modules.AcademicPerformance.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using AcademicCollectorDemo.Modules.AcademicPerformance.Analysis;
using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;
using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries.Enrichment;
using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleReviews;
using AcademicCollectorDemo.Modules.AcademicPerformance.Evaluations;
using AcademicCollectorDemo.Modules.AcademicPerformance.HrDossiers;
using AcademicCollectorDemo.Modules.AcademicPerformance.FacultyAssistant;

namespace AcademicCollectorDemo.Modules.AcademicPerformance;

public static class AcademicPerformanceModule
{
    public static IServiceCollection AddAcademicPerformanceModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<AnalysisServiceOptions>().Bind(configuration.GetSection("AnalysisService"))
            .ValidateDataAnnotations().Validate(value => Uri.TryCreate(value.BaseUrl, UriKind.Absolute, out var uri) &&
                (uri.Scheme == "https" || uri.Scheme == "http" && uri.IsLoopback) && string.IsNullOrEmpty(uri.UserInfo),
                "AnalysisService:BaseUrl must use HTTPS or loopback HTTP.");
        services.AddHttpClient<AnalysisServiceClient>((provider, client) =>
        {
            AnalysisServiceOptions options = provider.GetRequiredService<IOptions<AnalysisServiceOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            client.MaxResponseContentBufferSize = 1024 * 1024;
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddScoped<ResearcherAnalysisWorkflow>();
        services.TryAddSingleton<IAcademicProductAccessService, UnconfiguredAcademicProductAccessService>();
        services.AddScoped<HrEvidenceDossierService>();
        services.AddOptions<FacultyAssistantOptions>().Bind(configuration.GetSection("FacultyAssistant"))
            .ValidateDataAnnotations();
        services.AddScoped<FacultyAssistantContextService>();
        services.AddScoped<FacultyAssistantScheduler>();
        services.AddScoped<FacultyAssistantProcessor>();
        services.AddScoped<FacultyAssistantReadService>();
        services.AddHttpClient<FacultyAssistantServiceClient>((provider, client) =>
        {
            AnalysisServiceOptions analysis = provider.GetRequiredService<IOptions<AnalysisServiceOptions>>().Value;
            FacultyAssistantOptions assistant = provider.GetRequiredService<IOptions<FacultyAssistantOptions>>().Value;
            client.BaseAddress = new Uri(analysis.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(assistant.RequestTimeoutSeconds);
            client.MaxResponseContentBufferSize = 4 * 1024 * 1024;
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddHostedService<FacultyAssistantWorker>();
        services.AddArticleExtraction(configuration);
        services.AddScoped<ArticleSummaryWorkflow>();
        services.AddOptions<ArticleSummaryAutomationOptions>()
            .Bind(configuration.GetSection("ArticleSummaryAutomation"))
            .ValidateDataAnnotations();
        services.AddScoped<ArticleSummaryAutomationScheduler>();
        services.AddScoped<ArticleSummaryAutomationProcessor>();
        services.AddScoped<ArticleSummaryAutomationStatusService>();
        services.AddHostedService<ArticleSummaryAutomationWorker>();
        services.AddOptions<PublicationMetricsOptions>()
            .Bind(configuration.GetSection("PublicationMetrics"))
            .ValidateDataAnnotations();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.AddScoped<PublicationMetricsRefreshScheduler>();
        services.AddScoped<PublicationMetricSourceLoader>();
        services.AddScoped<IPublicationMetricsComputer, PublicationMetricsComputer>();
        services.AddScoped<PublicationMetricsProcessor>();
        services.AddScoped<PublicationMetricsReadService>();
        services.AddScoped<PublicationMetricsRefreshService>();
        services.AddScoped<ReferencePopulationManifestService>();
        services.AddHostedService<PublicationMetricsWorker>();
        services.AddScoped<IAcademicEvidenceSearchService, AcademicEvidenceSearchService>();
        services.AddScoped<AcademicGraphProjectionService>();
        services.AddHttpClient<ArticleSummaryServiceClient>((provider, client) =>
        {
            AnalysisServiceOptions analysis = provider.GetRequiredService<IOptions<AnalysisServiceOptions>>().Value;
            ArticleSummaryOptions summary = provider.GetRequiredService<IOptions<ArticleSummaryOptions>>().Value;
            client.BaseAddress = new Uri(analysis.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(summary.TotalTimeoutSeconds);
            client.MaxResponseContentBufferSize = 4 * 1024 * 1024;
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
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
        services.AddTransient<WebOfScienceClient>();
        services.AddTransient<YoksisClient>();
        services.AddTransient<TrDizinClient>();
        services.AddTransient<CrossrefClient>();
        services.AddScoped<CrossrefEnrichmentService>();
        services.AddOptions<SemanticScholarOptions>().Bind(configuration.GetSection("SemanticScholar")).ValidateDataAnnotations();
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
        services.AddScoped<CanonicalArticleEvidenceQueryService>();
        services.AddOptions<ArticleReviewOptions>().Bind(configuration.GetSection("ArticleReview"))
            .ValidateDataAnnotations();
        services.AddScoped<ArticleReviewWorkflow>();
        services.AddHttpClient<ArticleReviewServiceClient>((provider, client) =>
        {
            AnalysisServiceOptions analysis = provider.GetRequiredService<IOptions<AnalysisServiceOptions>>().Value;
            ArticleReviewOptions review = provider.GetRequiredService<IOptions<ArticleReviewOptions>>().Value;
            client.BaseAddress = new Uri(analysis.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(review.TotalTimeoutSeconds);
            client.MaxResponseContentBufferSize = 4 * 1024 * 1024;
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddOptions<ArticleEvaluationOptions>().Bind(configuration.GetSection("ArticleEvaluation"))
            .ValidateDataAnnotations();
        services.AddScoped<ArticleEvaluationScheduler>();
        services.AddScoped<ArticleEvaluationProcessor>();
        services.AddScoped<ArticleEvaluationReadService>();
        services.AddHttpClient<ArticleEvaluationServiceClient>((provider, client) =>
        {
            AnalysisServiceOptions analysis = provider.GetRequiredService<IOptions<AnalysisServiceOptions>>().Value;
            ArticleEvaluationOptions evaluation = provider.GetRequiredService<IOptions<ArticleEvaluationOptions>>().Value;
            client.BaseAddress = new Uri(analysis.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(evaluation.RequestTimeoutSeconds);
            client.MaxResponseContentBufferSize = 4 * 1024 * 1024;
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddHostedService<ArticleEvaluationWorker>();
        services.AddScoped<PublicationSummarySynchronizer>();
        services.AddScoped<ResearcherCollectionService>();
        services.AddScoped<ResearcherCollectionHandler>();
        services.AddScoped<IAcademicPerformanceApplicationService,
            AcademicPerformanceApplicationService>();
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
            ("WebOfScience", "WebOfScience:ApiBaseUrl", "https://api.clarivate.com/apis/wos-starter/v1"),
            ("Yoksis", "Yoksis:ServiceUrl", "https://servisler.yok.gov.tr/ws/OzgecmisV2")
            ,("TrDizin", "TrDizin:ApiBaseUrl", "https://search.trdizin.gov.tr")
            ,("Crossref", "Crossref:ApiBaseUrl", "https://api.crossref.org")
            ,("SemanticScholar", "SemanticScholar:ApiBaseUrl", "https://api.semanticscholar.org/graph/v1")
        })
        {
            int interval = configuration.GetValue($"ProviderRequestLimits:{name}:MinimumIntervalMilliseconds", 1000);
            int dailyLimit = configuration.GetValue($"ProviderRequestLimits:{name}:DailyRequestLimit", 0);
            if (interval is < 1 or > 60000 || dailyLimit < 0)
                throw new InvalidOperationException($"Invalid request limits for {name}.");
            policies.Add(new()
            {
                Enabled = configuration.GetValue($"ProviderRequestLimits:{name}:Enabled", true),
                Name = name,
                Host = new Uri(configuration[key] ?? defaultUrl).Host,
                MinimumIntervalMilliseconds = interval,
                DailyRequestLimit = dailyLimit
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
