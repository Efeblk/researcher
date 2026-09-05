using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Serenity.Services;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.WebClient.Endpoints;

[Route("Services/AcademicPerformance/PublicationSummary/[action]")]
public sealed class PublicationSummaryEndpoint : ServiceEndpoint
{
    [HttpPost]
    public async Task<ListResponse<PublicationSummary>> List(
        ListRequest request,
        [FromServices] AcademicDbContext dbContext)
    {
        IQueryable<PublicationSummary> query = dbContext.PublicationSummaries
            .AsNoTracking();

        if (!TryGetResearcherId(request, out int researcherId))
        {
            return new ListResponse<PublicationSummary>
            {
                Entities = [],
                TotalCount = 0
            };
        }

        query = query.Where(item => item.ResearcherId == researcherId);

        if (!string.IsNullOrWhiteSpace(request.ContainsText))
        {
            string searchText = request.ContainsText.Trim();
            query = query.Where(item =>
                item.Title.Contains(searchText) ||
                (item.Authors != null && item.Authors.Contains(searchText)) ||
                (item.Publication != null && item.Publication.Contains(searchText)) ||
                (item.Doi != null && item.Doi.Contains(searchText)));
        }

        int totalCount = await query.CountAsync();
        int skip = Math.Max(request.Skip, 0);
        int take = request.Take <= 0 ? 100 : Math.Min(request.Take, 500);
        List<PublicationSummary> entities = await query
            .OrderByDescending(item => item.PublicationYear)
            .ThenBy(item => item.Title)
            .ThenBy(item => item.Id)
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
