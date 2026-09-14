using AcademicCollector.Analysis.Contracts;
using Microsoft.AspNetCore.Mvc;
using ResearcherAnalysisService.Analysis;
using ResearcherAnalysisService.Api;

namespace ResearcherAnalysisService.Api.V1;

[ApiController, Route("api/v1/articles")]
public sealed class ArticleReviewController : ControllerBase
{
    [HttpGet("review/configuration"), ServiceFilter<AnalysisAccessFilter>]
    public ActionResult<ArticleReviewRuntimeConfiguration> Configuration(
        [FromServices] ArticleReviewStageExecutor executor)
    {
        try { return Ok(executor.GetConfiguration()); }
        catch (AnalysisUnavailableException exception)
        { return StatusCode(503, new { Message = exception.Message }); }
    }

    [HttpPost("review/stages/quote"), ServiceFilter<AnalysisAccessFilter>]
    public ActionResult<ArticleReviewStageQuote> Quote(
        [FromBody] ArticleReviewStageQuoteRequest request,
        [FromServices] ArticleReviewStageExecutor executor)
    {
        try { return Ok(executor.Quote(request)); }
        catch (BadHttpRequestException exception) { return BadRequest(new { Message = exception.Message }); }
        catch (AnalysisInputTooLargeException exception) { return StatusCode(413, new { Message = exception.Message }); }
        catch (AnalysisUnavailableException exception) { return StatusCode(503, new { Message = exception.Message }); }
        catch (InvalidAnalysisException exception)
        {
            return StatusCode(422, new AnalysisErrorResponse(
                "The proposed verification findings are invalid.", "invalid_stage_input")
            { Failure = new(InvalidAnalysisException.CodeFor(exception.Reason), exception.Stage, exception.Role) });
        }
    }

    [HttpPost("review/stages/generate"), ServiceFilter<AnalysisAccessFilter>]
    public async Task<ActionResult<ArticleReviewGenerationStageResult>> GenerateStage(
        [FromBody] ArticleReviewStageDispatchRequest request,
        [FromServices] ArticleReviewStageExecutor executor,
        CancellationToken cancellationToken)
    {
        try { return Ok(await executor.GenerateAsync(request, cancellationToken)); }
        catch (BadHttpRequestException exception) { return BadRequest(new { Message = exception.Message }); }
        catch (AnalysisInputTooLargeException exception) { return StatusCode(413, new { Message = exception.Message }); }
        catch (AnalysisUnavailableException exception) { return StageError(503, "provider_unavailable", exception.Message, request, executor); }
        catch (InvalidAnalysisException exception)
        {
            return StageError(502, InvalidAnalysisException.CodeFor(exception.Reason),
                "The model returned an invalid generation stage.", request, executor, "generation", request.Role);
        }
        catch (HttpRequestException)
        { return StageError(502, "provider_failure", "The configured model provider failed.", request, executor); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return StageError(504, "timeout", "Article review generation timed out.", request, executor); }
    }

    [HttpPost("review/stages/verify"), ServiceFilter<AnalysisAccessFilter>]
    public async Task<ActionResult<ArticleReviewVerificationStageResult>> VerifyStage(
        [FromBody] ArticleReviewStageDispatchRequest request,
        [FromServices] ArticleReviewStageExecutor executor,
        CancellationToken cancellationToken)
    {
        try { return Ok(await executor.VerifyAsync(request, cancellationToken)); }
        catch (BadHttpRequestException exception) { return BadRequest(new { Message = exception.Message }); }
        catch (AnalysisInputTooLargeException exception) { return StatusCode(413, new { Message = exception.Message }); }
        catch (AnalysisUnavailableException exception) { return StageError(503, "provider_unavailable", exception.Message, request, executor); }
        catch (InvalidAnalysisException exception)
        {
            return StageError(502, InvalidAnalysisException.CodeFor(exception.Reason),
                "The model returned an invalid verification stage.", request, executor, "verification", request.Role);
        }
        catch (HttpRequestException)
        { return StageError(502, "provider_failure", "The configured model provider failed.", request, executor); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return StageError(504, "timeout", "Article review verification timed out.", request, executor); }
    }

    [HttpPost("review"), ServiceFilter<AnalysisAccessFilter>]
    public async Task<ActionResult<ArticleReviewReport>> Review(
        [FromBody] ReviewArticleRequest request,
        [FromServices] ArticleReviewer reviewer,
        CancellationToken cancellationToken)
    {
        try { return Ok(await reviewer.ReviewAsync(request, cancellationToken)); }
        catch (BadHttpRequestException exception) { return BadRequest(new { Message = exception.Message }); }
        catch (AnalysisInputTooLargeException exception) { return StatusCode(413, new { Message = exception.Message }); }
        catch (AnalysisUnavailableException exception) { return StatusCode(503, new { Message = exception.Message }); }
        catch (InvalidAnalysisException exception)
        {
            return StatusCode(502, new AnalysisErrorResponse(
                "The model returned an invalid or unsupported specialist review.", "invalid_provider_response")
            {
                Failure = new(InvalidAnalysisException.CodeFor(exception.Reason), exception.Stage, exception.Role)
            });
        }
        catch (HttpRequestException)
        {
            return StatusCode(502, new AnalysisErrorResponse(
                "The configured model provider failed.", "provider_failure"));
        }
        catch (ArticleReviewTimedOutException exception) when (!cancellationToken.IsCancellationRequested)
        {
            return StatusCode(504, new AnalysisErrorResponse("Article review timed out.", "timeout")
            {
                Failure = new("timeout", exception.Stage, exception.Role)
            });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return StatusCode(504, new AnalysisErrorResponse("Article review timed out.", "timeout")); }
    }

    private static ObjectResult StageError(int status, string code, string message,
        ArticleReviewStageDispatchRequest request, ArticleReviewStageExecutor executor,
        string? stage = null, string? role = null) => new(new AnalysisErrorResponse(message, code)
        {
            Failure = stage is null ? null : new(code, stage, role),
            ProviderAttempt = executor.LastAttempt()
        }) { StatusCode = status };
}
