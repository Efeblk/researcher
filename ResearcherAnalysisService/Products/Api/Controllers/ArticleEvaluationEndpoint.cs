using System.Text.Json;
using ResearcherAnalysisService.Products.Api.Contracts;
using ResearcherAnalysisService.Products.Evaluations;
using Microsoft.AspNetCore.Mvc;
using ResearcherAnalysisService.Api;
using ResearcherAnalysisService.Products.ProductAccess;

namespace ResearcherAnalysisService.Products.Api.Controllers;

[ApiController]
[ResearcherAnalysisService.Products.Api.ProductJsonContract]
[ServiceFilter<AnalysisAccessFilter>]
[Route("api/v1")]
public sealed class ArticleEvaluationEndpoint : ControllerBase
{
    [HttpPost("evaluations/start")]
    public async Task<ActionResult<StartArticleEvaluationResponse>> StartArticleEvaluation(
        [FromBody] StartArticleEvaluationRequest request,
        [FromServices] IAcademicProductAccessService access,
        [FromServices] ArticleEvaluationScheduler scheduler,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        try
        {
            AcademicProductAccessGrant grant = await access.AuthorizeAsync(User,
                new(AcademicProductOperation.ArticleEvaluationStart, request.PersonelId.Trim()),
                cancellationToken);
            StartArticleEvaluationResponse response = await scheduler.EnqueueAsync(grant, request, cancellationToken);
            return StatusCode(StatusCodes.Status202Accepted, response);
        }
        catch (ArticleEvaluationValidationException exception)
        {
            return UnprocessableEntity(new { Message = exception.Message });
        }
        catch (Exception exception) when (IsAccessFailure(exception))
        {
            return AcademicProductEndpoint.Error(exception);
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

    [HttpPost("evaluations/status")]
    public async Task<ActionResult<ResearcherAnalysisService.Products.Api.Contracts.ArticleEvaluationResponse>> GetArticleEvaluation(
        [FromBody] GetArticleEvaluationRequest request,
        [FromServices] IAcademicProductAccessService access,
        [FromServices] ArticleEvaluationReadService reader,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || request.RunId == Guid.Empty) return BadRequest(ModelState);
        try
        {
            AcademicProductAccessGrant grant = await access.AuthorizeAsync(User,
                new(AcademicProductOperation.ArticleEvaluationRead, request.PersonelId.Trim()),
                cancellationToken);
            ResearcherAnalysisService.Products.Api.Contracts.ArticleEvaluationResponse? response =
                await reader.GetAsync(grant, request, cancellationToken);
            return response is null
                ? NotFound(new { Message = "No accessible article evaluation run was found." })
                : Ok(response);
        }
        catch (Exception exception) when (IsAccessFailure(exception))
        {
            return AcademicProductEndpoint.Error(exception);
        }
    }

    private static bool IsAccessFailure(Exception exception) => exception is
        AcademicProductAccessUnavailableException or AcademicProductUnauthenticatedException or
        AcademicProductAccessDeniedException;
}
