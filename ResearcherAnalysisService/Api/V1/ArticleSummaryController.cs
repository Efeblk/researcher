using AcademicCollector.Analysis.Contracts;
using Microsoft.AspNetCore.Mvc;
using ResearcherAnalysisService.Analysis;

namespace ResearcherAnalysisService.Api.V1;

[ApiController, Route("api/v1/articles")]
public sealed class ArticleSummaryController : ControllerBase
{
    [HttpPost("summarize"), ServiceFilter<AnalysisAccessFilter>]
    public async Task<ActionResult<ArticleSummaryReport>> Summarize([FromBody] SummarizeArticleRequest request,
        [FromServices] ArticleSummarizer summarizer, CancellationToken cancellationToken)
    {
        try { return Ok(await summarizer.SummarizeAsync(request, cancellationToken)); }
        catch (BadHttpRequestException exception) { return BadRequest(new { Message = exception.Message }); }
        catch (AnalysisInputTooLargeException exception) { return StatusCode(413, new { Message = exception.Message }); }
        catch (AnalysisUnavailableException exception) { return StatusCode(503, new { Message = exception.Message }); }
        catch (InvalidAnalysisException) { return StatusCode(502, new { Message = "The model returned an invalid or unsupported article report." }); }
        catch (HttpRequestException) { return StatusCode(502, new { Message = "The configured model provider failed." }); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return StatusCode(504, new { Message = "Article summarization timed out." }); }
    }
}
