using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Metrics;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Application;

public interface IAcademicPerformanceApplicationService
{
    Task<AcademicDataResponse> CollectAsync(AcademicDataCollectRequest request);
    Task<ResearcherMetricsResponse> RecalculateMetricsAsync(
        ResearcherMetricsRequest request,
        CancellationToken cancellationToken = default);
    Task<ResearcherMetricsResponse> RecalculateMetricsAsync(
        ResearcherMetricsRequest request,
        IProgress<ResearcherMetricsProgress> progress,
        CancellationToken cancellationToken = default) =>
        RecalculateMetricsAsync(request, cancellationToken);
    Task<AcademicDataResponse> GetResearcherAsync(AcademicResearcherRequest request);
    Task<AcademicPublicationListResponse> ListPublicationsAsync(
        AcademicPublicationListRequest request);
    Task<AcademicPublicationSelectionResponse> SavePublicationSelectionsAsync(
        AcademicPublicationSelectionRequest request);
    Task<CanonicalPublicationListResponse> ListCanonicalPublicationsAsync(
        CanonicalPublicationListRequest request,
        CancellationToken cancellationToken = default);
}
