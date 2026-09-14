using System.Collections.Concurrent;
using System.Net;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Integrations.Gemini;
using Serenity;
using Serenity.Extensions.DependencyInjection;

namespace ServiceAcceptancePilot;

internal static class BroaderAcceptanceHost
{
    public const string PrimarySubjectId = "broader-acceptance-primary";
    public const string SecondarySubjectId = "broader-acceptance-secondary";
    public const string PrimaryOrcid = "0000-0002-1825-0097";
    public const string SecondaryOrcid = "0000-0001-5109-3700";
    public const string Header = "X-Broader-Acceptance-Auth";
    public const string HeaderValue = "synthetic-primary-faculty";
    public const string ServiceKey = "broader-acceptance-analysis-key";

    public static async Task<WebApplication> StartCollectorAsync(string root, string database,
        string url, string analysisUrl, string mode, string sourceDirectory, BroaderReplayAudit audit)
    {
        WebApplication app = CreateCollectorApplication(
            root, database, url, analysisUrl, mode, sourceDirectory, audit);
        Serenity.Data.RowFieldsProvider.SetDefaultFrom(app.Services);
        app.Services.MigrateAcademicDatabase();
        await app.StartAsync();
        return app;
    }

    internal static WebApplication CreateCollectorApplication(string root, string database,
        string url, string analysisUrl, string mode, string sourceDirectory, BroaderReplayAudit audit)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
            ContentRootPath = root,
            ApplicationName = typeof(AcademicPerformanceModule).Assembly.FullName
        });
        builder.WebHost.UseUrls(url);
        builder.Configuration.AddJsonFile("appsettings.bundles.json", false)
            .AddJsonFile("academicsettings.json", false)
            .AddInMemoryCollection(CollectorConfiguration(database, analysisUrl, mode));
        builder.Logging.ClearProviders();
        builder.Services.AddAcademicPerformanceModule(builder.Configuration);
        builder.Services.RemoveAll<HttpClient>();
        builder.Services.AddSingleton(new HttpClient(new ProviderReplayHandler(audit)));
        builder.Services.AddAuthentication("BroaderAcceptance")
            .AddScheme<AuthenticationSchemeOptions, BroaderAuthenticationHandler>(
                "BroaderAcceptance", _ => { });
        builder.Services.AddApplicationPartsTypeSource();
        builder.Services.ConfigureSections(builder.Configuration);
        builder.Services.AddCaching();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<Serenity.Data.IRowFieldsProvider,
            Serenity.Data.DefaultRowFieldsProvider>();
        builder.Services.AddSingleton<Serenity.Abstractions.IPermissionService,
            DevelopmentPermissionService>();
        builder.Services.AddControllersWithViews();
        builder.Services.AddServiceEndpointConventions();
        builder.Services.Configure<JsonOptions>(options =>
            JSON.Defaults.Populate(options.JsonSerializerOptions));
        WebApplication app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.MapControllers();
        app.MapGet("/", () => Results.Ok(new { Run = Program.RunId, Mode = mode }));
        return app;
    }

    public static async Task<WebApplication> StartLiveAnalysisAsync(string database, string url,
        ServiceAcceptanceBudget budget, BroaderDispatchGate dispatchGate, RetainedProviderCapture capture)
    {
        WebApplication app = CreateLiveAnalysisApplication(
            database, url, budget, dispatchGate, capture, GeminiKey());
        app.UseAuthentication();
        await app.StartAsync();
        return app;
    }

    internal static WebApplication CreateLiveAnalysisApplication(string database, string url,
        ServiceAcceptanceBudget budget, BroaderDispatchGate dispatchGate,
        RetainedProviderCapture capture, string apiKey)
    {
        return ResearcherAnalysisService.Program.CreateApplication(
            ["--environment", "Testing", "--urls", url], builder =>
            {
                builder.Configuration.Sources.Clear();
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Urls"] = url,
                    ["Service:ApiKey"] = ServiceKey,
                    ["ConnectionStrings:UsageDatabase"] = database,
                    ["Gemini:ApiKey"] = apiKey,
                    ["Ai:Provider"] = "Ollama",
                    ["Ai:ArticleProvider"] = "Gemini",
                    ["Ai:ArticleModel"] = ServiceAcceptanceBudget.RequiredModel,
                    ["Ai:ArticleVerifierModel"] = ServiceAcceptanceBudget.RequiredModel,
                    ["Ai:ArticleGenerationThinkingLevel"] = "high",
                    ["Ai:ArticleVerifierThinkingLevel"] = "high",
                    ["Ai:FacultyAssistantGenerationThinkingLevel"] = "medium",
                    ["Ai:FacultyAssistantVerifierThinkingLevel"] = "medium",
                    ["Ai:ArticleContextTokens"] = "131072",
                    ["Ai:ArticleMaxOutputTokens"] = "8192",
                    ["Ai:ArticleVerifierMaxOutputTokens"] = "8192",
                    ["Ai:FacultyAssistantMaxOutputTokens"] = "16384",
                    ["Ai:ArticleFallbackChunkBytes"] = "10000",
                    ["Ai:TimeoutSeconds"] = "180"
                });
                builder.Logging.ClearProviders();
                builder.Services.AddSingleton<IAcademicProductAccessService, BroaderAcceptanceAccessService>();
                builder.Services.AddAuthentication("BroaderAcceptance")
                    .AddScheme<AuthenticationSchemeOptions, BroaderAuthenticationHandler>(
                        "BroaderAcceptance", _ => { });
                builder.Services.AddSingleton(budget);
                builder.Services.AddSingleton(dispatchGate);
                builder.Services.AddSingleton(capture);
                builder.Services.AddTransient<BroaderDispatchHandler>();
                builder.Services.AddTransient(_ => new RetainedBudgetHandler(
                    budget, capture, allowFacultyInitialMedium: true));
                builder.Services.AddHttpClient<GeminiArticleClient>()
                    .ConfigurePrimaryHttpMessageHandler(() =>
                        new HttpClientHandler { AllowAutoRedirect = false })
                    .AddHttpMessageHandler<BroaderDispatchHandler>()
                    .AddHttpMessageHandler<RetainedBudgetHandler>();
                builder.Services.AddHttpClient("ArticleEvaluationProvider")
                    .ConfigurePrimaryHttpMessageHandler(() =>
                        new HttpClientHandler { AllowAutoRedirect = false })
                    .AddHttpMessageHandler<BroaderDispatchHandler>()
                    .AddHttpMessageHandler<RetainedBudgetHandler>();
            });
    }

    public static bool HasGeminiKey()
    {
        try { return !string.IsNullOrWhiteSpace(GeminiKey()); }
        catch (InvalidOperationException) { return false; }
    }

    private static string GeminiKey()
    {
        IConfigurationRoot secrets = new ConfigurationBuilder()
            .AddUserSecrets(typeof(ResearcherAnalysisService.Program).Assembly, optional: true).Build();
        return secrets["Gemini:ApiKey"] ?? throw new InvalidOperationException("Gemini key is missing.");
    }

    private static Dictionary<string, string?> CollectorConfiguration(
        string database, string analysisUrl, string mode) => new()
    {
        ["ConnectionStrings:AcademicDatabase"] = database,
        ["BulkCollection:WorkerEnabled"] = (mode == "bulk").ToString(),
        ["BulkCollection:PollSeconds"] = "1",
        ["BulkCollection:MaximumAttempts"] = "1",
        ["ProviderRequestLimits:Orcid:Enabled"] = "true",
        ["ProviderRequestLimits:TrDizin:Enabled"] = "false",
        ["ProviderRequestLimits:OpenAlex:Enabled"] = "false",
        ["ProviderRequestLimits:Crossref:Enabled"] = "false",
        ["ProviderRequestLimits:SemanticScholar:Enabled"] = "false"
    };

    private sealed class ProviderReplayHandler(BroaderReplayAudit audit) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri? uri = request.RequestUri;
            string path = uri?.AbsolutePath ?? string.Empty;
            bool exact = request.Method == HttpMethod.Get && uri?.Host == "pub.orcid.org";
            string? body = exact ? path switch
            {
                $"/v3.0/{PrimaryOrcid}/record" => RecordJson(PrimaryOrcid, 2),
                $"/v3.0/{PrimaryOrcid}/works/1,2" => PrimaryWorksJson(),
                $"/v3.0/{SecondaryOrcid}/record" => RecordJson(SecondaryOrcid, 1),
                $"/v3.0/{SecondaryOrcid}/works/1" => SecondaryWorksJson(),
                _ => null
            } : null;
            audit.AddProvider(request, body is not null);
            return Task.FromResult(new HttpResponseMessage(body is null
                ? HttpStatusCode.NotFound : HttpStatusCode.OK)
            {
                Content = new StringContent(body ?? "{}", Encoding.UTF8, "application/json")
            });
        }

        private static string RecordJson(string orcid, int works)
        {
            System.Text.Json.Nodes.JsonArray groups = [];
            foreach (int value in Enumerable.Range(1, works))
                groups.Add(new System.Text.Json.Nodes.JsonObject
                {
                    ["work-summary"] = new System.Text.Json.Nodes.JsonArray
                    {
                        new System.Text.Json.Nodes.JsonObject
                        { ["put-code"] = value, ["display-index"] = "1" }
                    }
                });
            System.Text.Json.Nodes.JsonObject root = new()
            {
                ["orcid-identifier"] = new System.Text.Json.Nodes.JsonObject { ["path"] = orcid },
                ["history"] = new System.Text.Json.Nodes.JsonObject
                    { ["last-modified-date"] = new System.Text.Json.Nodes.JsonObject { ["value"] = 1789300000000L } },
                ["person"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["name"] = new System.Text.Json.Nodes.JsonObject
                    {
                        ["given-names"] = new System.Text.Json.Nodes.JsonObject { ["value"] = "Synthetic" },
                        ["family-name"] = new System.Text.Json.Nodes.JsonObject { ["value"] = "Researcher" }
                    },
                    ["addresses"] = new System.Text.Json.Nodes.JsonObject { ["address"] = new System.Text.Json.Nodes.JsonArray() },
                    ["keywords"] = new System.Text.Json.Nodes.JsonObject { ["keyword"] = new System.Text.Json.Nodes.JsonArray() }
                },
                ["activities-summary"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["employments"] = new System.Text.Json.Nodes.JsonObject { ["affiliation-group"] = new System.Text.Json.Nodes.JsonArray() },
                    ["educations"] = new System.Text.Json.Nodes.JsonObject { ["affiliation-group"] = new System.Text.Json.Nodes.JsonArray() },
                    ["fundings"] = new System.Text.Json.Nodes.JsonObject { ["group"] = new System.Text.Json.Nodes.JsonArray() },
                    ["peer-reviews"] = new System.Text.Json.Nodes.JsonObject { ["group"] = new System.Text.Json.Nodes.JsonArray() },
                    ["works"] = new System.Text.Json.Nodes.JsonObject { ["group"] = groups }
                }
            };
            return root.ToJsonString();
        }

        private static string PrimaryWorksJson() => new System.Text.Json.Nodes.JsonObject
        {
            ["bulk"] = new System.Text.Json.Nodes.JsonArray
            {
                new System.Text.Json.Nodes.JsonObject { ["work"] = WorkNode(1, BroaderServiceAcceptancePreflight.Sources[0]) },
                new System.Text.Json.Nodes.JsonObject { ["work"] = WorkNode(2, BroaderServiceAcceptancePreflight.Sources[1]) }
            }
        }.ToJsonString();

        private static string SecondaryWorksJson() => new System.Text.Json.Nodes.JsonObject
        {
            ["bulk"] = new System.Text.Json.Nodes.JsonArray
            {
                new System.Text.Json.Nodes.JsonObject { ["work"] = WorkNode(1, BroaderServiceAcceptancePreflight.Sources[1]) }
            }
        }.ToJsonString();

        private static System.Text.Json.Nodes.JsonObject WorkNode(int putCode, SourceDefinition source) => new()
        {
            ["put-code"] = putCode,
            ["title"] = new System.Text.Json.Nodes.JsonObject
                { ["title"] = new System.Text.Json.Nodes.JsonObject { ["value"] = source.Title } },
            ["type"] = "journal-article",
            ["publication-date"] = new System.Text.Json.Nodes.JsonObject
                { ["year"] = new System.Text.Json.Nodes.JsonObject { ["value"] = source.Year.ToString() } },
            ["external-ids"] = new System.Text.Json.Nodes.JsonObject
            {
                ["external-id"] = new System.Text.Json.Nodes.JsonArray
                {
                    new System.Text.Json.Nodes.JsonObject
                        { ["external-id-type"] = "doi", ["external-id-value"] = source.Doi }
                }
            },
            ["url"] = new System.Text.Json.Nodes.JsonObject { ["value"] = source.SourceUrl },
            ["contributors"] = new System.Text.Json.Nodes.JsonObject { ["contributor"] = new System.Text.Json.Nodes.JsonArray() },
            ["short-description"] = "Public held-out acceptance article.",
            ["visibility"] = "public"
        };
    }

    private sealed class SourceReplayHandler(string sourceDirectory, BroaderReplayAudit audit)
        : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SourceDefinition? source = BroaderServiceAcceptancePreflight.Sources.SingleOrDefault(value =>
                request.Method == HttpMethod.Get && request.RequestUri is not null &&
                request.RequestUri.AbsoluteUri == value.SourceUrl);
            audit.AddSource(request, source is not null);
            if (source is null) return new(HttpStatusCode.NotFound);
            byte[] bytes = await File.ReadAllBytesAsync(
                Path.Combine(sourceDirectory, source.FileName), cancellationToken);
            string hash = BroaderServiceAcceptancePreflight.Hash(bytes);
            if (bytes.Length != source.ExpectedBytes || hash != source.ExpectedSha256)
                throw new InvalidOperationException("A frozen source copy changed during live execution.");
            ByteArrayContent content = new(bytes);
            content.Headers.ContentType = new("application/pdf");
            return new(HttpStatusCode.OK) { Content = content };
        }
    }
}

