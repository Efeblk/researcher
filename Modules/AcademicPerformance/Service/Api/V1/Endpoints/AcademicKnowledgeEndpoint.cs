using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.GraphProjection;
using AcademicCollectorDemo.Modules.AcademicPerformance.Knowledge;
using AcademicCollectorDemo.Modules.AcademicPerformance.Metrics;
using AcademicCollectorDemo.Modules.AcademicPerformance.ProductAccess;
using Microsoft.AspNetCore.Mvc;
using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Endpoints;

[Route("Services/AcademicPerformance/V1/[action]")]
public sealed class AcademicKnowledgeEndpoint : ServiceEndpoint
{
    [HttpPost]
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

    [HttpPost]
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

    [HttpPost]
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

    [HttpPost]
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
