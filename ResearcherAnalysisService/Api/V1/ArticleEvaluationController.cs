using AcademicCollector.Analysis.Contracts;
using Microsoft.AspNetCore.Mvc;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Api;

namespace ResearcherAnalysisService.Api.V1;

[ApiController, Route("api/v1/evaluations")]
public sealed class ArticleEvaluationController : ControllerBase
{
    [HttpGet("profiles")]
    public ActionResult<ArticleEvaluationProfilesResponse> Profiles(
        [FromServices] ArticleEvaluationService service) => Ok(service.GetProfiles());

}
