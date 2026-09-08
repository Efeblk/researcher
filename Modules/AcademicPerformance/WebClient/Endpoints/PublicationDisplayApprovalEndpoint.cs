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
        await EnsureResearcherExistsAsync(request.PersonelId, dbContext);

        List<int> approvedIds = await dbContext.PublicationDisplayApprovals
            .AsNoTracking()
            .Where(approval => approval.PersonelId == request.PersonelId)
            .OrderBy(approval => approval.PublicationSummaryId)
            .Select(approval => approval.PublicationSummaryId)
            .ToListAsync();

        return CreateResponse(request.PersonelId, approvedIds);
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
                    PersonelId = request.PersonelId,
                    PublicationIds = request.PublicationSummaryIds
                });

        return CreateResponse(request.PersonelId, response.PublicationIds);
    }

    [HttpPost]
    public async Task<ApprovedPublicationListResponse> ListApproved(
        PublicationDisplayApprovalRequest request,
        [FromServices] AcademicDbContext dbContext)
    {
        await EnsureResearcherExistsAsync(request.PersonelId, dbContext);

        List<PublicationSummary> publications = await dbContext
            .PublicationDisplayApprovals
            .AsNoTracking()
            .Where(approval => approval.PersonelId == request.PersonelId)
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
            PersonelId = request.PersonelId,
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
