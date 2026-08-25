using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Orcid;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works;
using Microsoft.EntityFrameworkCore;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Application;

public sealed class AcademicPerformanceApplicationService :
    IAcademicPerformanceApplicationService
{
    private const int DefaultPageSize = 100;
    private const int MaximumPageSize = 500;

    private readonly ResearcherCollectionHandler _collectionHandler;
    private readonly AcademicDbContext _dbContext;

    public AcademicPerformanceApplicationService(
        ResearcherCollectionHandler collectionHandler,
        AcademicDbContext dbContext)
    {
        _collectionHandler = collectionHandler;
        _dbContext = dbContext;
    }

    public async Task<AcademicDataResponse> CollectAsync(
        AcademicDataCollectRequest request)
    {
        ResearcherCollectRequest? collectionRequest = null;
        ResearcherCollectResponse? collectionResponse = null;
        int researcherId = 0;
        int publicationCount = 0;

        collectionRequest = new ResearcherCollectRequest
        {
            Identifiers = CreateIdentifiers(request)
        };
        collectionResponse = await _collectionHandler.CollectAsync(collectionRequest);
        researcherId = collectionResponse.Researcher?.Id ?? 0;

        if (collectionResponse.IsSaved && researcherId > 0)
        {
            publicationCount = await _dbContext.PublicationSummaries
                .AsNoTracking()
                .CountAsync(summary => summary.ResearcherId == researcherId);
        }

        return new AcademicDataResponse
        {
            Researcher = MapResearcher(collectionResponse.Researcher),
            IsSaved = collectionResponse.IsSaved,
            PublicationCount = publicationCount,
            DatabaseProvider = collectionResponse.DatabaseProvider,
            CollectedAt = DateTime.UtcNow,
            Messages = collectionResponse.Messages
        };
    }

    public async Task<AcademicDataResponse> GetResearcherAsync(
        AcademicResearcherRequest request)
    {
        Researcher researcher = await ResolveResearcherAsync(
            request.ResearcherId,
            request.Orcid,
            request.WebOfScienceResearcherId);
        int publicationCount = await _dbContext.PublicationSummaries
            .AsNoTracking()
            .CountAsync(summary => summary.ResearcherId == researcher.Id);

        return new AcademicDataResponse
        {
            Researcher = MapResearcher(researcher),
            IsSaved = true,
            PublicationCount = publicationCount,
            CollectedAt = DateTime.UtcNow
        };
    }

    public async Task<AcademicPublicationListResponse> ListPublicationsAsync(
        AcademicPublicationListRequest request)
    {
        Researcher researcher = await ResolveResearcherAsync(
            request.ResearcherId,
            request.Orcid,
            request.WebOfScienceResearcherId);
        IQueryable<PublicationSummary> query = _dbContext.PublicationSummaries
            .AsNoTracking()
            .Where(summary => summary.ResearcherId == researcher.Id);

        if (request.ApprovedOnly)
        {
            query = query.Where(summary => summary.DisplayApproval != null);
        }

        if (!string.IsNullOrWhiteSpace(request.SearchText))
        {
            string searchText = request.SearchText.Trim();
            query = query.Where(summary =>
                summary.Title.Contains(searchText) ||
                (summary.Doi != null && summary.Doi.Contains(searchText)) ||
                (summary.Authors != null && summary.Authors.Contains(searchText)));
        }

        int totalCount = await query.CountAsync();
        int skip = Math.Max(request.Skip, 0);
        int take = request.Take <= 0
            ? DefaultPageSize
            : Math.Min(request.Take, MaximumPageSize);
        List<PublicationSummary> publications = await query
            .OrderByDescending(summary => summary.PublicationYear)
            .ThenBy(summary => summary.Title)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
        List<int> publicationIds = publications
            .Select(publication => publication.Id)
            .ToList();
        HashSet<int> approvedIds = (await _dbContext.PublicationDisplayApprovals
            .AsNoTracking()
            .Where(approval =>
                approval.ResearcherId == researcher.Id &&
                publicationIds.Contains(approval.PublicationSummaryId))
            .Select(approval => approval.PublicationSummaryId)
            .ToListAsync())
            .ToHashSet();

        return new AcademicPublicationListResponse
        {
            ResearcherId = researcher.Id,
            Entities = publications
                .Select(publication => MapPublication(
                    publication,
                    approvedIds.Contains(publication.Id)))
                .ToList(),
            TotalCount = totalCount,
            Skip = skip,
            Take = take
        };
    }

    public async Task<AcademicPublicationSelectionResponse>
        SavePublicationSelectionsAsync(AcademicPublicationSelectionRequest request)
    {
        if (request.ResearcherId <= 0 ||
            !await _dbContext.Researchers
                .AnyAsync(researcher => researcher.Id == request.ResearcherId))
        {
            throw new ArgumentException("Akademisyen kaydı bulunamadı.");
        }

        List<int> requestedIds = request.PublicationIds
            .Where(id => id > 0)
            .Distinct()
            .OrderBy(id => id)
            .ToList();
        List<int> validIds = await _dbContext.PublicationSummaries
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
                "Seçilen yayınlardan biri bu akademisyene ait değil veya mevcut değil.");
        }

        List<PublicationDisplayApproval> existing = await _dbContext
            .PublicationDisplayApprovals
            .Where(approval => approval.ResearcherId == request.ResearcherId)
            .ToListAsync();
        HashSet<int> requestedSet = validIds.ToHashSet();
        HashSet<int> existingIds = existing
            .Select(approval => approval.PublicationSummaryId)
            .ToHashSet();

        _dbContext.PublicationDisplayApprovals.RemoveRange(
            existing.Where(approval =>
                !requestedSet.Contains(approval.PublicationSummaryId)));

        foreach (int publicationId in validIds)
        {
            if (existingIds.Contains(publicationId))
            {
                continue;
            }

            _dbContext.PublicationDisplayApprovals.Add(
                new PublicationDisplayApproval
                {
                    ResearcherId = request.ResearcherId,
                    PublicationSummaryId = publicationId,
                    ApprovedAt = DateTime.UtcNow
                });
        }

        await _dbContext.SaveChangesAsync();
        return new AcademicPublicationSelectionResponse
        {
            ResearcherId = request.ResearcherId,
            PublicationIds = validIds,
            ApprovedCount = validIds.Count
        };
    }

    private async Task<Researcher> ResolveResearcherAsync(
        int? researcherId,
        string? orcid,
        string? webOfScienceResearcherId)
    {
        IQueryable<Researcher> query = _dbContext.Researchers
            .AsNoTracking()
            .Include(researcher => researcher.OrcidProfile)
            .Include(researcher => researcher.WebOfScienceProfile);

        if (researcherId > 0)
        {
            query = query.Where(researcher => researcher.Id == researcherId.Value);
        }
        else if (!string.IsNullOrWhiteSpace(orcid))
        {
            string normalizedOrcid = orcid.Trim();
            query = query.Where(researcher => researcher.Orcid == normalizedOrcid);
        }
        else if (!string.IsNullOrWhiteSpace(webOfScienceResearcherId))
        {
            string normalizedResearcherId = webOfScienceResearcherId
                .Trim()
                .ToUpperInvariant();
            query = query.Where(researcher =>
                researcher.WebOfScienceResearcherId == normalizedResearcherId);
        }
        else
        {
            throw new ArgumentException(
                "ResearcherId, ORCID veya Web of Science ResearcherID verilmelidir.");
        }

        return await query.FirstOrDefaultAsync()
            ?? throw new ArgumentException("Akademisyen kaydı bulunamadı.");
    }

    private static List<string> CreateIdentifiers(AcademicDataCollectRequest request)
    {
        List<string> identifiers = [];

        if (!string.IsNullOrWhiteSpace(request.Orcid))
        {
            identifiers.Add(request.Orcid.Trim());
        }

        if (!string.IsNullOrWhiteSpace(request.WebOfScienceResearcherId))
        {
            identifiers.Add(request.WebOfScienceResearcherId.Trim());
        }

        if (identifiers.Count == 0)
        {
            throw new ArgumentException(
                "ORCID veya Web of Science ResearcherID verilmelidir.");
        }

        return identifiers;
    }

    private static AcademicResearcherDto? MapResearcher(Researcher? researcher)
    {
        if (researcher is null)
        {
            return null;
        }

        return new AcademicResearcherDto
        {
            Id = researcher.Id,
            FirstName = researcher.FirstName,
            LastName = researcher.LastName,
            AcademicTitle = researcher.AcademicTitle,
            Department = researcher.Department,
            Orcid = researcher.Orcid,
            WebOfScienceResearcherId = researcher.WebOfScienceResearcherId,
            YoksisResearcherId = researcher.YoksisResearcherId,
            LastUpdatedAt = researcher.LastUpdatedAt,
            OrcidProfile = MapOrcidProfile(researcher.OrcidProfile),
            WebOfScienceProfile = MapWebOfScienceProfile(
                researcher.WebOfScienceProfile)
        };
    }

    private static OrcidProfileSummaryDto? MapOrcidProfile(OrcidProfile? profile)
    {
        if (profile is null)
        {
            return null;
        }

        return new OrcidProfileSummaryDto
        {
            DisplayName = profile.DisplayName,
            CurrentOrganization = profile.CurrentOrganization,
            CurrentDepartment = profile.CurrentDepartment,
            CurrentRoleTitle = profile.CurrentRoleTitle,
            WorksCount = profile.WorksCount,
            EmploymentsCount = profile.EmploymentsCount,
            EducationsCount = profile.EducationsCount,
            FundingsCount = profile.FundingsCount,
            PeerReviewsCount = profile.PeerReviewsCount,
            RecordLastModifiedAt = profile.RecordLastModifiedAt,
            LastUpdatedAt = profile.LastUpdatedAt
        };
    }

    private static WebOfScienceProfileSummaryDto? MapWebOfScienceProfile(
        WebOfScienceProfile? profile)
    {
        if (profile is null)
        {
            return null;
        }

        return new WebOfScienceProfileSummaryDto
        {
            DisplayName = profile.DisplayName,
            PrimaryOrganization = profile.PrimaryOrganization,
            HIndex = profile.HIndex,
            DocumentsCount = profile.DocumentsCount,
            TotalTimesCited = profile.TotalTimesCited,
            TotalCitingPublications = profile.TotalCitingPublications,
            PeerReviewsCount = profile.PeerReviewsCount,
            LastUpdatedAt = profile.LastUpdatedAt
        };
    }

    private static AcademicPublicationDto MapPublication(
        PublicationSummary publication,
        bool isApproved)
    {
        return new AcademicPublicationDto
        {
            Id = publication.Id,
            Title = publication.Title,
            PublicationYear = publication.PublicationYear,
            PublicationDate = publication.PublicationDate,
            Doi = publication.Doi,
            Category = publication.Category.ToString(),
            Authors = publication.Authors,
            Publication = publication.Publication,
            CitedByCount = publication.CitedByCount,
            PublicationUrl = publication.PublicationUrl,
            Sources = publication.Sources,
            IsApprovedForDisplay = isApproved
        };
    }
}
