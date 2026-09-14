using System.Net;
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using AcademicCollectorDemo.Host;
using AcademicCollectorDemo.Modules.AcademicPerformance;
using ResearcherAnalysisService.Products.ArticleSummaries;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using ResearcherAnalysisService.Products.ProductAccess;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using ResearcherAnalysisService.Integrations.Gemini;
using Serenity;
using Serenity.Extensions.DependencyInjection;

namespace ServiceAcceptancePilot;

internal static class RetainedAcceptanceHost
{
    public const string SubjectId = "service-acceptance-faculty";
    public const string Orcid = "0000-0002-1825-0097";
    public const string Header = "X-Service-Acceptance-Auth";
    public const string HeaderValue = "synthetic-faculty";
    public const string ServiceKey = "service-acceptance-analysis-key";
    private static readonly string SourceDirectory =
        Environment.GetEnvironmentVariable("ACADEMIC_ACCEPTANCE_SOURCE_DIRECTORY") ??
        Path.Combine(Path.GetTempPath(), "academic-fulltext-source-t9aqbsak");

    public static async Task<WebApplication> StartCollectorAsync(
        string root, string database, string url, string analysisUrl, string mode,
        ReplayAudit? replayAudit = null)
    {
        WebApplication app = CreateCollectorApplication(
            root, database, url, analysisUrl, mode, replayAudit);
        Serenity.Data.RowFieldsProvider.SetDefaultFrom(app.Services);
        app.Services.MigrateAcademicDatabase();
        await app.StartAsync();
        return app;
    }

