using System.Text.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;
using Microsoft.AspNetCore.Mvc;
using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Endpoints;

[Route("Services/AcademicPerformance/V1/[action]")]
public sealed class ArticleSummaryEndpoint : ServiceEndpoint
{
    [HttpPost]
    public async Task<ActionResult<SavedArticleSummaryResponse>> SummarizeArticle([FromBody] ArticleSummaryRequest request,
        [FromServices] ArticleSummaryWorkflow workflow, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        try
        {
            SavedArticleSummaryResponse? result = await workflow.SummarizeAsync(request.PersonelID.Trim(), request.AcademicWorkId, request.Language, cancellationToken);
            return result is null ? NotFound(new { Message = "Article was not found for this researcher." }) : Ok(result);
        }
        catch (ArticleSourceException exception) { return UnprocessableEntity(new { Message = exception.Message }); }
        catch (HttpRequestException exception) { return StatusCode(exception.StatusCode is null ? 503 : 502, new { Message = "Article summary generation failed; no report was saved." }); }
        catch (JsonException) { return StatusCode(502, new { Message = "The analysis service returned an unusable article report; no report was saved." }); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return StatusCode(504, new { Message = "Article summary generation timed out; no report was saved." }); }
    }

    [HttpPost]
    public async Task<ActionResult<SavedArticleSummaryResponse>> GetArticleSummary([FromBody] ArticleSummaryRequest request,
        [FromServices] ArticleSummaryWorkflow workflow, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        SavedArticleSummaryResponse? result = await workflow.GetLatestAsync(request.PersonelID.Trim(), request.AcademicWorkId, cancellationToken);
        return result is null ? NotFound(new { Message = "No saved summary exists for this article and researcher." }) : Ok(result);
    }
}
