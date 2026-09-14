using System.Security.Claims;
using ResearcherAnalysisService.Products.Api.Contracts;
using ResearcherAnalysisService.Products.Api.Controllers;
using ResearcherAnalysisService.Products.HrDossiers;
using ResearcherAnalysisService.Products.FacultyAssistant;
using ResearcherAnalysisService.Products.ProductAccess;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ResearcherAnalysisService.Tests;

public sealed class AcademicProductAccessEndpointTests
{
    [Fact]
    public async Task GetHrEvidenceDossier_AnonymousFailsBeforeServiceUse()
    {
        HrEvidenceDossierEndpoint endpoint = Endpoint(new ClaimsPrincipal(new ClaimsIdentity()));
        ActionResult<HrEvidenceDossierResponse> result = await endpoint.GetHrEvidenceDossier(
            new() { PersonelId = "subject", DossierId = 1 },
            new UnconfiguredAcademicProductAccessService(), null!, default);
        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetHrEvidenceDossier_AuthenticatedButUnconfiguredReturns503BeforeServiceUse()
    {
        HrEvidenceDossierEndpoint endpoint = Endpoint(new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "external-user")], "synthetic")));
        ActionResult<HrEvidenceDossierResponse> result = await endpoint.GetHrEvidenceDossier(
            new() { PersonelId = "subject", DossierId = 1 },
            new UnconfiguredAcademicProductAccessService(), null!, default);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsType<ObjectResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task GetFacultyAssistantContext_CrossSubjectDenialIsUniform404BeforeServiceUse()
    {
        FacultyAssistantEndpoint endpoint = new()
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "external-user")], "synthetic"))
            } }
        };

        ActionResult<FacultyAssistantContextResponse> result = await endpoint.GetFacultyAssistantContext(
            new() { PersonelId = "other-subject" }, new DenyingAccess(), null!, default);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    private static HrEvidenceDossierEndpoint Endpoint(ClaimsPrincipal principal) => new()
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } }
    };

    private sealed class DenyingAccess : IAcademicProductAccessService
    {
        public Task<AcademicProductAccessGrant> AuthorizeAsync(ClaimsPrincipal principal,
            AcademicProductAccessRequest request, CancellationToken cancellationToken) =>
            throw new AcademicProductAccessDeniedException();

        public Task<AcademicProductAccessGrant> ReauthorizeAsync(AcademicProductAccessGrant persistedGrant,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
