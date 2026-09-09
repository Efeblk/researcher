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
            new(http, config), new(), new(), config);
        List<string> messages = [];
        await service.CollectAsync(researcher, new() { WebOfScienceResearcherId = researcher.WebOfScienceResearcherId }, messages);
        Assert.Equal(0, handler.RequestCount);
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
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            new WebOfScienceClient(http, config).FillResearcherAsync(researcher, "A-1009-2008"));
        Assert.Equal(2, handler.RequestCount);
        Assert.Same(previous, researcher.WebOfScienceProfile);
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
