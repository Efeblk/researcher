using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Collection;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using Microsoft.Extensions.Configuration;

namespace AcademicCollectorDemo.Tests;

public sealed class ProviderCacheTests
{
    [Fact]
    public async Task CollectAsync_FreshConfiguredDatabaseCache_SkipsHttpRequests()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["WebOfScience:DatabaseIds:0"] = "WOS" }).Build();
        var handler = new StubHttpHandler(_ => throw new InvalidOperationException("Unexpected provider request"));
        using var http = new HttpClient(handler);
        var researcher = new Researcher
        {
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
        var researcher = new Researcher { WebOfScienceProfile = previous };
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            new WebOfScienceClient(http, config).FillResearcherAsync(researcher, "A-1009-2008"));
        Assert.Equal(2, handler.RequestCount);
        Assert.Same(previous, researcher.WebOfScienceProfile);
    }
}