    internal static WebApplication CreateCollectorApplication(
        string root, string database, string url, string analysisUrl, string mode,
        ReplayAudit? replayAudit = null)
    {
        replayAudit ??= new ReplayAudit();
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing", ContentRootPath = root,
            ApplicationName = typeof(AcademicPerformanceModule).Assembly.FullName
        });
        builder.WebHost.UseUrls(url);
        builder.Configuration.AddJsonFile("appsettings.bundles.json", false)
            .AddJsonFile("academicsettings.json", false)
            .AddInMemoryCollection(Configuration(database, analysisUrl, mode));
        builder.Logging.ClearProviders();
        builder.Services.AddAcademicPerformanceModule(builder.Configuration);
        builder.Services.RemoveAll<HttpClient>();
        builder.Services.AddSingleton(new HttpClient(new ProviderReplayHandler(replayAudit)));
        builder.Services.AddAuthentication("Acceptance")
            .AddScheme<AuthenticationSchemeOptions, AcceptanceAuthenticationHandler>("Acceptance", _ => { });
        builder.Services.AddApplicationPartsTypeSource();
        builder.Services.ConfigureSections(builder.Configuration);
        builder.Services.AddCaching(); builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<Serenity.Data.IRowFieldsProvider, Serenity.Data.DefaultRowFieldsProvider>();
        builder.Services.AddSingleton<Serenity.Abstractions.IPermissionService, DevelopmentPermissionService>();
        builder.Services.AddControllersWithViews(); builder.Services.AddServiceEndpointConventions();
        builder.Services.Configure<JsonOptions>(options => JSON.Defaults.Populate(options.JsonSerializerOptions));
        WebApplication app = builder.Build();
        app.UseRouting(); app.UseAuthentication(); app.MapControllers();
        app.MapGet("/", () => Results.Ok(new { Run = Program.RunId, Mode = mode }));
        return app;
    }

    public static async Task<WebApplication> StartLiveAnalysisAsync(
        string database, string url, ServiceAcceptanceBudget budget, RetainedProviderCapture? capture = null)
    {
        WebApplication app = CreateLiveAnalysisApplication(database, url, budget, capture);
        app.UseAuthentication();
        await app.StartAsync();
        return app;
    }

    internal static WebApplication CreateLiveAnalysisApplication(
        string database, string url, ServiceAcceptanceBudget budget, RetainedProviderCapture? capture = null)
    {
        IConfigurationRoot secrets = new ConfigurationBuilder()
            .AddUserSecrets(typeof(ResearcherAnalysisService.Program).Assembly, optional: false).Build();
        string apiKey = secrets["Gemini:ApiKey"] ?? throw new InvalidOperationException("Gemini key is missing.");
        WebApplication app = ResearcherAnalysisService.Program.CreateApplication(
            ["--environment", "Testing", "--urls", url], builder =>
            {
                builder.Configuration.Sources.Clear();
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Urls"] = url, ["Service:ApiKey"] = ServiceKey,
                    ["ConnectionStrings:UsageDatabase"] = database, ["Gemini:ApiKey"] = apiKey,
                    ["Ai:Provider"] = "Ollama", ["Ai:ArticleProvider"] = "Gemini",
                    ["Ai:ArticleModel"] = ServiceAcceptanceBudget.RequiredModel,
                    ["Ai:ArticleVerifierModel"] = ServiceAcceptanceBudget.RequiredModel,
                    ["Ai:ArticleGenerationThinkingLevel"] = "high", ["Ai:ArticleVerifierThinkingLevel"] = "high",
                    ["Ai:FacultyAssistantGenerationThinkingLevel"] = "medium",
                    ["Ai:FacultyAssistantVerifierThinkingLevel"] = "medium",
                    ["Ai:ArticleContextTokens"] = "131072", ["Ai:ArticleMaxOutputTokens"] = "8192",
                    ["Ai:ArticleVerifierMaxOutputTokens"] = "8192", ["Ai:FacultyAssistantMaxOutputTokens"] = "16384",
                    ["Ai:ArticleFallbackChunkBytes"] = "10000", ["Ai:TimeoutSeconds"] = "180"
                });
                builder.Logging.ClearProviders();
                builder.Services.AddSingleton<IAcademicProductAccessService, AcceptanceAccessService>();
                builder.Services.AddAuthentication("Acceptance")
                    .AddScheme<AuthenticationSchemeOptions, AcceptanceAuthenticationHandler>("Acceptance", _ => { });
                builder.Services.AddSingleton(budget);
                if (capture is not null) builder.Services.AddSingleton(capture);
                builder.Services.AddTransient(_ => new RetainedBudgetHandler(budget, capture,
                    allowFacultyInitialMedium: true));
                builder.Services.AddHttpClient<GeminiArticleClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
                    .AddHttpMessageHandler<RetainedBudgetHandler>();
                builder.Services.AddHttpClient("ArticleEvaluationProvider")
                    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
                    .AddHttpMessageHandler<RetainedBudgetHandler>();
            });
        return app;
    }

    private static Dictionary<string, string?> Configuration(
        string database, string analysisUrl, string mode) => new()
    {
        ["ConnectionStrings:AcademicDatabase"] = database,
        ["BulkCollection:WorkerEnabled"] = (mode == "bulk").ToString(), ["BulkCollection:PollSeconds"] = "1",
        ["BulkCollection:MaximumAttempts"] = "1",
        ["ProviderRequestLimits:TrDizin:Enabled"] = "false",
        ["ProviderRequestLimits:Orcid:Enabled"] = "true", ["ProviderRequestLimits:OpenAlex:Enabled"] = "false",
        ["ProviderRequestLimits:Crossref:Enabled"] = "false",
        ["ProviderRequestLimits:SemanticScholar:Enabled"] = "false",
        ["ArticleSummary:OcrEnabled"] = "false"
    };

    private sealed class ProviderReplayHandler(ReplayAudit audit) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri? uri = request.RequestUri;
            string path = uri?.AbsolutePath ?? string.Empty;
            bool record = request.Method == HttpMethod.Get && uri?.Host == "pub.orcid.org" &&
                path == $"/v3.0/{Orcid}/record";
            bool works = request.Method == HttpMethod.Get && uri?.Host == "pub.orcid.org" &&
                path == $"/v3.0/{Orcid}/works/1,2";
            audit.AddProvider(request, record || works);
            string body = record ? RecordJson() : works ? WorksJson() : "{}";
            HttpStatusCode status = record || works ? HttpStatusCode.OK : HttpStatusCode.NotFound;
            return Task.FromResult(new HttpResponseMessage(status)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }

        private static string RecordJson() => """
        {"orcid-identifier":{"path":"0000-0002-1825-0097"},"history":{"last-modified-date":{"value":1789300000000}},
        "person":{"name":{"given-names":{"value":"Sentetik"},"family-name":{"value":"Akademisyen"}},"addresses":{"address":[]},"keywords":{"keyword":[]}},
        "activities-summary":{"employments":{"affiliation-group":[]},"educations":{"affiliation-group":[]},"fundings":{"group":[]},"peer-reviews":{"group":[]},
        "works":{"group":[{"work-summary":[{"put-code":1,"display-index":"1"}]},{"work-summary":[{"put-code":2,"display-index":"1"}]}]}}}
        """;

        private static string WorksJson() => """
        {"bulk":[{"work":{"put-code":1,"title":{"title":{"value":"Adam: A Method for Stochastic Optimization"}},"type":"journal-article","publication-date":{"year":{"value":"2014"}},"external-ids":{"external-id":[{"external-id-type":"doi","external-id-value":"10.48550/arXiv.1412.6980"}]},"url":{"value":"https://arxiv.org/pdf/1412.6980v1"},"contributors":{"contributor":[]},"short-description":"A public paper describing the Adam optimization method.","visibility":"public"}},
        {"work":{"put-code":2,"title":{"title":{"value":"Multiagent off-screen behavior prediction in football"}},"type":"journal-article","publication-date":{"year":{"value":"2022"}},"external-ids":{"external-id":[{"external-id-type":"doi","external-id-value":"10.1038/s41598-022-12547-0"}]},"url":{"value":"https://livrepository.liverpool.ac.uk/3166141/1/Multiagent%20off-screen%20behavior%20prediction%20in%20football.pdf"},"contributors":{"contributor":[]},"short-description":"A public paper about multiagent football behavior prediction.","visibility":"public"}}]}
        """;
    }

    private sealed class SourceReplayHandler(ReplayAudit audit) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri? uri = request.RequestUri;
            string? file = request.Method == HttpMethod.Get && uri?.Host == "arxiv.org" &&
                uri.AbsolutePath == "/pdf/1412.6980v1" ? "adam-v1-fulltext.pdf" :
                request.Method == HttpMethod.Get && uri?.Host == "livrepository.liverpool.ac.uk" &&
                Uri.UnescapeDataString(uri.AbsolutePath) ==
                    "/3166141/1/Multiagent off-screen behavior prediction in football.pdf"
                    ? "football-fulltext.pdf" : null;
            audit.AddSource(request, file is not null);
            if (file is null) return new(HttpStatusCode.NotFound);
            byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(SourceDirectory, file), cancellationToken);
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }
    }
}

