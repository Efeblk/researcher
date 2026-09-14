using System.Security.Claims;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.ProductAccess;

public enum AcademicProductOperation
{
    FacultyContextRead,
    FacultyContextWrite,
    FacultyAssistantStart,
    FacultyAssistantRead,
    KnowledgeRead,
    ReferencePopulationRead,
    ReferencePopulationImport,
    GraphExport,
    HrDossierCreate,
    HrDossierRead,
    HrReviewActionAppend,
    HrReviewActionRead,
    ArticleEvaluationStart,
    ArticleEvaluationRead
}

public sealed record AcademicProductAccessRequest(
    AcademicProductOperation Operation,
    string SubjectPersonelId);

public sealed record AcademicProductAccessGrant(
    string AuthorizationGrantId,
    string ActorAuditId,
    string SubjectPersonelId,
    AcademicProductOperation Operation);

public interface IAcademicProductAccessService
{
    Task<AcademicProductAccessGrant> AuthorizeAsync(
        ClaimsPrincipal principal,
        AcademicProductAccessRequest request,
        CancellationToken cancellationToken);

    Task<AcademicProductAccessGrant> ReauthorizeAsync(
        AcademicProductAccessGrant persistedGrant,
        CancellationToken cancellationToken);
}

public sealed class UnconfiguredAcademicProductAccessService : IAcademicProductAccessService
{
    public Task<AcademicProductAccessGrant> AuthorizeAsync(
        ClaimsPrincipal principal,
        AcademicProductAccessRequest request,
        CancellationToken cancellationToken)
    {
        if (principal.Identity?.IsAuthenticated != true)
            throw new AcademicProductUnauthenticatedException();
        throw new AcademicProductAccessUnavailableException();
    }

    public Task<AcademicProductAccessGrant> ReauthorizeAsync(
        AcademicProductAccessGrant persistedGrant,
        CancellationToken cancellationToken) =>
        throw new AcademicProductAccessUnavailableException();
}

public sealed class AcademicProductAccessUnavailableException : Exception;
public sealed class AcademicProductUnauthenticatedException : Exception;
public sealed class AcademicProductAccessDeniedException : Exception;
