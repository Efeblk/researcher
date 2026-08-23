using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Endpoints;

[Route("Services/AcademicPerformance/PublicationSummary/[action]")]
public sealed class PublicationSummaryEndpoint : ServiceEndpoint
{
    [HttpPost]
    public async Task<ListResponse<PublicationSummary>> List(
        ListRequest request,
        [FromServices] AcademicDatabaseInitializer databaseInitializer,
        [FromServices] AcademicDbContext dbContext)
    {
        IQueryable<PublicationSummary> query = dbContext.PublicationSummaries
            .AsNoTracking();

        await databaseInitializer.EnsureReadyAsync();

        if (!TryGetResearcherId(request, out int researcherId))
        {
            return new ListResponse<PublicationSummary>
            {
                Entities = [],
                TotalCount = 0
            };
        }

        query = query.Where(item => item.ResearcherId == researcherId);

        int totalCount = await query.CountAsync();
        int skip = Math.Max(request.Skip, 0);
        int take = request.Take <= 0 ? 100 : Math.Min(request.Take, 500);
        List<PublicationSummary> entities = await query
            .OrderByDescending(item => item.PublicationYear)
            .ThenBy(item => item.Title)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
        List<int> entityIds = entities.Select(item => item.Id).ToList();
        HashSet<int> approvedIds = (await dbContext.PublicationDisplayApprovals
            .AsNoTracking()
            .Where(approval =>
                approval.ResearcherId == researcherId &&
                entityIds.Contains(approval.PublicationSummaryId))
            .Select(approval => approval.PublicationSummaryId)
            .ToListAsync())
            .ToHashSet();

        foreach (PublicationSummary entity in entities)
        {
            entity.IsApprovedForDisplay = approvedIds.Contains(entity.Id);
        }

        return new ListResponse<PublicationSummary>
        {
            Entities = entities,
            TotalCount = totalCount
        };
    }

    private static bool TryGetResearcherId(ListRequest request, out int researcherId)
    {
        researcherId = 0;

        if (request.EqualityFilter is null ||
            !request.EqualityFilter.TryGetValue("ResearcherId", out object? value) ||
            value is null)
        {
            return false;
        }

        return int.TryParse(value.ToString(), out researcherId) && researcherId > 0;
    }
}
