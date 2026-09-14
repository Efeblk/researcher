using AcademicCollector.Analysis.Contracts;
using Microsoft.AspNetCore.Mvc;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Api;

namespace ResearcherAnalysisService.Api.V1;

[ApiController, Route("api/v1/evaluations")]
public sealed class ArticleEvaluationController : ControllerBase
{
    [HttpGet("profiles"), ServiceFilter<AnalysisAccessFilter>]
    public ActionResult<ArticleEvaluationProfilesResponse> Profiles(
        [FromServices] ArticleEvaluationService service) => Ok(service.GetProfiles());

    [HttpPost("execute"), ServiceFilter<AnalysisAccessFilter>]
    public async Task<ActionResult<ArticleEvaluationResponse>> Execute(
        [FromBody] ArticleEvaluationRequest request,
        [FromServices] ArticleEvaluationService service,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.ExecuteAsync(request, cancellationToken));
        }
        catch (ArticleEvaluationRequestException exception)
        {
            ProblemDetails problem = new()
            {
                Status = exception.StatusCode,
                Title = exception.Message
            };
            problem.Extensions["errorCode"] = exception.ErrorCode;
            return StatusCode(exception.StatusCode, problem);
        }
    }
}
