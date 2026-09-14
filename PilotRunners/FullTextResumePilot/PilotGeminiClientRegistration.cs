using Microsoft.Extensions.DependencyInjection;
using ResearcherAnalysisService.Integrations.Gemini;

namespace FullTextResumePilot;

public static class PilotGeminiClientRegistration
{
    public const string EvaluationClientName = "ArticleEvaluationProvider";

    public static void AddGuardedClients(IServiceCollection services)
    {
        services.AddHttpClient<GeminiArticleClient>()
            .ConfigurePrimaryHttpMessageHandler(CreatePrimaryHandler)
            .AddHttpMessageHandler<PilotGeminiBudgetHandler>();
        services.AddHttpClient(EvaluationClientName)
            .ConfigurePrimaryHttpMessageHandler(CreatePrimaryHandler)
            .AddHttpMessageHandler<PilotGeminiBudgetHandler>();
    }

    private static HttpMessageHandler CreatePrimaryHandler() => new HttpClientHandler
    {
        AllowAutoRedirect = false
    };
}
