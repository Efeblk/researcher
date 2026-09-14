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
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Persistence;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis.Collection;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Application;

public sealed class AcademicPerformanceApplicationService :
    IAcademicPerformanceApplicationService
{
    private const int DefaultPageSize = 100;
    private const int MaximumPageSize = 500;

    private readonly ResearcherCollectionHandler _collectionHandler;
    private readonly AcademicDbContext _dbContext;
    private readonly ResearcherProviderInputNormalizer _inputNormalizer;
    private readonly CanonicalWorkQueryService _canonicalWorkQueryService;
    private readonly CanonicalWorkSynchronizer _canonicalWorkSynchronizer;
    private readonly YoksisCollectionHandler? _yoksisCollectionHandler;

    public AcademicPerformanceApplicationService(
        ResearcherCollectionHandler collectionHandler,
        AcademicDbContext dbContext,
        ResearcherProviderInputNormalizer inputNormalizer,
        CanonicalWorkQueryService? canonicalWorkQueryService = null,
        CanonicalWorkSynchronizer? canonicalWorkSynchronizer = null,
        YoksisCollectionHandler? yoksisCollectionHandler = null)
    {
        _collectionHandler = collectionHandler;
        _dbContext = dbContext;
        _inputNormalizer = inputNormalizer;
        _canonicalWorkQueryService = canonicalWorkQueryService ?? new CanonicalWorkQueryService(dbContext);
        _canonicalWorkSynchronizer = canonicalWorkSynchronizer ?? new CanonicalWorkSynchronizer(dbContext);
        _yoksisCollectionHandler = yoksisCollectionHandler;
    }

    public async Task<AcademicDataResponse> CollectAsync(
        AcademicDataCollectRequest request)
    {
        int publicationCount = 0;
        if (string.IsNullOrWhiteSpace(request.PersonelId))
            throw new ArgumentException("PersonelID is required.");
        if (request.PersonelId.Trim().Length > 200)
            throw new ArgumentException("PersonelID must be at most 200 characters.");

        string personelId = request.PersonelId.Trim();
        string? tcKimlikNo = string.IsNullOrWhiteSpace(request.TcKimlikNo)
            ? null : YoksisCollectionService.ValidateTcKimlikNo(request.TcKimlikNo);
        if (tcKimlikNo is not null)
        {
            bool identityConflict = await _dbContext.Researchers.AsNoTracking().AnyAsync(researcher =>
                (researcher.PersonelId == personelId && researcher.TcKimlikNo != null && researcher.TcKimlikNo != tcKimlikNo) ||
                (researcher.TcKimlikNo == tcKimlikNo && researcher.PersonelId != personelId));
            if (identityConflict)
                throw new ArgumentException("T.C. kimlik numarası farklı bir personel kaydıyla eşleşiyor.");
        }

        ResearcherProviderInputNormalizationResult normalization = _inputNormalizer.Normalize(new()
        {
            Orcid = request.Orcid,
            GoogleScholarId = request.GoogleScholarId,
            WebOfScienceResearcherId = request.WebOfScienceResearcherId,
            ScopusId = request.ScopusId
        });
        if (normalization.RejectionReason is not null && tcKimlikNo is null)
        {
            string details = normalization.Warnings.Count == 0
                ? string.Empty
                : " " + string.Join(" ", normalization.Warnings);
            throw new ArgumentException(normalization.RejectionReason + details);
        }
        bool providerOwnershipConflict = await _dbContext.Researchers.AsNoTracking().AnyAsync(researcher =>
            researcher.PersonelId != personelId &&
            ((normalization.Input.Orcid != null && researcher.Orcid == normalization.Input.Orcid) ||
             (normalization.Input.GoogleScholarId != null && researcher.GoogleScholarId == normalization.Input.GoogleScholarId) ||
             (normalization.Input.WebOfScienceResearcherId != null &&
                researcher.WebOfScienceResearcherId == normalization.Input.WebOfScienceResearcherId)));
        if (providerOwnershipConflict)
            throw new ArgumentException("Sağlayıcı kimliği farklı bir personel kaydıyla eşleşiyor.");
        YoksisCollectResponse? yoksisResponse = null;
        if (tcKimlikNo is not null)
        {
            if (_yoksisCollectionHandler is null)
                throw new InvalidOperationException("YÖKSİS collection is not configured.");
            yoksisResponse = await _yoksisCollectionHandler.CollectAsync(new()
            {
                PersonelId = personelId,
                TcKimlikNo = tcKimlikNo
            });
            Researcher? discovered = await _dbContext.Researchers.AsNoTracking()
                .SingleOrDefaultAsync(researcher => researcher.PersonelId == personelId);
            if (discovered is not null)
            {
                normalization.Input.Orcid ??= discovered.Orcid;
                normalization.Input.GoogleScholarId ??= discovered.GoogleScholarId;
                normalization.Input.WebOfScienceResearcherId ??= discovered.WebOfScienceResearcherId;
            }
        }
        bool hasProviderIdentifier = normalization.Input.Orcid is not null ||
            normalization.Input.GoogleScholarId is not null || normalization.Input.WebOfScienceResearcherId is not null;
        if (!hasProviderIdentifier)
        {
            Researcher? savedResearcher = await _dbContext.Researchers.AsNoTracking()
                .SingleOrDefaultAsync(researcher => researcher.PersonelId == personelId);
            return new()
            {
                Researcher = savedResearcher is null ? null : AcademicPerformanceDtoMapper.MapResearcher(savedResearcher),
                IsSaved = yoksisResponse?.IsSaved == true,
                YoksisFailedCategoryCount = yoksisResponse?.FailedCategoryCount ?? 0,
                CollectedAt = DateTime.UtcNow,
                Messages = yoksisResponse?.Messages ?? []
            };
        }
        ResearcherCollectRequest collectionRequest =
            ResearcherProviderInputNormalizer.ToCollectionRequest(normalization.Input);
        collectionRequest.PersonelId = personelId;
        collectionRequest.TcKimlikNo = tcKimlikNo;
        collectionRequest.ScopusId = string.IsNullOrWhiteSpace(request.ScopusId)
            ? null : request.ScopusId.Trim();
        ResearcherCollectResponse? collectionResponse = await _collectionHandler.CollectAsync(collectionRequest);
        string? collectedPersonelId = collectionResponse.Researcher?.PersonelId;

        if (collectionResponse.IsSaved && !string.IsNullOrWhiteSpace(collectedPersonelId))
        {
            publicationCount = await _dbContext.PublicationSummaries
                .AsNoTracking()
                .CountAsync(summary => summary.PersonelId == collectedPersonelId);
        }

        return new AcademicDataResponse
        {
            Researcher = AcademicPerformanceDtoMapper.MapResearcher(collectionResponse.Researcher),
            IsSaved = collectionResponse.IsSaved || yoksisResponse?.IsSaved == true,
            FailureCode = collectionResponse.FailureCode,
            YoksisFailedCategoryCount = yoksisResponse?.FailedCategoryCount ?? 0,
            PublicationCount = publicationCount,
            DatabaseProvider = collectionResponse.DatabaseProvider,
            CollectedAt = DateTime.UtcNow,
            Messages = (yoksisResponse?.Messages ?? []).Concat(collectionResponse.Messages)
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

    public async Task<CanonicalPublicationListResponse> ListCanonicalPublicationsAsync(
        CanonicalPublicationListRequest request,
        CancellationToken cancellationToken = default)
    {
        Researcher researcher = await ResolveResearcherAsync(
            request.PersonelId,
            request.Orcid,
            request.GoogleScholarId,
            request.WebOfScienceResearcherId,
            cancellationToken);
        return await _canonicalWorkQueryService.ListAsync(
            researcher.PersonelId, request, cancellationToken);
    }

    public async Task<CanonicalPublicationRebuildResponse> RebuildCanonicalPublicationsAsync(
        CanonicalPublicationRebuildRequest request,
        CancellationToken cancellationToken = default)
    {
        string personelId = request.PersonelId?.Trim() ?? string.Empty;
        if (personelId.Length == 0 || personelId.Length > 200 ||
            !await _dbContext.Researchers.AsNoTracking()
                .AnyAsync(researcher => researcher.PersonelId == personelId, cancellationToken))
        {
            throw new ArgumentException("Akademisyen kaydı bulunamadı.");
        }

        CanonicalWorkSyncResult result = await _canonicalWorkSynchronizer.SyncAsync(
            personelId, cancellationToken);
        return new()
        {
            PersonelId = personelId,
            CanonicalWorkCount = result.CanonicalWorkCount,
            ObservationCount = result.ObservationCount,
            AssociationCount = result.AssociationCount
        };
    }

    private async Task<Researcher> ResolveResearcherAsync(
        string? personelId,
        string? orcid,
        string? googleScholarId,
        string? webOfScienceResearcherId,
        CancellationToken cancellationToken = default)
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
        return await query.FirstOrDefaultAsync(cancellationToken)
            ?? throw new ArgumentException("Akademisyen kaydı bulunamadı.");
    }

}
