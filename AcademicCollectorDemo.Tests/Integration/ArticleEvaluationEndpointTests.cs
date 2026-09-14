using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Evaluations;
using AcademicCollectorDemo.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class ArticleEvaluationEndpointTests(SqlServerFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task StartCalibration_Returns202AndWorkerPersistsWithoutGetTriggeringCalls()
    {
        await using FakeArticleEvaluationServer analysis = await FakeArticleEvaluationServer.StartAsync();
        using HostProcess host = new(fixture.ConnectionString, analysis.BaseUrl,
            articleEvaluationWorkerEnabled: true, articleEvaluationPollSeconds: 1);
        await host.WaitUntilReadyAsync();

        using HttpResponseMessage start = await host.Client.PostAsJsonAsync(
            "/Services/AcademicPerformance/V1/StartArticleEvaluation",
            new StartArticleEvaluationRequest { ProfileIds = ["fake-profile"] });

        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        StartArticleEvaluationResponse accepted = (await start.Content
            .ReadFromJsonAsync<StartArticleEvaluationResponse>())!;
        Assert.Equal(6, accepted.TotalCases);
        Assert.Equal(6, accepted.TotalWorkItems);
        Assert.Equal(6, accepted.WorstCaseModelCalls);
        ArticleEvaluationResponse? result = null;
        for (int poll = 0; poll < 40; poll++)
        {
            using HttpResponseMessage response = await host.Client.PostAsJsonAsync(
                "/Services/AcademicPerformance/V1/GetArticleEvaluation",
                new GetArticleEvaluationRequest { RunId = accepted.RunId });
            response.EnsureSuccessStatusCode();
            result = await response.Content.ReadFromJsonAsync<ArticleEvaluationResponse>();
            if (result!.Status == "Completed") break;
            await Task.Delay(250);
        }
        Assert.NotNull(result);
        Assert.Equal("Completed", result.Status);
        Assert.Equal(6, result.CompletedWorkItems);
        Assert.Equal("actual-synthetic-model-revision",
            Assert.Single(result.WorkItems.Select(value => value.ActualModelIdentity).Distinct()));
        Assert.Equal(18, result.Aggregate.ScheduledClaims);
        Assert.Equal(0, result.Aggregate.PendingClaims);
        Assert.Null(result.Aggregate.ScientificAccuracy);
        Assert.Null(result.Aggregate.RealArticleOmissionRecall);
        Assert.Single(result.ProfileAggregates);
        Assert.Equal(6, analysis.ExecuteCalls);

        using (IServiceScope scope = fixture.Services.CreateScope())
        {
            AcademicDbContext database = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            string requestJson = await database.ArticleEvaluationAttempts.AsNoTracking()
                .Where(value => value.WorkItem!.Case!.Run!.RunId == accepted.RunId &&
                    value.WorkItem.Case.CaseId == "en-injection")
                .Select(value => value.RequestJson).SingleAsync();
            AcademicCollector.Analysis.Contracts.ArticleEvaluationRequest outbound = JsonSerializer.Deserialize<
                AcademicCollector.Analysis.Contracts.ArticleEvaluationRequest>(requestJson, JsonOptions)!;
            Assert.Equal(3, outbound.CalibrationClaims!.Count);
            Assert.All(outbound.CalibrationClaims, value => Assert.Equal("data", value.Section));
            Assert.DoesNotContain("expectedVerdicts", requestJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("derivation", requestJson, StringComparison.OrdinalIgnoreCase);
        }

        using HttpResponseMessage stored = await host.Client.PostAsJsonAsync(
            "/Services/AcademicPerformance/V1/GetArticleEvaluation",
            new GetArticleEvaluationRequest { RunId = accepted.RunId });
        stored.EnsureSuccessStatusCode();
        await Task.Delay(100);
        Assert.Equal(6, analysis.ExecuteCalls);
    }
}
