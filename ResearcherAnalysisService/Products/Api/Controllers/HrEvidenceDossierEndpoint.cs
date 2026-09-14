using ResearcherAnalysisService.Products.Api.Contracts;
using ResearcherAnalysisService.Products.HrDossiers;
using ResearcherAnalysisService.Products.ProductAccess;
using Microsoft.AspNetCore.Mvc;
using ResearcherAnalysisService.Api;

namespace ResearcherAnalysisService.Products.Api.Controllers;

[ApiController]
[ResearcherAnalysisService.Products.Api.ProductJsonContract]
[ServiceFilter<AnalysisAccessFilter>]
[Route("api/v1/products/[action]")]
public sealed class HrEvidenceDossierEndpoint : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<HrEvidenceDossierResponse>> CreateHrEvidenceDossier(
        [FromBody] CreateHrEvidenceDossierRequest request,
        [FromServices] IAcademicProductAccessService access,
        [FromServices] HrEvidenceDossierService service, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        try
        {
            var grant = await access.AuthorizeAsync(User, new(AcademicProductOperation.HrDossierCreate,
                request.PersonelId.Trim()), cancellationToken);
            var response = await service.CreateAsync(grant, request, cancellationToken);
            return response is null ? NotFound(new { Message = "The requested resource was not found." }) : Ok(response);
        }
        catch (Exception exception) when (exception is AcademicProductAccessUnavailableException or
            AcademicProductUnauthenticatedException or AcademicProductAccessDeniedException)
        { return AcademicProductEndpoint.Error(exception); }
        catch (HrDossierInputException exception) { return UnprocessableEntity(new { Message = exception.Message }); }
    }

    [HttpPost]
    public async Task<ActionResult<HrEvidenceDossierResponse>> GetHrEvidenceDossier(
        [FromBody] GetHrEvidenceDossierRequest request, [FromServices] IAcademicProductAccessService access,
        [FromServices] HrEvidenceDossierService service, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        try
        {
            var grant = await access.AuthorizeAsync(User, new(AcademicProductOperation.HrDossierRead,
                request.PersonelId.Trim()), cancellationToken);
            var response = await service.GetAsync(grant, request.DossierId, cancellationToken);
            return response is null ? NotFound(new { Message = "The requested resource was not found." }) : Ok(response);
        }
        catch (Exception exception) when (exception is AcademicProductAccessUnavailableException or
            AcademicProductUnauthenticatedException or AcademicProductAccessDeniedException)
        { return AcademicProductEndpoint.Error(exception); }
    }

    [HttpPost]
    public async Task<ActionResult<HrDossierReviewActionResponse>> AppendHrDossierReviewAction(
        [FromBody] AppendHrDossierReviewActionRequest request, [FromServices] IAcademicProductAccessService access,
        [FromServices] HrEvidenceDossierService service, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        try
        {
            var grant = await access.AuthorizeAsync(User, new(AcademicProductOperation.HrReviewActionAppend,
                request.PersonelId.Trim()), cancellationToken);
            var response = await service.AppendActionAsync(grant, request, cancellationToken);
            return response is null ? NotFound(new { Message = "The requested resource was not found." }) : Ok(response);
        }
        catch (Exception exception) when (exception is AcademicProductAccessUnavailableException or
            AcademicProductUnauthenticatedException or AcademicProductAccessDeniedException)
        { return AcademicProductEndpoint.Error(exception); }
        catch (HrDossierInputException exception) { return UnprocessableEntity(new { Message = exception.Message }); }
        catch (HrDossierConflictException) { return Conflict(new { Message = "The request ID was already used for different action content." }); }
    }

    [HttpPost]
    public async Task<ActionResult<HrDossierReviewActionListResponse>> ListHrDossierReviewActions(
        [FromBody] ListHrDossierReviewActionsRequest request, [FromServices] IAcademicProductAccessService access,
        [FromServices] HrEvidenceDossierService service, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        try
        {
            var grant = await access.AuthorizeAsync(User, new(AcademicProductOperation.HrReviewActionRead,
                request.PersonelId.Trim()), cancellationToken);
            var response = await service.ListActionsAsync(grant, request, cancellationToken);
            return response is null ? NotFound(new { Message = "The requested resource was not found." }) : Ok(response);
        }
        catch (Exception exception) when (exception is AcademicProductAccessUnavailableException or
            AcademicProductUnauthenticatedException or AcademicProductAccessDeniedException)
        { return AcademicProductEndpoint.Error(exception); }
    }
}
