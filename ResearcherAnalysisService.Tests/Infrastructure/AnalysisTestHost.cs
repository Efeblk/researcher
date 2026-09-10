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
        bool configureAccessKey = true, string environment = "Testing", HttpMessageHandler? geminiHandler = null)
    {
        WebApplication application = Program.CreateApplication(["--environment", environment], builder =>
        {
            // Tests never use developer secrets, application settings, or paid provider requests.
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Urls"] = "http://127.0.0.1:0",
                ["Service:ApiKey"] = configureAccessKey ? "synthetic-service-key" : null,
                ["Gemini:ApiKey"] = geminiHandler is null ? null : "synthetic-gemini-key"
            });
            builder.Logging.ClearProviders();
            if (generator is not null)
            {
                builder.Services.RemoveAll<IResearcherReportGenerator>();
                builder.Services.AddSingleton(generator);
            }
            if (geminiHandler is not null)
                builder.Services.AddHttpClient<GeminiArticleClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => geminiHandler);
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
