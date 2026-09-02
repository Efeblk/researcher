using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis.Collection;
using Microsoft.AspNetCore.Mvc;
using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Endpoints;

[Route("Services/AcademicPerformance/V1/Yoksis/[action]")]
public sealed class YoksisEndpoint : ServiceEndpoint
{
    [HttpPost]
    public Task<YoksisCollectResponse> Collect(
        YoksisCollectRequest request,
        [FromServices] YoksisCollectionHandler collectionHandler)
    {
        return collectionHandler.CollectAsync(request);
    }
}
