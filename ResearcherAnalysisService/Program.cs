using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Api;
using ResearcherAnalysisService.Configuration;
using ResearcherAnalysisService.Integrations.OpenAi;

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
        builder.Services.AddOptions<AiOptions>().BindConfiguration("Ai").ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddScoped<AnalysisAccessFilter>();
        builder.Services.AddScoped<ResearcherAnalysis>();
        builder.Services.AddHttpClient<IResearcherReportGenerator, OpenAiReportGenerator>((services, client) =>
        {
            client.Timeout = TimeSpan.FromSeconds(services.GetRequiredService<IOptions<AiOptions>>().Value.TimeoutSeconds);
            client.MaxResponseContentBufferSize = 1024 * 1024;
        });
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
