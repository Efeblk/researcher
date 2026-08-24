using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Endpoints;

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
        [FromServices] AcademicDbContext dbContext)
    {
        await EnsureResearcherExistsAsync(request.ResearcherId, dbContext);

        List<int> requestedIds = request.PublicationSummaryIds
            .Where(id => id > 0)
            .Distinct()
            .OrderBy(id => id)
            .ToList();
        List<int> validIds = await dbContext.PublicationSummaries
            .AsNoTracking()
            .Where(summary =>
                summary.ResearcherId == request.ResearcherId &&
                requestedIds.Contains(summary.Id))
            .Select(summary => summary.Id)
            .OrderBy(id => id)
            .ToListAsync();

        if (validIds.Count != requestedIds.Count)
        {
            throw new ArgumentException(
                "Seçilen yayınlardan biri bu akademisyene ait değil veya artık mevcut değil.");
        }

        List<PublicationDisplayApproval> existing = await dbContext
            .PublicationDisplayApprovals
            .Where(approval => approval.ResearcherId == request.ResearcherId)
            .ToListAsync();
        HashSet<int> requestedSet = validIds.ToHashSet();

        dbContext.PublicationDisplayApprovals.RemoveRange(
            existing.Where(approval =>
                !requestedSet.Contains(approval.PublicationSummaryId)));

        HashSet<int> existingIds = existing
            .Select(approval => approval.PublicationSummaryId)
            .ToHashSet();
        DateTime approvedAt = DateTime.UtcNow;

        foreach (int publicationSummaryId in validIds)
        {
            if (existingIds.Contains(publicationSummaryId))
            {
                continue;
            }

            dbContext.PublicationDisplayApprovals.Add(
                new PublicationDisplayApproval
                {
                    ResearcherId = request.ResearcherId,
                    PublicationSummaryId = publicationSummaryId,
                    ApprovedAt = approvedAt
                });
        }

        await dbContext.SaveChangesAsync();
        return CreateResponse(request.ResearcherId, validIds);
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
