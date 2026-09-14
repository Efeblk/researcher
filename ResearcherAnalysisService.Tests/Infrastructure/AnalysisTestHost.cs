using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Integrations.Gemini;

namespace ResearcherAnalysisService.Tests.Infrastructure;

internal sealed class AnalysisTestHost(WebApplication application, HttpClient client) : IAsyncDisposable
{
    public HttpClient Client { get; } = client;

    public static async Task<AnalysisTestHost> StartAsync(IResearcherReportGenerator? generator = null,
        bool configureAccessKey = true, string environment = "Testing", HttpMessageHandler? geminiHandler = null,
        IReadOnlyDictionary<string, string?>? settings = null, IGeminiUsageRepository? usageRepository = null,
        IArticleReviewGenerator? reviewGenerator = null, IArticleReviewVerifier? reviewVerifier = null,
        HttpMessageHandler? evaluationHandler = null, string? usageDatabase = null)
    {
        WebApplication application = Program.CreateApplication(["--environment", environment], builder =>
        {
            // Tests never use developer secrets, application settings, or paid provider requests.
            builder.Configuration.Sources.Clear();
            Dictionary<string, string?> configuration = new()
            {
                ["Urls"] = "http://127.0.0.1:0",
                ["DatabaseMigrations:Enabled"] = "false",
                ["ConnectionStrings:UsageDatabase"] =
                    usageDatabase ?? (@"Server=(localdb)\MSSQLLocalDB;Database=ResearcherAnalysisStatelessTests;" +
                    "Integrated Security=true;Encrypt=true;TrustServerCertificate=true"),
                ["Service:ApiKey"] = configureAccessKey ? "synthetic-service-key" : null,
                ["Gemini:ApiKey"] = geminiHandler is null ? null : "synthetic-gemini-key",
                ["CollectionChanges:WorkerEnabled"] = "false",
                ["ArticleSummaryAutomation:Enabled"] = "false",
                ["ArticleSummaryAutomation:WorkerEnabled"] = "false",
                ["PublicationMetrics:WorkerEnabled"] = "false",
                ["ArticleEvaluation:WorkerEnabled"] = "false",
                ["FacultyAssistant:WorkerEnabled"] = "false"
            };
            if (settings is not null)
                foreach ((string key, string? value) in settings)
                    configuration[key] = value;
            builder.Configuration.AddInMemoryCollection(configuration);
            builder.Logging.ClearProviders();
            builder.Services.RemoveAll<IGeminiUsageRepository>();
            builder.Services.AddSingleton(usageRepository ?? new TestGeminiUsageRepository());
            if (generator is not null)
            {
                builder.Services.RemoveAll<IResearcherReportGenerator>();
                builder.Services.AddSingleton(generator);
            }
            if (reviewGenerator is not null)
            {
                builder.Services.RemoveAll<IArticleReviewGenerator>();
                builder.Services.AddSingleton(reviewGenerator);
            }
            if (reviewVerifier is not null)
            {
                builder.Services.RemoveAll<IArticleReviewVerifier>();
                builder.Services.AddSingleton(reviewVerifier);
            }
            if (geminiHandler is not null)
                builder.Services.AddHttpClient<GeminiArticleClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => geminiHandler);
            if (evaluationHandler is not null)
                builder.Services.AddHttpClient("ArticleEvaluationProvider")
                    .ConfigurePrimaryHttpMessageHandler(() => evaluationHandler);
        });
        await application.StartAsync();
        string address = application.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        HttpClient client = new() { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.Add("X-Analysis-Key", "synthetic-service-key");
        return new AnalysisTestHost(application, client);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await application.StopAsync();
        await application.DisposeAsync();
    }
}
