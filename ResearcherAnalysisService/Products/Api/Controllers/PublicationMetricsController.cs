using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using ResearcherAnalysisService.Api;
using ResearcherAnalysisService.Products.Api.Contracts;
using ResearcherAnalysisService.Products.Metrics;

namespace ResearcherAnalysisService.Products.Api.Controllers;

[ApiController]
[ServiceFilter<AnalysisAccessFilter>]
[Route("api/v1/products/[action]")]
public sealed class PublicationMetricsController : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ResearcherPublicationMetricsStatusResponse>> GetResearcherPublicationMetrics(
        [FromBody] JsonElement requestBody,
        [FromServices] PublicationMetricsReadService readService,
        CancellationToken cancellationToken)
    {
        if (!TryReadPersonelId(requestBody, out string personelId))
            return BadRequest(new { Message = "PersonelID is required and must be at most 200 characters." });
        ResearcherPublicationMetricsStatusResponse? result = await readService.GetAsync(personelId, cancellationToken);
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
        return Accepted((await readService.GetAsync(personelId, cancellationToken))!);
    }

    private static bool TryReadPersonelId(JsonElement requestBody, out string personelId)
    {
        personelId = string.Empty;
        try
        {
            ResearcherPublicationMetricsRequest? request = requestBody.Deserialize<ResearcherPublicationMetricsRequest>(
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (requestBody.ValueKind != JsonValueKind.Object || request is null ||
                string.IsNullOrWhiteSpace(request.PersonelId))
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
