using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;
using Microsoft.AspNetCore.Mvc;
using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Endpoints;

[Route("Services/AcademicPerformance/Researcher/[action]")]
public sealed class ResearcherEndpoint : ServiceEndpoint
{
    [HttpPost]
    public Task<ResearcherCollectResponse> Collect(
        ResearcherCollectRequest request,
        [FromServices] ResearcherCollectionHandler handler)
    {
        return handler.CollectAsync(request);
    }

    [HttpPost]
    public async Task<ContentResult> CollectText(
        ResearcherCollectRequest request,
        [FromServices] ResearcherCollectionHandler handler)
    {
        ResearcherCollectResponse? response = null;
        string? feedbackText = null;

        response = await handler.CollectAsync(request);
        feedbackText = string.Join(Environment.NewLine, response.Messages);

        return Content(feedbackText, "text/plain; charset=utf-8");
    }

    [HttpPost]
    public async Task<ResearcherRandomResponse> Random(
        ServiceRequest request,
        [FromServices] AcademicDatabaseInitializer databaseInitializer,
        [FromServices] DatabaseMaintenance databaseMaintenance,
        [FromServices] ResearcherSummaryFactory summaryFactory)
    {
        ResearcherRandomResponse? response = null;
        Researcher? researcher = null;

        await databaseInitializer.EnsureReadyAsync();
        researcher = await databaseMaintenance.GetRandomResearcherAsync();
        response = new ResearcherRandomResponse();
        response.Researcher = summaryFactory.Create(researcher);

        return response;
    }
}
