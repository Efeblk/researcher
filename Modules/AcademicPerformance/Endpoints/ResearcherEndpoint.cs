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
    public async Task<ResearcherRandomResponse> Random(
        ServiceRequest request,
        [FromServices] DatabaseMaintenance databaseMaintenance,
        [FromServices] ResearcherSummaryFactory summaryFactory)
    {
        ResearcherRandomResponse? response = null;
        Researcher? researcher = null;

        researcher = await databaseMaintenance.GetRandomResearcherAsync();
        response = new ResearcherRandomResponse();
        response.Researcher = summaryFactory.Create(researcher);

        return response;
    }
}
