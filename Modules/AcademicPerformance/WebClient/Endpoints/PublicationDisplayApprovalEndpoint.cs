using AcademicCollectorDemo.Modules.AcademicPerformance.WebClient.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Application;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Serenity.Services;
using AcademicCollectorDemo.Modules.AcademicPerformance.WebClient.Identity;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.WebClient.Endpoints;

[Route("Services/AcademicPerformance/PublicationDisplayApproval/[action]")]
public sealed class PublicationDisplayApprovalEndpoint : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<PublicationDisplayApprovalResponse>> Get(
        PublicationDisplayApprovalRequest request,
        [FromServices] AcademicDbContext dbContext,
        [FromServices] ICurrentPersonnelResolver currentPersonnel)
    {
        if (!CurrentPersonnelHttpResult.TryResolve(
            currentPersonnel, out string personelId, out ActionResult? error))
        {
            return error!;
        }
        await EnsureResearcherExistsAsync(personelId, dbContext);

        List<int> approvedIds = await dbContext.PublicationDisplayApprovals
            .AsNoTracking()
            .Where(approval => approval.PersonelId == personelId)
            .OrderBy(approval => approval.PublicationSummaryId)
            .Select(approval => approval.PublicationSummaryId)
            .ToListAsync();

        return CreateResponse(personelId, approvedIds);
    }

    [HttpPost]
    public async Task<ActionResult<PublicationDisplayApprovalResponse>> Save(
        PublicationDisplayApprovalRequest request,
        [FromServices] ICurrentPersonnelResolver currentPersonnel,
        [FromServices] IAcademicPerformanceApplicationService applicationService)
    {
        if (!CurrentPersonnelHttpResult.TryResolve(
            currentPersonnel, out string personelId, out ActionResult? error))
        {
            return error!;
        }
        AcademicPublicationSelectionResponse response =
            await applicationService.SavePublicationSelectionsAsync(
                new AcademicPublicationSelectionRequest
                {
                    PersonelId = personelId,
                    PublicationIds = request.PublicationSummaryIds
                });

        return CreateResponse(personelId, response.PublicationIds);
    }

    [HttpPost]
    public async Task<ActionResult<ApprovedPublicationListResponse>> ListApproved(
        PublicationDisplayApprovalRequest request,
        [FromServices] AcademicDbContext dbContext,
        [FromServices] ICurrentPersonnelResolver currentPersonnel)
    {
        if (!CurrentPersonnelHttpResult.TryResolve(
            currentPersonnel, out string personelId, out ActionResult? error))
        {
            return error!;
        }
        await EnsureResearcherExistsAsync(personelId, dbContext);

        List<PublicationSummary> publications = await dbContext
            .PublicationDisplayApprovals
            .AsNoTracking()
            .Where(approval => approval.PersonelId == personelId)
            .Select(approval => approval.PublicationSummary!)
            .OrderByDescending(summary => summary.PublicationYear)
            .ThenBy(summary => summary.Title)
            .ToListAsync();

        foreach (PublicationSummary publication in publications)
        {
            publication.IsApprovedForDisplay = true;
        }

        return new ApprovedPublicationListResponse
        {
            PersonelId = personelId,
            Entities = publications,
            TotalCount = publications.Count
        };
    }

    private static PublicationDisplayApprovalResponse CreateResponse(
        string personelId,
        List<int> approvedIds)
    {
        return new PublicationDisplayApprovalResponse
        {
            PersonelId = personelId,
            PublicationSummaryIds = approvedIds,
            ApprovedCount = approvedIds.Count
        };
    }

    private static async Task EnsureResearcherExistsAsync(
        string personelId,
        AcademicDbContext dbContext)
    {
        if (string.IsNullOrWhiteSpace(personelId) ||
            !await dbContext.Researchers.AnyAsync(item => item.PersonelId == personelId))
        {
            throw new ArgumentException("Akademisyen kaydı bulunamadı.");
        }
    }
}
