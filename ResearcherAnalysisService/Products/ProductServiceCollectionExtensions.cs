using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ResearcherAnalysisService.Background;
using ResearcherAnalysisService.Products.Analysis;
using ResearcherAnalysisService.Products.ArticleReviews;
using ResearcherAnalysisService.Products.ArticleSummaries;
using ResearcherAnalysisService.Products.Data;
using ResearcherAnalysisService.Products.Evaluations;
using ResearcherAnalysisService.Products.FacultyAssistant;
using ResearcherAnalysisService.Products.GraphProjection;
using ResearcherAnalysisService.Products.HrDossiers;
using ResearcherAnalysisService.Products.Knowledge;
using ResearcherAnalysisService.Products.Metrics;
using ResearcherAnalysisService.Products.ProductAccess;

namespace ResearcherAnalysisService.Products;

public static class ProductServiceCollectionExtensions
{
    public static IServiceCollection AddAcademicProducts(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string connectionString = configuration.GetConnectionString("UsageDatabase")
            ?? throw new InvalidOperationException("ConnectionStrings:UsageDatabase is required.");
        services.AddDbContext<AnalysisDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<AnalysisSourceLock>();
        services.AddOptions<CollectionChangeOptions>().Bind(configuration.GetSection("CollectionChanges"))
            .ValidateDataAnnotations();
        services.AddScoped<CollectionChangeProcessor>();

        services.AddOptions<AnalysisServiceOptions>().Bind(configuration.GetSection("AnalysisProducts"))
            .ValidateDataAnnotations();
        services.AddScoped<AnalysisServiceClient>();
        services.AddScoped<ResearcherAnalysisWorkflow>();

        services.TryAddSingleton<IAcademicProductAccessService, UnconfiguredAcademicProductAccessService>();
        services.AddScoped<HrEvidenceDossierService>();
        services.AddOptions<FacultyAssistantOptions>().Bind(configuration.GetSection("FacultyAssistant"))
            .ValidateDataAnnotations();
        services.AddScoped<FacultyAssistantContextService>();
        services.AddScoped<FacultyAssistantScheduler>();
        services.AddScoped<FacultyAssistantProcessor>();
        services.AddScoped<FacultyAssistantReadService>();
        services.AddScoped<FacultyAssistantServiceClient>();

        services.AddArticleExtraction(configuration);
        services.AddScoped<ArticleSummaryServiceClient>();
        services.AddScoped<ArticleSummaryWorkflow>();
        services.AddOptions<ArticleSummaryAutomationOptions>()
            .Bind(configuration.GetSection("ArticleSummaryAutomation")).ValidateDataAnnotations();
        services.AddScoped<ArticleSummaryAutomationScheduler>();
        services.AddScoped<ArticleSummaryAutomationProcessor>();
        services.AddScoped<ArticleSummaryAutomationStatusService>();
        services.AddScoped<CanonicalArticleEvidenceQueryService>();
        services.AddScoped<CanonicalArticleAnalysisQueryService>();

        services.AddOptions<PublicationMetricsOptions>()
            .Bind(configuration.GetSection("PublicationMetrics")).ValidateDataAnnotations();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.AddScoped<PublicationMetricsRefreshScheduler>();
        services.AddScoped<PublicationMetricSourceLoader>();
        services.AddScoped<IPublicationMetricsComputer, PublicationMetricsComputer>();
        services.AddScoped<PublicationMetricsProcessor>();
        services.AddScoped<PublicationMetricsReadService>();
        services.AddScoped<PublicationMetricsRefreshService>();
        services.AddScoped<ReferencePopulationManifestService>();
        services.AddScoped<IAcademicEvidenceSearchService, AcademicEvidenceSearchService>();
        services.AddScoped<AcademicGraphProjectionService>();

        services.AddOptions<ArticleReviewOptions>().Bind(configuration.GetSection("ArticleReview"))
            .ValidateDataAnnotations();
        services.AddScoped<ArticleReviewServiceClient>();
        services.AddScoped<ArticleReviewWorkflow>();
        services.AddOptions<ArticleEvaluationOptions>().Bind(configuration.GetSection("ArticleEvaluation"))
            .ValidateDataAnnotations();
        services.AddScoped<ArticleEvaluationServiceClient>();
        services.AddScoped<ArticleEvaluationScheduler>();
        services.AddScoped<ArticleEvaluationProcessor>();
        services.AddScoped<ArticleEvaluationReadService>();

        // Registration order matters: Program registers the migration hosted service first.
        services.AddHostedService<CollectionChangeWorker>();
        services.AddHostedService<FacultyAssistantWorker>();
        services.AddHostedService<ArticleSummaryAutomationWorker>();
        services.AddHostedService<PublicationMetricsWorker>();
        services.AddHostedService<ArticleEvaluationWorker>();
        return services;
    }
}
