using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.FacultyAssistant;
using AcademicCollectorDemo.Modules.AcademicPerformance.ProductAccess;
using Microsoft.AspNetCore.Mvc;
using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Endpoints;

[Route("Services/AcademicPerformance/V1/[action]")]
public sealed class FacultyAssistantEndpoint : ServiceEndpoint
{
    [HttpPost]
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

    [HttpPost]
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

    [HttpPost]
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

    [HttpPost]
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
