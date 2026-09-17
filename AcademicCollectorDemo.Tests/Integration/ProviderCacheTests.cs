using AcademicCollectorDemo.Tests.Infrastructure;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Collection;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AcademicCollectorDemo.Tests.Integration;

public sealed class ProviderCacheTests
{
    [Fact]
    public async Task CollectAsync_DisabledProviders_SkipHttpAndRetainSavedProfiles()
    {
        var settings = new Dictionary<string, string?>
        {
            ["ProviderRequestLimits:Orcid:Enabled"] = "false",
            ["ProviderRequestLimits:OpenAlex:Enabled"] = "false",
            ["ProviderRequestLimits:SearchApi:Enabled"] = "false",
            ["ProviderRequestLimits:WebOfScience:Enabled"] = "false",
            ["ProviderRequestLimits:Scopus:Enabled"] = "false",
            ["ProviderRequestLimits:TrDizin:Enabled"] = "false"
        };
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var httpHandler = new StubHttpHandler(_ =>
            throw new InvalidOperationException("Disabled provider made an HTTP request."));
        using var http = new HttpClient(httpHandler);
        var savedOrcid = new AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Orcid.OrcidProfile
        {
            LastUpdatedAt = DateTime.UtcNow.AddDays(-10), Works = []
        };
        var researcher = new Researcher
        {
            PersonelId = "disabled-providers",
            Orcid = "0000-0001-8560-7482",
            OrcidProfile = savedOrcid
        };
        var service = new ResearcherCollectionService(new(http, config), new(http, config),
            new(http, config), new(http, config),
            new AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.TrDizin.TrDizinClient(http, config),
            new(), new(), config, new(http, config));

        List<ProviderCollectionFeedback> feedback = await service.CollectAsync(researcher, new()
        {
            Orcid = researcher.Orcid,
            GoogleScholarId = "scholar-id",
            WebOfScienceResearcherId = "A-1009-2008",
            ScopusId = "12345678901"
        }, []);

        Assert.Equal(0, httpHandler.RequestCount);
        Assert.Same(savedOrcid, researcher.OrcidProfile);
        Assert.All(feedback, item =>
        {
            Assert.Equal("Skipped", item.Status);
            Assert.Contains(item.Reasons, reason => reason.Code == "Disabled");
        });
    }

