using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis;
using Microsoft.AspNetCore.Mvc;
using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Endpoints;

[Route("Services/AcademicPerformance/Yoksis/[action]")]
public sealed class YoksisEndpoint : ServiceEndpoint
{
    [HttpPost]
    public Task<YoksisCollectResponse> Collect(
        YoksisCollectRequest request,
        [FromServices] YoksisCollectionService collectionService)
    {
        return collectionService.CollectAsync(request);
    }
}
