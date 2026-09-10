using AcademicCollectorDemo.Modules.AcademicPerformance.Application;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis.Collection;
using AcademicCollectorDemo.Modules.AcademicPerformance.WebClient.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.WebClient.Identity;
using Microsoft.AspNetCore.Mvc;
using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.WebClient.Endpoints;

[Route("Services/AcademicPerformance/ResearcherCollection/[action]")]
public sealed class ResearcherCollectionEndpoint : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<AcademicDataResponse>> Collect(
        ResearcherCollectionRequest request,
        [FromServices] ICurrentPersonnelResolver currentPersonnel,
        [FromServices] IAcademicPerformanceApplicationService applicationService)
    {
        if (!CurrentPersonnelHttpResult.TryResolve(currentPersonnel, out string personelId, out ActionResult? error))
            return error!;

        AcademicDataResponse response = await applicationService.CollectAsync(new AcademicDataCollectRequest
        {
            PersonelId = personelId,
            Orcid = request.Orcid,
            GoogleScholarId = request.GoogleScholarId,
            WebOfScienceResearcherId = request.WebOfScienceResearcherId
        });
        response.Messages = response.Messages
            .Select(message => System.Text.RegularExpressions.Regex.Replace(
                message,
                @"\s*\(PersonelID:\s*[^)]*\)\.?$",
                "."))
            .ToList();
        return response;
    }

    [HttpPost]
    public async Task<ActionResult<YoksisCollectResponse>> CollectYoksis(
        YoksisCollectionRequest request,
        [FromServices] ICurrentPersonnelResolver currentPersonnel,
        [FromServices] YoksisCollectionHandler collectionHandler)
    {
        if (!CurrentPersonnelHttpResult.TryResolve(currentPersonnel, out string personelId, out ActionResult? error))
            return error!;

        return await collectionHandler.CollectAsync(new YoksisCollectRequest
        {
            PersonelId = personelId,
            TcKimlikNo = request.TcKimlikNo,
            UpdatedAfter = request.UpdatedAfter,
            IncludeRecords = request.IncludeRecords,
            IncludeRawResponses = request.IncludeRawResponses
        });
    }

}
