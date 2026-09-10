using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Api;
using ResearcherAnalysisService.Configuration;
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
            .Validate(value => value.ArticleVerifierMaxOutputTokens + 512 < value.ArticleContextTokens,
                "Ai:ArticleContextTokens must leave at least 512 tokens beyond Ai:ArticleVerifierMaxOutputTokens.")
            .Validate(value => value.ArticleFallbackChunkBytes <= value.ArticleContextTokens - value.ArticleMaxOutputTokens - 512,
                "Ai:ArticleFallbackChunkBytes must fit the conservative article input budget.")
            .ValidateOnStart();
        builder.Services.AddOptions<GeminiOptions>().BindConfiguration("Gemini");
        builder.Services.AddScoped<AnalysisAccessFilter>();
        builder.Services.AddScoped<ResearcherAnalysis>();
        builder.Services.AddScoped<ArticleSummarizer>();
        Action<IServiceProvider, HttpClient> configureClient = (services, client) =>
        {
            client.Timeout = TimeSpan.FromSeconds(services.GetRequiredService<IOptions<AiOptions>>().Value.TimeoutSeconds);
            client.MaxResponseContentBufferSize = 1024 * 1024;
        };
        builder.Services.AddHttpClient<OpenAiReportGenerator>(configureClient);
        builder.Services.AddHttpClient<OllamaReportGenerator>(configureClient);
        builder.Services.AddHttpClient<OllamaArticleSummaryGenerator>(configureClient);
        builder.Services.AddHttpClient<OllamaArticleClaimVerifier>(configureClient);
        builder.Services.AddHttpClient<GeminiArticleClient>(configureClient);
        builder.Services.AddScoped<GeminiArticleSummaryGenerator>();
        builder.Services.AddScoped<GeminiArticleClaimVerifier>();
        builder.Services.AddScoped<IArticleSummaryGenerator>(services =>
            services.GetRequiredService<IOptions<AiOptions>>().Value.ArticleProvider == "Ollama"
                ? services.GetRequiredService<OllamaArticleSummaryGenerator>()
                : services.GetRequiredService<GeminiArticleSummaryGenerator>());
        builder.Services.AddScoped<IArticleClaimVerifier>(services =>
            services.GetRequiredService<IOptions<AiOptions>>().Value.ArticleProvider == "Ollama"
                ? services.GetRequiredService<OllamaArticleClaimVerifier>()
                : services.GetRequiredService<GeminiArticleClaimVerifier>());
        builder.Services.AddScoped<IResearcherReportGenerator>(services =>
            services.GetRequiredService<IOptions<AiOptions>>().Value.Provider == "Ollama"
                ? services.GetRequiredService<OllamaReportGenerator>()
                : services.GetRequiredService<OpenAiReportGenerator>());
        configure?.Invoke(builder);

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
}
