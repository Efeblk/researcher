using System.Text.Json;
using AcademicCollector.Analysis.Contracts;
using ResearcherAnalysisService.Products.Analysis;
using ResearcherAnalysisService.Products.Api.Contracts;
using Microsoft.AspNetCore.Mvc;
using ResearcherAnalysisService.Api;

namespace ResearcherAnalysisService.Products.Api.Controllers;

[ApiController]
[ResearcherAnalysisService.Products.Api.ProductJsonContract]
[Route("api/v1")]
public sealed class ResearcherAnalysisEndpoint : ControllerBase
{
    [HttpPost("researchers/analysis/generate")]
    public async Task<ActionResult<SavedResearcherAnalysisResponse>> AnalyzeResearcher(
        [FromBody] ResearcherAnalysisIdRequest? request,
        [FromServices] ResearcherAnalysisWorkflow workflow, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || request is null || string.IsNullOrWhiteSpace(request.PersonelId))
            return BadRequest(new { Message = "PersonelID is required." });
        try
        {
            SavedResearcherAnalysisResponse? result = await workflow.AnalyzeAsync(request.PersonelId.Trim(), request.SnapshotAt, cancellationToken);
            return result is null ? NotFound(new { Message = "Researcher not found." }) : Ok(result);
        }
        catch (AnalysisInputUnavailableException exception)
        {
            return UnprocessableEntity(new { Message = exception.Message });
        }
        catch (HttpRequestException exception)
        {
            // Do not expose upstream bodies, research text, or service credentials.
            return StatusCode(exception.StatusCode is null ? 503 : 502,
                new { Message = "Analysis failed; no report was saved.", AnalysisServiceStatus = (int?)exception.StatusCode });
        }
        catch (JsonException)
        {
            return StatusCode(502, new { Message = "The analysis service returned an unusable report; no report was saved." });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return StatusCode(504, new { Message = "Analysis timed out; no report was saved." });
        }
    }

    [HttpPost("researchers/analysis")]
    public async Task<ActionResult<ResearcherAnalysisReadResponse>> GetResearcherAnalysis(
        [FromBody] ResearcherAnalysisIdRequest? request,
        [FromServices] ResearcherAnalysisWorkflow workflow, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || request is null || string.IsNullOrWhiteSpace(request.PersonelId))
            return BadRequest(new { Message = "PersonelID is required." });
        ResearcherAnalysisReadResponse? result = await workflow.GetAnalysisAsync(request.PersonelId.Trim(), cancellationToken);
        return result is null ? NotFound(new { Message = "Researcher not found." }) : Ok(result);
    }
}
