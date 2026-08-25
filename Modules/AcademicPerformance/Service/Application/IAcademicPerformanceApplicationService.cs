namespace AcademicCollectorDemo.Modules.AcademicPerformance.Application;

public interface IAcademicPerformanceApplicationService
{
    Task<AcademicDataResponse> CollectAsync(AcademicDataCollectRequest request);
    Task<AcademicDataResponse> GetResearcherAsync(AcademicResearcherRequest request);
    Task<AcademicPublicationListResponse> ListPublicationsAsync(
        AcademicPublicationListRequest request);
    Task<AcademicPublicationSelectionResponse> SavePublicationSelectionsAsync(
        AcademicPublicationSelectionRequest request);
}
