using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Status;
using Microsoft.AspNetCore.Mvc;
using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Endpoints;

[Route("Services/AcademicPerformance/V1/[action]")]
public sealed class ProviderStatusEndpoint : ServiceEndpoint
{
    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ProviderStatusSummaryResponse> ProviderStatus(
        [FromServices] ProviderStatusService service, CancellationToken cancellationToken)
    {
        ProviderStatusResponse response = await service.GetAsync(cancellationToken);
        return ProviderStatusSummaryMapper.Map(response, DateTime.UtcNow);
    }
}
