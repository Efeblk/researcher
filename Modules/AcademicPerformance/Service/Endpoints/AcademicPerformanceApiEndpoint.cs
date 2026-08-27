using AcademicCollectorDemo.Modules.AcademicPerformance.Application;
using AcademicCollectorDemo.Modules.AcademicPerformance.Contracts.V1;
using Microsoft.AspNetCore.Mvc;
using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Endpoints;

[Route("Services/AcademicPerformance/V1/[action]")]
public sealed class AcademicPerformanceApiEndpoint : ServiceEndpoint
{
    [HttpPost]
    public Task<AcademicDataResponse> Collect(
        AcademicDataCollectRequest request,
        [FromServices] IAcademicPerformanceApplicationService applicationService)
    {
        return applicationService.CollectAsync(request);
    }

    [HttpPost]
    public Task<AcademicDataResponse> GetResearcher(
        AcademicResearcherRequest request,
        [FromServices] IAcademicPerformanceApplicationService applicationService)
    {
        return applicationService.GetResearcherAsync(request);
    }

    [HttpPost]
    public Task<AcademicPublicationListResponse> ListPublications(
        AcademicPublicationListRequest request,
        [FromServices] IAcademicPerformanceApplicationService applicationService)
    {
        return applicationService.ListPublicationsAsync(request);
    }

    [HttpPost]
    public Task<AcademicPublicationSelectionResponse> SavePublicationSelections(
        AcademicPublicationSelectionRequest request,
        [FromServices] IAcademicPerformanceApplicationService applicationService)
    {
        return applicationService.SavePublicationSelectionsAsync(request);
    }
}
