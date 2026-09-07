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
        catch (InvalidAnalysisException exception)
        {
            logger.LogWarning("AI analysis failed ({ErrorType}, {Reason}).",
                nameof(InvalidAnalysisException), exception.Reason);
            return Problem(statusCode: 502, title: "The AI provider did not return a usable report.",
                detail: exception.Message,
                extensions: new Dictionary<string, object?> { ["reason"] = exception.Reason.ToString() });
        }
        catch (HttpRequestException exception)
        {
            // Provider error bodies and submitted research text must not enter logs or error responses.
            int? providerStatus = exception.StatusCode is { } status ? (int)status : null;
            logger.LogWarning("AI analysis failed ({ErrorType}, provider HTTP status: {ProviderStatus}).",
                exception.GetType().Name, providerStatus);
            string detail = providerStatus.HasValue
                ? $"The AI provider returned HTTP {providerStatus.Value}. Check the provider logs for the underlying error."
                : "The AI provider request failed before a usable response was received. Check the provider logs and connection.";
            return Problem(statusCode: 502, title: "The AI provider did not return a usable report.",
                detail: detail, extensions: new Dictionary<string, object?>
                {
                    ["reason"] = "ProviderRequestFailed",
                    ["providerStatus"] = providerStatus
                });
        }
    }
}
