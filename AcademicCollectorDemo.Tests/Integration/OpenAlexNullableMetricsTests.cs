using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Tests.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace AcademicCollectorDemo.Tests.Integration;

public sealed class OpenAlexNullableMetricsTests
{
    [Fact]
    public async Task FillResearcherAsync_MissingAuthorTotalsRemainUnknown_WhileExplicitZeroIsPreserved()
    {
        string suffix = Guid.NewGuid().ToString("N");
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["OpenAlex:ApiBaseUrl"] = "https://openalex.test"
            }).Build();
        Researcher missing = new() { Orcid = "0000-0000-0000-0001" };
        using (HttpClient http = new(new StubHttpHandler(request =>
            StubHttpHandler.Json(request.RequestUri!.AbsolutePath.EndsWith("/authors")
                ? $"{{\"results\":[{{\"id\":\"https://openalex.org/A{suffix}\",\"summary_stats\":{{}}}}]}}"
                : "{\"results\":[],\"meta\":{\"next_cursor\":null}}"))))
        {
            await new OpenAlexClient(http, configuration).FillResearcherAsync(missing);
        }

        Assert.Null(missing.OpenAlexProfile!.WorksCount);
        Assert.Null(missing.OpenAlexProfile.CitedByCount);

        Researcher zero = new() { Orcid = "0000-0000-0000-0002" };
        using (HttpClient http = new(new StubHttpHandler(request =>
            StubHttpHandler.Json(request.RequestUri!.AbsolutePath.EndsWith("/authors")
                ? $"{{\"results\":[{{\"id\":\"https://openalex.org/A0{suffix}\",\"works_count\":0,\"cited_by_count\":0,\"summary_stats\":{{\"h_index\":0,\"i10_index\":0}}}}]}}"
                : "{\"results\":[],\"meta\":{\"next_cursor\":null}}"))))
        {
            await new OpenAlexClient(http, configuration).FillResearcherAsync(zero);
        }

        Assert.Equal(0, zero.OpenAlexProfile!.WorksCount);
        Assert.Equal(0, zero.OpenAlexProfile.CitedByCount);
    }
}
