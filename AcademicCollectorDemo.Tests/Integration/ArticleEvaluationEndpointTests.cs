using System.Net;
using System.Net.Http.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Tests.Infrastructure;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class ArticleEvaluationEndpointTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task StartCalibration_Unauthenticated_DoesNotContactAnalysisService()
    {
        await using FakeArticleEvaluationServer analysis = await FakeArticleEvaluationServer.StartAsync();
        using HostProcess host = new(fixture.ConnectionString, analysis.BaseUrl,
            articleEvaluationWorkerEnabled: true, articleEvaluationPollSeconds: 1);
        await host.WaitUntilReadyAsync();

        using HttpResponseMessage start = await host.Client.PostAsJsonAsync(
            "/Services/AcademicPerformance/V1/StartArticleEvaluation",
            new StartArticleEvaluationRequest { PersonelId = "subject", ProfileIds = ["fake-profile"] });

        Assert.Equal(HttpStatusCode.Unauthorized, start.StatusCode);
        Assert.Equal(0, analysis.ExecuteCalls);
    }
}
