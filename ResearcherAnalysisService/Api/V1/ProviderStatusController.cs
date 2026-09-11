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
        [FromServices] GeminiArticleClient client, [FromServices] IGeminiUsageRepository usageRepository,
        CancellationToken cancellationToken)
    {
        Task<GeminiProviderStatus> statusTask = client.GetStatusAsync(cancellationToken);
        Task<GeminiSpendingStatus> spendingTask = usageRepository.GetSpendingAsync(cancellationToken);
        await Task.WhenAll(statusTask, spendingTask);
        GeminiProviderStatus status = await statusTask;
        GeminiSpendingStatus spending = await spendingTask;
        return Ok(new ProviderStatusResponse("Gemini", status.Health, [], new(
            spending.Available, spending.Currency, spending.Kind, spending.Since, spending.RequestCount,
            spending.UnknownCount, spending.EstimatedTotalUsd, spending.Last3.Select(item =>
                new ProviderSpendingItemResponse(item.At, item.Model, item.EstimatedUsd)).ToList())));
    }
}
