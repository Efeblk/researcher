using Microsoft.AspNetCore.Mvc;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Api.V1.Contracts;

namespace ResearcherAnalysisService.Api.V1;

[ApiController]
[Route("api/v1/analyze")]
[ServiceFilter(typeof(AnalysisAccessFilter))]
public sealed class AnalyzeController(ResearcherAnalysis analysis, ILogger<AnalyzeController> logger) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(512000)]
    [ProducesResponseType<ResearcherAnalysisReport>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ResearcherAnalysisReport>> Analyze(
        AnalyzeResearcherRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await analysis.AnalyzeAsync(request, cancellationToken);
        }
        catch (AnalysisUnavailableException exception)
        {
            return Problem(statusCode: 503, title: exception.Message);
        }
        catch (AnalysisInputTooLargeException exception)
        {
            return Problem(statusCode: 422, title: exception.Message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Problem(statusCode: 504, title: "AI analysis timed out. Try a smaller publication sample.");
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidAnalysisException)
        {
            // Provider error bodies and submitted research text must not enter logs or error responses.
            logger.LogWarning("AI analysis failed ({ErrorType}).", exception.GetType().Name);
            return Problem(statusCode: 502, title: "The AI provider did not return a usable report.");
        }
    }
}
