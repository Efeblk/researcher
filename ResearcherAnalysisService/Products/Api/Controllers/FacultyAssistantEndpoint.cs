using ResearcherAnalysisService.Products.Api.Contracts;
using ResearcherAnalysisService.Products.FacultyAssistant;
using ResearcherAnalysisService.Products.ProductAccess;
using Microsoft.AspNetCore.Mvc;
using ResearcherAnalysisService.Api;

namespace ResearcherAnalysisService.Products.Api.Controllers;

[ApiController]
[ResearcherAnalysisService.Products.Api.ProductJsonContract]
[ServiceFilter<AnalysisAccessFilter>]
[Route("api/v1")]
public sealed class FacultyAssistantEndpoint : ControllerBase
{
    [HttpPost("faculty/context/save")]
    public async Task<ActionResult<FacultyAssistantContextResponse>> SaveFacultyAssistantContext(
        [FromBody] SaveFacultyAssistantContextRequest request, [FromServices] IAcademicProductAccessService access,
        [FromServices] FacultyAssistantContextService service, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        try
        {
            var grant = await access.AuthorizeAsync(User, new(AcademicProductOperation.FacultyContextWrite,
                request.PersonelId.Trim()), cancellationToken);
            var result = await service.SaveAsync(grant, request, cancellationToken);
            return result is null ? NotFound(new { Message = "The requested resource was not found." }) : Ok(result);
        }
        catch (Exception exception) when (exception is AcademicProductAccessUnavailableException or
            AcademicProductUnauthenticatedException or AcademicProductAccessDeniedException)
        { return AcademicProductEndpoint.Error(exception); }
        catch (FacultyAssistantInputException exception) { return UnprocessableEntity(new { Message = exception.Message }); }
        catch (FacultyAssistantConflictException) { return Conflict(new { Message = "The private context version changed." }); }
    }

    [HttpPost("faculty/context")]
    public async Task<ActionResult<FacultyAssistantContextResponse>> GetFacultyAssistantContext(
        [FromBody] GetFacultyAssistantContextRequest request, [FromServices] IAcademicProductAccessService access,
        [FromServices] FacultyAssistantContextService service, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        try
        {
            var grant = await access.AuthorizeAsync(User, new(AcademicProductOperation.FacultyContextRead,
                request.PersonelId.Trim()), cancellationToken);
            var result = await service.GetAsync(grant, request.Version, cancellationToken);
            return result is null ? NotFound(new { Message = "The requested resource was not found." }) : Ok(result);
        }
        catch (Exception exception) when (exception is AcademicProductAccessUnavailableException or
            AcademicProductUnauthenticatedException or AcademicProductAccessDeniedException)
        { return AcademicProductEndpoint.Error(exception); }
    }

    [HttpPost("faculty/assistant/start")]
    public async Task<ActionResult<FacultyAssistantRunResponse>> StartFacultyAssistant(
        [FromBody] StartFacultyAssistantRequest request, [FromServices] IAcademicProductAccessService access,
        [FromServices] FacultyAssistantScheduler scheduler, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        try
        {
            var grant = await access.AuthorizeAsync(User, new(AcademicProductOperation.FacultyAssistantStart,
                request.PersonelId.Trim()), cancellationToken);
            return StatusCode(StatusCodes.Status202Accepted, await scheduler.EnqueueAsync(grant, request, cancellationToken));
        }
        catch (Exception exception) when (exception is AcademicProductAccessUnavailableException or
            AcademicProductUnauthenticatedException or AcademicProductAccessDeniedException)
        { return AcademicProductEndpoint.Error(exception); }
        catch (FacultyAssistantInputException exception) { return UnprocessableEntity(new { Message = exception.Message }); }
        catch (FacultyAssistantConflictException) { return Conflict(new { Message = "The request ID was already used for different input." }); }
    }

    [HttpPost("faculty/assistant/run")]
    public async Task<ActionResult<FacultyAssistantRunResponse>> GetFacultyAssistantRun(
        [FromBody] GetFacultyAssistantRunRequest request, [FromServices] IAcademicProductAccessService access,
        [FromServices] FacultyAssistantReadService service, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || request.RunId == Guid.Empty) return BadRequest(ModelState);
        try
        {
            var grant = await access.AuthorizeAsync(User, new(AcademicProductOperation.FacultyAssistantRead,
                request.PersonelId.Trim()), cancellationToken);
            var result = await service.GetAsync(grant, request.RunId, cancellationToken);
            return result is null ? NotFound(new { Message = "The requested resource was not found." }) : Ok(result);
        }
        catch (Exception exception) when (exception is AcademicProductAccessUnavailableException or
            AcademicProductUnauthenticatedException or AcademicProductAccessDeniedException)
        { return AcademicProductEndpoint.Error(exception); }
    }
}
