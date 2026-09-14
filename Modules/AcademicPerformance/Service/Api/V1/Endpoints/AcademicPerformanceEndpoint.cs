using AcademicCollectorDemo.Modules.AcademicPerformance.Application;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Metrics;
using Microsoft.AspNetCore.Mvc;
using Serenity.Services;
using System.Text.Json;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Endpoints;

[Route("Services/AcademicPerformance/V1/[action]")]
public sealed class AcademicPerformanceEndpoint : ServiceEndpoint
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

    [HttpPost]
    public Task<CanonicalPublicationListResponse> ListCanonicalPublications(
        CanonicalPublicationListRequest request,
        [FromServices] IAcademicPerformanceApplicationService applicationService,
        CancellationToken cancellationToken)
    {
        return applicationService.ListCanonicalPublicationsAsync(request, cancellationToken);
    }

    [HttpPost]
    public Task<CanonicalPublicationRebuildResponse> RebuildCanonicalPublications(
        CanonicalPublicationRebuildRequest request,
        [FromServices] IAcademicPerformanceApplicationService applicationService,
        CancellationToken cancellationToken)
    {
        return applicationService.RebuildCanonicalPublicationsAsync(request, cancellationToken);
    }

    [HttpPost]
    public async Task<ActionResult<ResearcherPublicationMetricsStatusResponse>> GetResearcherPublicationMetrics(
        [FromBody] JsonElement requestBody,
        [FromServices] PublicationMetricsReadService readService,
        CancellationToken cancellationToken)
    {
        if (!TryReadPersonelId(requestBody, out string personelId))
            return BadRequest(new { Message = "PersonelID is required and must be at most 200 characters." });

        ResearcherPublicationMetricsStatusResponse? result = await readService.GetAsync(
            personelId, cancellationToken);
        if (result is null)
            return NotFound(new { Message = "Researcher was not found." });
        return result.Data is null ? Accepted(result) : Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<ResearcherPublicationMetricsStatusResponse>> RefreshResearcherPublicationMetrics(
        [FromBody] JsonElement requestBody,
        [FromServices] PublicationMetricsRefreshService refreshService,
        [FromServices] PublicationMetricsReadService readService,
        CancellationToken cancellationToken)
    {
        if (!TryReadPersonelId(requestBody, out string personelId))
            return BadRequest(new { Message = "PersonelID is required and must be at most 200 characters." });

        if (!await refreshService.ScheduleAsync(personelId, cancellationToken))
            return NotFound(new { Message = "Researcher was not found." });
        ResearcherPublicationMetricsStatusResponse result = (await readService.GetAsync(
            personelId, cancellationToken))!;
        return Accepted(result);
    }

    private static bool TryReadPersonelId(JsonElement requestBody, out string personelId)
    {
        personelId = string.Empty;
        if (requestBody.ValueKind != JsonValueKind.Object)
            return false;
        try
        {
            ResearcherPublicationMetricsRequest? request =
                requestBody.Deserialize<ResearcherPublicationMetricsRequest>(
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (request is null || string.IsNullOrWhiteSpace(request.PersonelId))
                return false;
            personelId = request.PersonelId.Trim();
            return personelId.Length <= 200;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
