using System.Net;
using System.Net.Http.Json;
using ResearcherAnalysisService.Products.Api.Contracts;
using ResearcherAnalysisService.Tests.Infrastructure;

namespace ResearcherAnalysisService.Tests;

[Collection("Analysis Product SQL Server")]
public sealed class ArticleEvaluationEndpointTests(AnalysisProductSqlServerFixture fixture)
{
    [Fact]
    public async Task StartCalibration_Unauthenticated_DoesNotContactAnalysisService()
    {
        await using FakeArticleEvaluationServer analysis = await FakeArticleEvaluationServer.StartAsync();
        using HostProcess host = new(fixture.ConnectionString, analysis.BaseUrl,
            articleEvaluationWorkerEnabled: true, articleEvaluationPollSeconds: 1);
        await host.WaitUntilReadyAsync();

        using HttpResponseMessage start = await host.Client.PostAsJsonAsync(
            "/api/v1/products/StartArticleEvaluation",
            new StartArticleEvaluationRequest { PersonelId = "subject", ProfileIds = ["fake-profile"] });

        Assert.Equal(HttpStatusCode.Unauthorized, start.StatusCode);
        Assert.Equal(0, analysis.ExecuteCalls);
    }
}
