using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Orcid;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.GoogleScholar;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Collection;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using Microsoft.EntityFrameworkCore;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Application;

public sealed class AcademicPerformanceApplicationService :
    IAcademicPerformanceApplicationService
{
    private const int DefaultPageSize = 100;
    private const int MaximumPageSize = 500;

    private readonly ResearcherCollectionHandler _collectionHandler;
    private readonly AcademicDbContext _dbContext;
    private readonly ResearcherProviderInputNormalizer _inputNormalizer;

    public AcademicPerformanceApplicationService(
        ResearcherCollectionHandler collectionHandler,
        AcademicDbContext dbContext,
        ResearcherProviderInputNormalizer inputNormalizer)
    {
        _collectionHandler = collectionHandler;
        _dbContext = dbContext;
        _inputNormalizer = inputNormalizer;
    }

    public async Task<AcademicDataResponse> CollectAsync(
        AcademicDataCollectRequest request)
    {
        int publicationCount = 0;
        if (string.IsNullOrWhiteSpace(request.PersonelId))
            throw new ArgumentException("PersonelID is required.");
        if (request.PersonelId.Trim().Length > 200)
            throw new ArgumentException("PersonelID must be at most 200 characters.");

        ResearcherProviderInputNormalizationResult normalization = _inputNormalizer.Normalize(new()
        {
            Orcid = request.Orcid,
            GoogleScholarId = request.GoogleScholarId,
            WebOfScienceResearcherId = request.WebOfScienceResearcherId,
            ScopusId = request.ScopusId
        });
        if (normalization.RejectionReason is not null)
        {
            string details = normalization.Warnings.Count == 0
                ? string.Empty
                : " " + string.Join(" ", normalization.Warnings);
            throw new ArgumentException(normalization.RejectionReason + details);
        }
        ResearcherCollectRequest collectionRequest =
            ResearcherProviderInputNormalizer.ToCollectionRequest(normalization.Input);
        collectionRequest.PersonelId = request.PersonelId.Trim();
        collectionRequest.ScopusId = string.IsNullOrWhiteSpace(request.ScopusId)
            ? null : request.ScopusId.Trim();
        ResearcherCollectResponse? collectionResponse = await _collectionHandler.CollectAsync(collectionRequest);
        string? personelId = collectionResponse.Researcher?.PersonelId;

        if (collectionResponse.IsSaved && !string.IsNullOrWhiteSpace(personelId))
        {
            publicationCount = await _dbContext.PublicationSummaries
                .AsNoTracking()
                .CountAsync(summary => summary.PersonelId == personelId);
        }

        return new AcademicDataResponse
        {
            Researcher = AcademicPerformanceDtoMapper.MapResearcher(collectionResponse.Researcher),
            IsSaved = collectionResponse.IsSaved,
            FailureCode = collectionResponse.FailureCode,
            PublicationCount = publicationCount,
            DatabaseProvider = collectionResponse.DatabaseProvider,
            CollectedAt = DateTime.UtcNow,
            Messages = collectionResponse.Messages
                .Concat(normalization.Warnings.Select(warning => "[UYARI] " + warning)).ToList(),
            Warnings = normalization.Warnings
        };
    }

    public async Task<AcademicDataResponse> GetResearcherAsync(
        AcademicResearcherRequest request)
    {
        Researcher researcher = await ResolveResearcherAsync(
            request.PersonelId,
            request.Orcid,
            request.GoogleScholarId,
            request.WebOfScienceResearcherId);
        int publicationCount = await _dbContext.PublicationSummaries
            .AsNoTracking()
            .CountAsync(summary => summary.PersonelId == researcher.PersonelId);

        AcademicResearcherDto? researcherDto = AcademicPerformanceDtoMapper.MapResearcher(researcher);
        if (researcherDto?.OpenAlexProfile is not null)
        {
            researcherDto.OpenAlexProfile.CollectedWorksCount = await _dbContext.OpenAlexWorks
                .CountAsync(work => work.OpenAlexProfileId == researcher.OpenAlexProfile!.Id);
        }

        return new AcademicDataResponse
        {
            Researcher = researcherDto,
            IsSaved = true,
            PublicationCount = publicationCount,
            CollectedAt = DateTime.UtcNow
        };
    }

    public async Task<AcademicPublicationListResponse> ListPublicationsAsync(
        AcademicPublicationListRequest request)
    {
        Researcher researcher = await ResolveResearcherAsync(
            request.PersonelId,
            request.Orcid,
            request.GoogleScholarId,
            request.WebOfScienceResearcherId);
        IQueryable<PublicationSummary> query = _dbContext.PublicationSummaries
            .AsNoTracking()
            .Where(summary => summary.PersonelId == researcher.PersonelId);

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
            .ThenBy(summary => summary.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
        List<int> publicationIds = publications
            .Select(publication => publication.Id)
            .ToList();
        HashSet<int> approvedIds = (await _dbContext.PublicationDisplayApprovals
            .AsNoTracking()
            .Where(approval =>
                approval.PersonelId == researcher.PersonelId &&
                publicationIds.Contains(approval.PublicationSummaryId))
            .Select(approval => approval.PublicationSummaryId)
            .ToListAsync())
            .ToHashSet();

        return new AcademicPublicationListResponse
        {
            PersonelId = researcher.PersonelId,
            Entities = publications
                .Select(publication => AcademicPerformanceDtoMapper.MapPublication(
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
        if (string.IsNullOrWhiteSpace(request.PersonelId) ||
            !await _dbContext.Researchers
                .AnyAsync(researcher => researcher.PersonelId == request.PersonelId))
        {
            throw new ArgumentException("Akademisyen kaydı bulunamadı.");
        }

        if (request.PublicationIds is null)
            throw new ArgumentException("Yayın seçim listesi verilmelidir; tüm seçimleri kaldırmak için boş liste gönderin.");

        List<int> requestedIds = request.PublicationIds
            .Where(id => id > 0)
            .Distinct()
            .OrderBy(id => id)
            .ToList();
        List<int> validIds = await _dbContext.PublicationSummaries
            .AsNoTracking()
            .Where(summary =>
                summary.PersonelId == request.PersonelId &&
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
            .Where(approval => approval.PersonelId == request.PersonelId)
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
                    PersonelId = request.PersonelId,
                    PublicationSummaryId = publicationId,
                    ApprovedAt = DateTime.UtcNow
                });
        }

        await _dbContext.SaveChangesAsync();
        return new AcademicPublicationSelectionResponse
        {
            PersonelId = request.PersonelId,
            PublicationIds = validIds,
            ApprovedCount = validIds.Count
        };
    }

    private async Task<Researcher> ResolveResearcherAsync(
        string? personelId,
        string? orcid,
        string? googleScholarId,
        string? webOfScienceResearcherId)
    {
        IQueryable<Researcher> query = _dbContext.Researchers
            .AsNoTracking()
            .Include(researcher => researcher.OrcidProfile)
            .Include(researcher => researcher.GoogleScholarProfile)
            .Include(researcher => researcher.OpenAlexProfile)
            .Include(researcher => researcher.WebOfScienceProfile)
            .Include(researcher => researcher.TrDizinProfile);

        bool hasSelector = !string.IsNullOrWhiteSpace(personelId) ||
            !string.IsNullOrWhiteSpace(orcid) || !string.IsNullOrWhiteSpace(googleScholarId) ||
            !string.IsNullOrWhiteSpace(webOfScienceResearcherId);
        if (!hasSelector)
            throw new ArgumentException("PersonelID, ORCID, ScholarID veya ResearcherID verilmelidir.");

        if (!string.IsNullOrWhiteSpace(personelId))
            query = query.Where(researcher => researcher.PersonelId == personelId.Trim());
        if (!string.IsNullOrWhiteSpace(orcid))
        {
            string normalizedOrcid = ResearcherIdentifierParser.NormalizeOrcid(orcid);
            query = query.Where(researcher => researcher.Orcid == normalizedOrcid);
        }
        if (!string.IsNullOrWhiteSpace(googleScholarId))
        {
            string normalizedGoogleScholarId = ResearcherIdentifierParser.NormalizeGoogleScholarId(googleScholarId);
            query = query.Where(researcher =>
                researcher.GoogleScholarId == normalizedGoogleScholarId);
        }
        if (!string.IsNullOrWhiteSpace(webOfScienceResearcherId))
        {
            string normalizedResearcherId = ResearcherIdentifierParser.NormalizeResearcherId(webOfScienceResearcherId);
            query = query.Where(researcher =>
                researcher.WebOfScienceResearcherId == normalizedResearcherId);
        }
        return await query.FirstOrDefaultAsync()
            ?? throw new ArgumentException("Akademisyen kaydı bulunamadı.");
    }

}
