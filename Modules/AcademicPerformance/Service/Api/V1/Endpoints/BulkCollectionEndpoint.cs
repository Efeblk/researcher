using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Bulk;
using AcademicCollectorDemo.Modules.AcademicPerformance.Bulk.SqlImport;
using Microsoft.AspNetCore.Mvc;
using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Endpoints;

[Route("Services/AcademicPerformance/V1/Bulk/[action]")]
public sealed class BulkCollectionEndpoint : ServiceEndpoint
{
    [HttpPost, RequestSizeLimit(8 * 1024 * 1024)]
    public Task<BulkCollectionStatusResponse> Submit(BulkCollectionSubmitRequest request,
        [FromServices] BulkCollectionService service, CancellationToken cancellationToken)
        => service.SubmitAsync(request, cancellationToken);

    [HttpPost]
    public Task<BulkCollectionStatusResponse> Status(BulkCollectionStatusRequest request,
        [FromServices] BulkCollectionService service, CancellationToken cancellationToken)
        => service.GetStatusAsync(request, cancellationToken);

    [HttpPost]
    public Task<BulkCollectionStatusResponse> ImportSql(BulkCollectionStatusRequest request,
        [FromServices] BulkSqlImporter importer, CancellationToken cancellationToken)
        => importer.ImportAsync(request.BatchId, cancellationToken);
}
