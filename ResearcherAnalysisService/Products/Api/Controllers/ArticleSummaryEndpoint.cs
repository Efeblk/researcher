using System.Text.Json;
using AcademicCollector.Analysis.Contracts;
using ResearcherAnalysisService.Products.Api.Contracts;
using ResearcherAnalysisService.Products.ArticleSummaries;
using ResearcherAnalysisService.Products.ArticleReviews;
using Microsoft.AspNetCore.Mvc;
using ResearcherAnalysisService.Api;

namespace ResearcherAnalysisService.Products.Api.Controllers;

[ApiController]
[ResearcherAnalysisService.Products.Api.ProductJsonContract]
[ServiceFilter<AnalysisAccessFilter>]
[Route("api/v1")]
public sealed class ArticleSummaryEndpoint : ControllerBase
{
    [HttpPost("articles/summary/generate")]
    public async Task<ActionResult<SavedArticleSummaryResponse>> SummarizeArticle([FromBody] ArticleSummaryRequest request,
        [FromServices] ArticleSummaryWorkflow workflow, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        try
        {
            SavedArticleSummaryResponse? result = await workflow.SummarizeAsync(request.PersonelID.Trim(), request.AcademicWorkId, request.Language, cancellationToken);
            return result is null ? NotFound(new { Message = "Article was not found for this researcher." }) : Ok(result);
        }
        catch (ArticleSourceException exception) { return UnprocessableEntity(new { Message = exception.Message }); }
        catch (ArticleSummaryBusyException) { return StatusCode(409, new { Message = "Article summary generation is already in progress." }); }
        catch (HttpRequestException exception) { return StatusCode(exception.StatusCode is null ? 503 : 502, new { Message = "Article summary generation failed; no report was saved." }); }
        catch (JsonException) { return StatusCode(502, new { Message = "The analysis service returned an unusable article report; no report was saved." }); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return StatusCode(504, new { Message = "Article summary generation timed out; no report was saved." }); }
    }

    [HttpPost("articles/summary")]
    public async Task<ActionResult<SavedArticleSummaryResponse>> GetArticleSummary([FromBody] ArticleSummaryRequest request,
        [FromServices] ArticleSummaryWorkflow workflow, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        SavedArticleSummaryResponse? result = await workflow.GetLatestAsync(
            request.PersonelID.Trim(), request.AcademicWorkId, request.Language, cancellationToken);
        return result is null ? NotFound(new { Message = "No saved summary exists for this article and researcher." }) : Ok(result);
    }

    [HttpPost("articles/analysis")]
    public async Task<ActionResult<CanonicalArticleAnalysisResponse>> GetCanonicalArticleAnalysis(
        [FromBody] CanonicalArticleAnalysisRequest request,
        [FromServices] CanonicalArticleAnalysisQueryService queryService,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        CanonicalArticleAnalysisResponse? result = await queryService.GetAsync(
            request.PersonelId.Trim(), request.CanonicalWorkId, request.Language,
            request.Skip, request.Take, cancellationToken);
        return result is null
            ? NotFound(new { Message = "No current researcher association was found." })
            : Ok(result);
    }

    [HttpPost("articles/review/generate")]
    public async Task<ActionResult<CanonicalArticleReviewResponse>> ReviewCanonicalArticle(
        [FromBody] CanonicalArticleReviewRequest request,
        [FromServices] ArticleReviewWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        try
        {
            return Ok(await workflow.ReviewAsync(request.PersonelId.Trim(), request.CanonicalWorkId,
                request.Language, request.ForceRegeneration, cancellationToken));
        }
        catch (ArticleReviewUnavailableException exception) { return UnprocessableEntity(new { Message = exception.Message }); }
        catch (ArticleReviewBusyException) { return StatusCode(409, new { Message = "Article review generation is already in progress." }); }
        catch (ArticleReviewInputTooLargeException) { return StatusCode(413, new { Message = "The saved article source exceeds the configured review budget." }); }
        catch (ArticleReviewSourceChangedException) { return StatusCode(409, new { Message = "The canonical article analysis changed during review; no report was saved." }); }
        catch (ArticleReviewAnalysisException exception)
        {
            return StatusCode((int)exception.StatusCode,
                new AnalysisErrorResponse(exception.Message, exception.ErrorCode) { Failure = exception.Failure });
        }
        catch (HttpRequestException exception) when (exception.StatusCode == System.Net.HttpStatusCode.RequestEntityTooLarge)
        { return StatusCode(413, new { Message = "The saved article source exceeds the analysis service review budget." }); }
        catch (HttpRequestException) { return StatusCode(502, new { Message = "Article review generation failed; no report was saved." }); }
        catch (JsonException) { return StatusCode(502, new { Message = "The analysis service returned an unusable article review; no report was saved." }); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return StatusCode(504, new { Message = "Article review generation timed out; no report was saved." }); }
    }

}
