using AcademicCollectorDemo.Modules.AcademicPerformance.WebClient.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Application;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.WebClient.Endpoints;

[Route("Services/AcademicPerformance/PublicationDisplayApproval/[action]")]
public sealed class PublicationDisplayApprovalEndpoint : ServiceEndpoint
{
    [HttpPost]
    public async Task<PublicationDisplayApprovalResponse> Get(
        PublicationDisplayApprovalRequest request,
        [FromServices] AcademicDbContext dbContext)
    {
        await EnsureResearcherExistsAsync(request.ResearcherId, dbContext);

        List<int> approvedIds = await dbContext.PublicationDisplayApprovals
            .AsNoTracking()
            .Where(approval => approval.ResearcherId == request.ResearcherId)
            .OrderBy(approval => approval.PublicationSummaryId)
            .Select(approval => approval.PublicationSummaryId)
            .ToListAsync();

        return CreateResponse(request.ResearcherId, approvedIds);
    }

    [HttpPost]
    public async Task<PublicationDisplayApprovalResponse> Save(
        PublicationDisplayApprovalRequest request,
        [FromServices] IAcademicPerformanceApplicationService applicationService)
    {
        AcademicPublicationSelectionResponse response =
            await applicationService.SavePublicationSelectionsAsync(
                new AcademicPublicationSelectionRequest
                {
                    ResearcherId = request.ResearcherId,
                    PublicationIds = request.PublicationSummaryIds
                });

        return CreateResponse(request.ResearcherId, response.PublicationIds);
    }

    [HttpPost]
    public async Task<ApprovedPublicationListResponse> ListApproved(
        PublicationDisplayApprovalRequest request,
        [FromServices] AcademicDbContext dbContext)
    {
        await EnsureResearcherExistsAsync(request.ResearcherId, dbContext);

        List<PublicationSummary> publications = await dbContext
            .PublicationDisplayApprovals
            .AsNoTracking()
            .Where(approval => approval.ResearcherId == request.ResearcherId)
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
            ResearcherId = request.ResearcherId,
            Entities = publications,
            TotalCount = publications.Count
        };
    }

    private static PublicationDisplayApprovalResponse CreateResponse(
        int researcherId,
        List<int> approvedIds)
    {
        return new PublicationDisplayApprovalResponse
        {
            ResearcherId = researcherId,
            PublicationSummaryIds = approvedIds,
            ApprovedCount = approvedIds.Count
        };
    }

    private static async Task EnsureResearcherExistsAsync(
        int researcherId,
        AcademicDbContext dbContext)
    {
        if (researcherId <= 0 ||
            !await dbContext.Researchers.AnyAsync(item => item.Id == researcherId))
        {
            throw new ArgumentException("Akademisyen kaydı bulunamadı.");
        }
    }
}
