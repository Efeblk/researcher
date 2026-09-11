using Microsoft.AspNetCore.Mvc;
using ResearcherAnalysisService.Api;
using ResearcherAnalysisService.Integrations.Gemini;

namespace ResearcherAnalysisService.Api.V1;

[ApiController, Route("api/v1/internal/provider-status")]
public sealed class ProviderStatusController : ControllerBase
{
    [HttpGet("gemini"), ServiceFilter<AnalysisAccessFilter>]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<ProviderStatusResponse>> Gemini(
        [FromServices] GeminiArticleClient client, CancellationToken cancellationToken)
    {
        GeminiProviderStatus status = await client.GetStatusAsync(cancellationToken);
        return Ok(new ProviderStatusResponse("Gemini", status.Health, []));
    }
}
