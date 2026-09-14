using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Api;
using ResearcherAnalysisService.Configuration;
using ResearcherAnalysisService.Data;
using ResearcherAnalysisService.Integrations.OpenAi;
using ResearcherAnalysisService.Integrations.Ollama;
using ResearcherAnalysisService.Integrations.Gemini;

namespace ResearcherAnalysisService;

public static class Program
{
    public static async Task Main(string[] args)
    {
        await using WebApplication application = CreateApplication(args);
        await application.RunAsync();
    }

    public static WebApplication CreateApplication(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ApplicationName = typeof(Program).Assembly.FullName
        });
        builder.Services.AddControllers();
        builder.Services.AddProblemDetails();
        builder.Services.AddOptions<AiOptions>().BindConfiguration("Ai").ValidateDataAnnotations()
            .Validate(value => value.ArticleMaxOutputTokens + 512 < value.ArticleContextTokens,
                "Ai:ArticleContextTokens must leave at least 512 tokens beyond Ai:ArticleMaxOutputTokens.")
            .Validate(value => value.ArticleProvider != "Gemini" ||
                    value.FacultyAssistantMaxOutputTokens + 512 < value.ArticleContextTokens,
                "Ai:ArticleContextTokens must leave at least 512 tokens beyond Ai:FacultyAssistantMaxOutputTokens.")
            .Validate(value => value.ArticleVerifierMaxOutputTokens + 512 < value.ArticleContextTokens,
                "Ai:ArticleContextTokens must leave at least 512 tokens beyond Ai:ArticleVerifierMaxOutputTokens.")
            .Validate(value => value.ArticleFallbackChunkBytes <= value.ArticleContextTokens - value.ArticleMaxOutputTokens - 512,
                "Ai:ArticleFallbackChunkBytes must fit the conservative article input budget.")
            .ValidateOnStart();
        builder.Services.AddOptions<GeminiOptions>().BindConfiguration("Gemini");
        builder.Services.AddOptions<ArticleEvaluationOptions>().BindConfiguration("Evaluation")
            .ValidateDataAnnotations()
            .Validate(value => value.MaxOutputTokens + 512 < value.ContextTokens,
                "Evaluation:ContextTokens must leave at least 512 tokens beyond Evaluation:MaxOutputTokens.")
            .Validate(value => value.VerifierMaxOutputTokens + 512 < value.ContextTokens,
                "Evaluation:ContextTokens must leave at least 512 tokens beyond Evaluation:VerifierMaxOutputTokens.")
            .Validate(value => HasCompleteDeepSeekPricing(value.DeepSeek),
                "Evaluation:DeepSeek pricing must be absent or contain a version and all non-negative rates.")
            .ValidateOnStart();
        builder.Services.AddSingleton<IGeminiUsageRepository, GeminiUsageRepository>();
        builder.Services.AddScoped<AnalysisAccessFilter>();
        builder.Services.AddScoped<ResearcherAnalysis>();
        builder.Services.AddScoped<ArticleSummarizer>();
        builder.Services.AddScoped<ArticleReviewer>();
        builder.Services.AddScoped<ArticleReviewDispatchContext>();
        builder.Services.AddScoped<ArticleReviewStageExecutor>();
        builder.Services.AddScoped<ArticleEvaluationProfileCatalog>();
        builder.Services.AddScoped<ArticleEvaluationService>();
        Action<IServiceProvider, HttpClient> configureClient = (services, client) =>
        {
            client.Timeout = TimeSpan.FromSeconds(services.GetRequiredService<IOptions<AiOptions>>().Value.TimeoutSeconds);
            client.MaxResponseContentBufferSize = 1024 * 1024;
        };
        builder.Services.AddHttpClient<OpenAiReportGenerator>(configureClient);
        builder.Services.AddHttpClient<OllamaReportGenerator>(configureClient);
        builder.Services.AddHttpClient<OllamaArticleSummaryGenerator>(configureClient);
        builder.Services.AddHttpClient<OllamaArticleClaimVerifier>(configureClient);
        builder.Services.AddHttpClient<OllamaArticleReviewGenerator>(configureClient);
        builder.Services.AddHttpClient<OllamaArticleReviewVerifier>(configureClient);
        builder.Services.AddHttpClient<GeminiArticleClient>(configureClient);
        builder.Services.AddHttpClient("ArticleEvaluationProvider", (services, client) =>
        {
            client.Timeout = TimeSpan.FromSeconds(services
                .GetRequiredService<IOptions<ArticleEvaluationOptions>>().Value.TimeoutSeconds);
            client.MaxResponseContentBufferSize = 1024 * 1024;
        });
        builder.Services.AddScoped<GeminiArticleSummaryGenerator>();
        builder.Services.AddScoped<GeminiArticleClaimVerifier>();
        builder.Services.AddScoped<GeminiArticleReviewGenerator>();
        builder.Services.AddScoped<GeminiArticleReviewVerifier>();
        builder.Services.AddScoped<GeminiFacultyAssistantGenerator>();
        builder.Services.AddScoped<GeminiFacultyAssistantVerifier>();
        builder.Services.AddScoped<GeminiFacultyAssistantRepairGenerator>();
        builder.Services.AddScoped<GeminiFacultyRequestCoverageVerifier>();
        builder.Services.AddScoped<IFacultyAssistantGenerator>(services =>
            services.GetRequiredService<GeminiFacultyAssistantGenerator>());
        builder.Services.AddScoped<IFacultyAssistantVerifier>(services =>
            services.GetRequiredService<GeminiFacultyAssistantVerifier>());
        builder.Services.AddScoped<IFacultyAssistantRepairGenerator>(services =>
            services.GetRequiredService<GeminiFacultyAssistantRepairGenerator>());
        builder.Services.AddScoped<IFacultyRequestCoverageVerifier>(services =>
            services.GetRequiredService<GeminiFacultyRequestCoverageVerifier>());
        builder.Services.AddScoped(services => new FacultyAssistant(
            services.GetRequiredService<IFacultyAssistantGenerator>(),
            services.GetRequiredService<IFacultyAssistantVerifier>(),
            services.GetRequiredService<IFacultyAssistantRepairGenerator>(),
            services.GetRequiredService<IFacultyRequestCoverageVerifier>()));
        builder.Services.AddScoped<IArticleSummaryGenerator>(services =>
            services.GetRequiredService<IOptions<AiOptions>>().Value.ArticleProvider == "Ollama"
                ? services.GetRequiredService<OllamaArticleSummaryGenerator>()
                : services.GetRequiredService<GeminiArticleSummaryGenerator>());
        builder.Services.AddScoped<IArticleClaimVerifier>(services =>
            services.GetRequiredService<IOptions<AiOptions>>().Value.ArticleProvider == "Ollama"
                ? services.GetRequiredService<OllamaArticleClaimVerifier>()
                : services.GetRequiredService<GeminiArticleClaimVerifier>());
        builder.Services.AddScoped<IArticleReviewGenerator>(services =>
            services.GetRequiredService<IOptions<AiOptions>>().Value.ArticleProvider == "Ollama"
                ? services.GetRequiredService<OllamaArticleReviewGenerator>()
                : services.GetRequiredService<GeminiArticleReviewGenerator>());
        builder.Services.AddScoped<IArticleReviewVerifier>(services =>
            services.GetRequiredService<IOptions<AiOptions>>().Value.ArticleProvider == "Ollama"
                ? services.GetRequiredService<OllamaArticleReviewVerifier>()
                : services.GetRequiredService<GeminiArticleReviewVerifier>());
        builder.Services.AddScoped<IResearcherReportGenerator>(services =>
            services.GetRequiredService<IOptions<AiOptions>>().Value.Provider == "Ollama"
                ? services.GetRequiredService<OllamaReportGenerator>()
                : services.GetRequiredService<OpenAiReportGenerator>());
        configure?.Invoke(builder);

        if (builder.Configuration.GetValue("DatabaseMigrations:Enabled", true))
        {
            builder.Services.AddAnalysisDatabaseMigrations(builder.Configuration);
            builder.Services.AddHostedService<AnalysisDatabaseMigrationHostedService>();
        }

        WebApplication application = builder.Build();
        application.UseExceptionHandler(new ExceptionHandlerOptions
        {
            StatusCodeSelector = exception => exception is BadHttpRequestException badRequest
                ? badRequest.StatusCode
                : StatusCodes.Status500InternalServerError
        });
        application.MapGet("/health", () => Results.Ok(new { Service = "ResearcherAnalysisService", Status = "Running" }));
        application.MapControllers();
        return application;
    }

    private static bool HasCompleteDeepSeekPricing(DeepSeekEvaluationOptions options)
    {
        bool any = options.InputUsdPerMillionTokens.HasValue || options.CacheHitUsdPerMillionTokens.HasValue ||
            options.OutputUsdPerMillionTokens.HasValue || !string.IsNullOrWhiteSpace(options.PricingVersion);
        return !any || !string.IsNullOrWhiteSpace(options.PricingVersion) &&
            options.InputUsdPerMillionTokens is >= 0 && options.CacheHitUsdPerMillionTokens is >= 0 &&
            options.OutputUsdPerMillionTokens is >= 0;
    }
}