internal sealed class BroaderDispatchGate
{
    private readonly object gate = new();
    private readonly Dictionary<string, int> calls = new(StringComparer.Ordinal);
    private string phase = "initialized";

    public void SetPhase(string value, ServiceAcceptanceBudget budget)
    {
        lock (gate) phase = value;
        budget.SetPhase(value);
    }

    public void AuthorizeDispatch()
    {
        lock (gate)
        {
            if (!phase.StartsWith("faculty-", StringComparison.Ordinal)) return;
            int count = calls.GetValueOrDefault(phase);
            if (count >= 11)
                throw new ServiceAcceptanceBudgetException(
                    $"Faculty phase {phase} reached its hard eleven-call ceiling.");
            calls[phase] = count + 1;
        }
    }
}

internal sealed class BroaderDispatchHandler(BroaderDispatchGate gate) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        gate.AuthorizeDispatch();
        return base.SendAsync(request, cancellationToken);
    }
}

internal sealed class BroaderReplayAudit
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

    public object Snapshot() => new
    {
        providerServed = ProviderServed,
        sourceServed = SourceServed,
        providerTargets = provider.ToArray(),
        sourceTargets = source.ToArray()
    };
}

internal sealed class BroaderAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(BroaderAcceptanceHost.Header, out var value) ||
            value != BroaderAcceptanceHost.HeaderValue)
            return Task.FromResult(AuthenticateResult.NoResult());
        ClaimsIdentity identity = new(
            [new Claim(ClaimTypes.NameIdentifier, "broader-acceptance-actor")], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new(identity), Scheme.Name)));
    }
}

internal sealed class BroaderAcceptanceAccessService : IAcademicProductAccessService
{
    public Task<AcademicProductAccessGrant> AuthorizeAsync(ClaimsPrincipal principal,
        AcademicProductAccessRequest request, CancellationToken cancellationToken)
    {
        if (principal.Identity?.IsAuthenticated != true)
            throw new AcademicProductUnauthenticatedException();
        if (request.SubjectPersonelId != BroaderAcceptanceHost.PrimarySubjectId)
            throw new AcademicProductAccessDeniedException();
        return Task.FromResult(new AcademicProductAccessGrant("broader-acceptance-grant",
            "broader-acceptance-actor", BroaderAcceptanceHost.PrimarySubjectId, request.Operation));
    }

    public Task<AcademicProductAccessGrant> ReauthorizeAsync(
        AcademicProductAccessGrant persistedGrant, CancellationToken cancellationToken) =>
        persistedGrant.AuthorizationGrantId == "broader-acceptance-grant" &&
        persistedGrant.SubjectPersonelId == BroaderAcceptanceHost.PrimarySubjectId
            ? Task.FromResult(persistedGrant)
            : throw new AcademicProductAccessDeniedException();
}
