using AcademicCollector.Analysis.Contracts;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using ResearcherAnalysisService.Analysis;

namespace ResearcherAnalysisService.Api.V1;

[ApiController]
[Route("api/v1/faculty-assistant")]
[ServiceFilter<AnalysisAccessFilter>]
public sealed class FacultyAssistantController : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(512_000)]
    public async Task<ActionResult<FacultyAssistantAnalysisReport>> Answer(
        [FromBody] FacultyAssistantAnalysisRequest request,
        [FromServices] FacultyAssistant assistant, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try { return Ok(await assistant.AnswerAsync(request, cancellationToken)); }
        catch (AnalysisInputTooLargeException exception) { return UnprocessableEntity(new { reason = "InputTooLarge", detail = exception.Message }); }
        catch (AnalysisUnavailableException exception) { return StatusCode(503, new { reason = "ProviderUnavailable", detail = exception.Message }); }
        catch (InvalidAnalysisException exception) { return StatusCode(502, new { reason = exception.Reason.ToString(), detail = "The assistant output failed structured evidence validation." }); }
        catch (JsonException) { return StatusCode(502, new { reason = "InvalidJson", detail = "The assistant output failed structured validation." }); }
        catch (HttpRequestException) { return StatusCode(502, new { reason = "ProviderRequestFailed", detail = "The configured article provider failed." }); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return StatusCode(504, new { reason = "Timeout", detail = "The faculty assistant timed out." }); }
    }
}
