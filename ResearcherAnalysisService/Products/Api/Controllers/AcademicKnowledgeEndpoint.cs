using ResearcherAnalysisService.Products.Api.Contracts;
using ResearcherAnalysisService.Products.GraphProjection;
using ResearcherAnalysisService.Products.Knowledge;
using ResearcherAnalysisService.Products.Metrics;
using ResearcherAnalysisService.Products.ProductAccess;
using Microsoft.AspNetCore.Mvc;
using ResearcherAnalysisService.Api;

namespace ResearcherAnalysisService.Products.Api.Controllers;

[ApiController]
[ResearcherAnalysisService.Products.Api.ProductJsonContract]
[Route("api/v1")]
public sealed class AcademicKnowledgeEndpoint : ControllerBase
{
    [HttpPost("knowledge/search")]
    public async Task<ActionResult<AcademicEvidenceSearchResponse>> SearchAcademicEvidence(
        [FromBody] AcademicEvidenceSearchRequest request,
        [FromServices] IAcademicProductAccessService access,
        [FromServices] IAcademicEvidenceSearchService search,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        try
        {
            AcademicProductAccessGrant grant = await access.AuthorizeAsync(User,
                new(AcademicProductOperation.KnowledgeRead, request.PersonelId.Trim()),
                cancellationToken);
            return Ok(await search.SearchAsync(grant.SubjectPersonelId, request, cancellationToken));
        }
        catch (Exception exception) when (IsAccessFailure(exception))
        { return AcademicProductEndpoint.Error(exception); }
        catch (ArgumentException exception)
        { return UnprocessableEntity(new { Message = exception.Message }); }
    }

    [HttpPost("knowledge/reference-population")]
    public async Task<ActionResult<ReferencePopulationManifestResponse>> GetReferencePopulation(
        [FromBody] ReferencePopulationManifestRequest request,
        [FromServices] IAcademicProductAccessService access,
        [FromServices] ReferencePopulationManifestService service,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        try
        {
            await access.AuthorizeAsync(User,
                new(AcademicProductOperation.ReferencePopulationRead, request.PersonelId.Trim()),
                cancellationToken);
            ReferencePopulationManifestResponse? result = await service.GetAsync(
                request.ManifestVersion, cancellationToken);
            return result is null ? NotFound(new { Message = "The requested resource was not found." }) : Ok(result);
        }
        catch (Exception exception) when (IsAccessFailure(exception))
        { return AcademicProductEndpoint.Error(exception); }
    }

    [HttpPost("knowledge/reference-population/import")]
    public async Task<ActionResult<ReferencePopulationManifestResponse>> ImportReferencePopulation(
        [FromBody] ReferencePopulationImportRequest request,
        [FromServices] IAcademicProductAccessService access,
        [FromServices] ReferencePopulationManifestService service,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        try
        {
            AcademicProductAccessGrant grant = await access.AuthorizeAsync(User,
                new(AcademicProductOperation.ReferencePopulationImport, request.PersonelId.Trim()),
                cancellationToken);
            return Ok(await service.ImportAsync(grant.ActorAuditId, request, cancellationToken));
        }
        catch (Exception exception) when (IsAccessFailure(exception))
        { return AcademicProductEndpoint.Error(exception); }
        catch (ArgumentException exception)
        { return UnprocessableEntity(new { Message = exception.Message }); }
        catch (InvalidOperationException exception)
        { return Conflict(new { Message = exception.Message }); }
    }

    [HttpPost("knowledge/graph/export")]
    public async Task<ActionResult<AcademicGraphProjectionBundle>> ExportAcademicEvidenceGraph(
        [FromBody] GraphProjectionRequest request,
        [FromServices] IAcademicProductAccessService access,
        [FromServices] AcademicGraphProjectionService service,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        try
        {
            AcademicProductAccessGrant grant = await access.AuthorizeAsync(User,
                new(AcademicProductOperation.GraphExport, request.PersonelId.Trim()),
                cancellationToken);
            AcademicGraphProjectionBundle? result = await service.ExportAsync(
                grant.SubjectPersonelId, request.CanonicalWorkIds, cancellationToken);
            return result is null ? NotFound(new { Message = "The requested resource was not found." }) : Ok(result);
        }
        catch (Exception exception) when (IsAccessFailure(exception))
        { return AcademicProductEndpoint.Error(exception); }
        catch (ArgumentException exception)
        { return UnprocessableEntity(new { Message = exception.Message }); }
        catch (InvalidOperationException exception)
        { return UnprocessableEntity(new { Message = exception.Message }); }
    }

    private static bool IsAccessFailure(Exception exception) =>
        exception is AcademicProductAccessUnavailableException or
            AcademicProductUnauthenticatedException or AcademicProductAccessDeniedException;
}