internal sealed class ReplayAudit
{
    private readonly ConcurrentQueue<string> provider = new();
    private readonly ConcurrentQueue<string> source = new();
    private int providerServed;
    private int sourceServed;
    public int ProviderServed => Volatile.Read(ref providerServed);
    public int SourceServed => Volatile.Read(ref sourceServed);

    public void AddProvider(HttpRequestMessage request, bool served)
    {
        provider.Enqueue($"{request.Method} {request.RequestUri} {(served ? "served" : "rejected")}");
        if (served) Interlocked.Increment(ref providerServed);
    }

    public void AddSource(HttpRequestMessage request, bool served)
    {
        source.Enqueue($"{request.Method} {request.RequestUri} {(served ? "served" : "rejected")}");
        if (served) Interlocked.Increment(ref sourceServed);
    }

    public System.Text.Json.Nodes.JsonObject ToJson() => new()
    {
        ["providerServed"] = ProviderServed, ["sourceServed"] = SourceServed,
        ["providerTargets"] = System.Text.Json.JsonSerializer.SerializeToNode(provider.ToArray()),
        ["sourceTargets"] = System.Text.Json.JsonSerializer.SerializeToNode(source.ToArray())
    };
}

internal sealed class AcceptanceAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(RetainedAcceptanceHost.Header, out var value) ||
            value != RetainedAcceptanceHost.HeaderValue) return Task.FromResult(AuthenticateResult.NoResult());
        ClaimsIdentity identity = new([new Claim(ClaimTypes.NameIdentifier, "service-acceptance-actor")], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new(identity), Scheme.Name)));
    }
}

internal sealed class AcceptanceAccessService : IAcademicProductAccessService
{
    public Task<AcademicProductAccessGrant> AuthorizeAsync(ClaimsPrincipal principal,
        AcademicProductAccessRequest request, CancellationToken cancellationToken)
    {
        if (principal.Identity?.IsAuthenticated != true) throw new AcademicProductUnauthenticatedException();
        if (request.SubjectPersonelId != RetainedAcceptanceHost.SubjectId) throw new AcademicProductAccessDeniedException();
        return Task.FromResult(new AcademicProductAccessGrant("service-acceptance-grant",
            "service-acceptance-actor", RetainedAcceptanceHost.SubjectId, request.Operation));
    }

    public Task<AcademicProductAccessGrant> ReauthorizeAsync(
        AcademicProductAccessGrant persistedGrant, CancellationToken cancellationToken) =>
        persistedGrant.AuthorizationGrantId == "service-acceptance-grant" &&
        persistedGrant.SubjectPersonelId == RetainedAcceptanceHost.SubjectId
            ? Task.FromResult(persistedGrant) : throw new AcademicProductAccessDeniedException();
}