    [Fact]
    public async Task CollectAsync_MixedEnablement_ContinuesEnabledProvider()
    {
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProviderRequestLimits:Orcid:Enabled"] = "false",
                ["ProviderRequestLimits:OpenAlex:Enabled"] = "false",
                ["ProviderRequestLimits:TrDizin:Enabled"] = "false",
                ["ProviderRequestLimits:WebOfScience:Enabled"] = "true",
                ["WebOfScience:ApiKey"] = "test-key",
                ["WebOfScience:DatabaseIds:0"] = "WOS"
            }).Build();
        var httpHandler = new StubHttpHandler(_ =>
            StubHttpHandler.Json("""{"metadata":{"total":0,"limit":50},"hits":[]}"""));
        using var http = new HttpClient(httpHandler);
        var service = new ResearcherCollectionService(new(http, config), new(http, config),
            new(http, config), new(http, config),
            new AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.TrDizin.TrDizinClient(http, config),
            new(), new(), config, new(http, config));

        List<ProviderCollectionFeedback> feedback = await service.CollectAsync(
            new() { PersonelId = "mixed-enablement" },
            new() { Orcid = "0000-0001-8560-7482", WebOfScienceResearcherId = "C-5899-2018" }, []);

        Assert.Equal(1, httpHandler.RequestCount);
        Assert.Equal("Skipped", feedback.Single(item => item.Provider == "ORCID").Status);
        Assert.DoesNotContain(feedback.Single(item => item.Provider == "Web of Science").Reasons,
            reason => reason.Code == "Disabled");
    }


    [Fact]
    public async Task FillResearcherAsync_MultipleDatabasesAndPages_StopsAtMetadataTotalAndLogsProgress()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WebOfScience:ApiKey"] = Guid.NewGuid().ToString("N"),
            ["WebOfScience:DatabaseIds:0"] = "WOS",
            ["WebOfScience:DatabaseIds:1"] = "WOK",
            ["WebOfScience:MaximumPages"] = "10"
        }).Build();
        List<(string Database, string Page)> requests = [];
        var handler = new StubHttpHandler(request =>
        {
            string database = GetQueryValue(request.RequestUri!, "db");
            string page = GetQueryValue(request.RequestUri!, "page");
            requests.Add((database, page));

            return StubHttpHandler.Json(
                $$"""{"metadata":{"total":51,"limit":50},"hits":[{"uid":"{{database}}:{{page}}","title":"Synthetic work {{database}} {{page}}","types":["Article"]}]}""");
        });
        using var http = new HttpClient(handler);
        var logger = new CollectingLogger<WebOfScienceClient>();
        var researcher = new Researcher { PersonelId = "test-" + Guid.NewGuid().ToString("N") };

        await new WebOfScienceClient(http, config, logger)
            .FillResearcherAsync(researcher, "A-1009-2008");

        Assert.Null(researcher.WebOfScienceResearcherId);
        Assert.Equal(4, handler.RequestCount);
        Assert.Contains(("WOS", "1"), requests);
        Assert.Contains(("WOS", "2"), requests);
        Assert.Contains(("WOK", "1"), requests);
        Assert.Contains(("WOK", "2"), requests);
        Assert.Contains(logger.Messages, message => message.Contains(
            "Web of Science WOS page 1 request started; configured maximum is 10 pages.",
            StringComparison.Ordinal));
        Assert.Contains(logger.Messages, message => message.Contains(
            "Web of Science WOK page 2 response received and parsed; provider total is 51, computed page count is 2, and configured maximum is 10.",
            StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("A-1009-2008", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CollectAsync_FreshConfiguredDatabaseCache_SkipsHttpRequests()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["WebOfScience:DatabaseIds:0"] = "WOS" }).Build();
        var handler = new StubHttpHandler(_ => throw new InvalidOperationException("Unexpected provider request"));
        using var http = new HttpClient(handler);
        var researcher = new Researcher
        {
            PersonelId = "test-" + Guid.NewGuid().ToString("N"),
            WebOfScienceResearcherId = "A-1009-2008",
            WebOfScienceProfile = new() { LastUpdatedAt = DateTime.UtcNow, DocumentPagesJson = "{\"WOS\":[{}]}", Works = [] }
        };
        var service = new ResearcherCollectionService(new(http, config), new(http, config), new(http, config),
            new(http, config), new AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.TrDizin.TrDizinClient(http, config), new(), new(), config);
        List<string> messages = [];
        List<ProviderCollectionFeedback> feedback = await service.CollectAsync(researcher,
            new() { WebOfScienceResearcherId = researcher.WebOfScienceResearcherId }, messages);
        Assert.Equal(0, handler.RequestCount);
        ProviderCollectionFeedback webOfScience = Assert.Single(feedback,
            item => item.Provider == "Web of Science");
        Assert.Equal("Cached", webOfScience.Status);
        Assert.Equal(0, webOfScience.RetrievedCount);
        Assert.Contains(webOfScience.Reasons, reason => reason.Code == "Cached");
        Assert.Contains(messages, message => message.StartsWith("[ÖNBELLEK]"));
    }

    [Fact]
    public async Task FillResearcherAsync_PageLimitReached_StopsAndPreservesExistingProfile()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WebOfScience:ApiKey"] = Guid.NewGuid().ToString("N"),
            ["WebOfScience:MaximumPages"] = "2"
        }).Build();
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{"metadata":{"total":10000,"limit":50},"hits":[]}"""));
        using var http = new HttpClient(handler);
        var previous = new WebOfScienceProfile { DocumentsCount = 5 };
        var researcher = new Researcher
        {
            PersonelId = "test-" + Guid.NewGuid().ToString("N"), WebOfScienceProfile = previous };
        ProviderCollectionException failure = await Assert.ThrowsAsync<ProviderCollectionException>(() =>
            new WebOfScienceClient(http, config).FillResearcherAsync(researcher, "A-1009-2008"));
        Assert.Equal("PageLimit", failure.Code);
        Assert.Equal(10000, failure.ExpectedCount);
        Assert.Equal(0, failure.RetrievedCount);
        Assert.Equal(2, handler.RequestCount);
        Assert.Same(previous, researcher.WebOfScienceProfile);
    }

    [Fact]
    public async Task CollectAsync_ValidResearcherIdWithNoPublications_ReportsNoPublications()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WebOfScience:ApiKey"] = Guid.NewGuid().ToString("N"),
            ["WebOfScience:DatabaseIds:0"] = "WOS"
        }).Build();
        using var http = new HttpClient(new StubHttpHandler(_ =>
            StubHttpHandler.Json("""{"metadata":{"total":0,"limit":50},"hits":[]}""")));
        var researcher = new Researcher { PersonelId = "wos-empty" };
        var service = new ResearcherCollectionService(new(http, config), new(http, config), new(http, config),
            new(http, config), new AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.TrDizin.TrDizinClient(http, config), new(), new(), config);

        List<ProviderCollectionFeedback> feedback = await service.CollectAsync(researcher,
            new() { WebOfScienceResearcherId = "C-5899-2018" }, []);

        ProviderCollectionFeedback webOfScience = Assert.Single(feedback,
            item => item.Provider == "Web of Science");
        Assert.Equal("Failed", webOfScience.Status);
        Assert.Contains(webOfScience.Reasons, reason => reason.Code == "NoPublications");
        Assert.DoesNotContain(webOfScience.Reasons, reason => reason.Code == "InvalidIdentifier");
    }

    [Fact]
    public async Task CollectAsync_WebOfScienceNotFoundResponse_DoesNotReportInvalidIdentifier()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WebOfScience:ApiKey"] = Guid.NewGuid().ToString("N"),
            ["WebOfScience:DatabaseIds:0"] = "WOS"
        }).Build();
        using var http = new HttpClient(new StubHttpHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)));
        var researcher = new Researcher { PersonelId = "wos-not-found" };
        var service = new ResearcherCollectionService(new(http, config), new(http, config), new(http, config),
            new(http, config), new AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.TrDizin.TrDizinClient(http, config), new(), new(), config);

        List<ProviderCollectionFeedback> feedback = await service.CollectAsync(researcher,
            new() { WebOfScienceResearcherId = "C-5899-2018" }, []);

        ProviderCollectionFeedback webOfScience = Assert.Single(feedback,
            item => item.Provider == "Web of Science");
        Assert.Equal("Failed", webOfScience.Status);
        Assert.Contains(webOfScience.Reasons, reason => reason.Code == "NotFound");
        Assert.DoesNotContain(webOfScience.Reasons, reason => reason.Code == "InvalidIdentifier");
    }

    private sealed class CollectingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }

    private static string GetQueryValue(Uri uri, string name)
    {
        return uri.Query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Split('=', 2))
            .Where(value => string.Equals(value[0], name, StringComparison.Ordinal))
            .Select(value => Uri.UnescapeDataString(value[1]))
            .Single();
    }
}
