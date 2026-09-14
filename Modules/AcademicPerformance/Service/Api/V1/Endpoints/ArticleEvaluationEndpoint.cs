using System.Text.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Evaluations;
using Microsoft.AspNetCore.Mvc;
using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Endpoints;

[Route("Services/AcademicPerformance/V1/[action]")]
public sealed class ArticleEvaluationEndpoint : ServiceEndpoint
{
    [HttpPost]
    public async Task<ActionResult<StartArticleEvaluationResponse>> StartArticleEvaluation(
        [FromBody] StartArticleEvaluationRequest request,
        [FromServices] ArticleEvaluationScheduler scheduler,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        try
        {
            StartArticleEvaluationResponse response = await scheduler.EnqueueAsync(request, cancellationToken);
            return StatusCode(StatusCodes.Status202Accepted, response);
        }
        catch (ArticleEvaluationValidationException exception)
        {
            return UnprocessableEntity(new { Message = exception.Message });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { Message = "Evaluation profile preflight is unavailable; no run was enqueued." });
        }
        catch (JsonException)
        {
            return StatusCode(StatusCodes.Status502BadGateway,
                new { Message = "Evaluation profile metadata was unusable; no run was enqueued." });
        }
    }

    [HttpPost]
    public async Task<ActionResult<AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts.ArticleEvaluationResponse>> GetArticleEvaluation(
        [FromBody] GetArticleEvaluationRequest request,
        [FromServices] ArticleEvaluationReadService reader,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || request.RunId == Guid.Empty) return BadRequest(ModelState);
        AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts.ArticleEvaluationResponse? response =
            await reader.GetAsync(request, cancellationToken);
        return response is null
            ? NotFound(new { Message = "No accessible article evaluation run was found." })
            : Ok(response);
    }
}
