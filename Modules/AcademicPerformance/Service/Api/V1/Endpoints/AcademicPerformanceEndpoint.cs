using AcademicCollectorDemo.Modules.AcademicPerformance.Application;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using Microsoft.AspNetCore.Mvc;
using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Endpoints;

[Route("Services/AcademicPerformance/V1/[action]")]
public sealed class AcademicPerformanceEndpoint : ServiceEndpoint
{
    [HttpPost]
    public async Task<AcademicDataResponse> Collect(
        AcademicDataCollectRequest request,
        [FromServices] IAcademicPerformanceApplicationService applicationService)
    {
        try
        {
            return await applicationService.CollectAsync(request);
        }
        catch (ArgumentException exception)
        {
            throw new ValidationError("ValidationError", exception.Message);
        }
    }

    [HttpPost]
    public async Task<ResearcherMetricsResponse> RecalculateMetrics(
        ResearcherMetricsRequest request,
        [FromServices] IAcademicPerformanceApplicationService applicationService,
        CancellationToken cancellationToken)
    {
        try
        {
            return await applicationService.RecalculateMetricsAsync(request, cancellationToken);
        }
        catch (ArgumentException exception)
        {
            throw new ValidationError(exception.Message);
        }
    }

    [HttpPost]
    public async Task<AcademicDataResponse> GetResearcher(
        AcademicResearcherRequest request,
        [FromServices] IAcademicPerformanceApplicationService applicationService)
    {
        try
        {
            return await applicationService.GetResearcherAsync(request);
        }
        catch (ArgumentException exception)
        {
            throw new ValidationError(exception.Message);
        }
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

    [HttpPost]
    public Task<CanonicalPublicationListResponse> ListCanonicalPublications(
        CanonicalPublicationListRequest request,
        [FromServices] IAcademicPerformanceApplicationService applicationService,
        CancellationToken cancellationToken)
    {
        return applicationService.ListCanonicalPublicationsAsync(request, cancellationToken);
    }

}
