using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Persistence;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;
using Microsoft.EntityFrameworkCore.Storage;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Collection;

public sealed class ResearcherCollectionHandler
{
    private readonly ResearcherIdentifierParser _identifierParser;
    private readonly ResearcherCollectionService _collectionService;
    private readonly ResearcherRepository _researcherRepository;
    private readonly AcademicWorkSynchronizer _academicWorkSynchronizer;
    private readonly PublicationSummarySynchronizer _publicationSummarySynchronizer;
    private readonly AcademicDbContext _dbContext;

    public ResearcherCollectionHandler(
        ResearcherIdentifierParser identifierParser,
        ResearcherCollectionService collectionService,
        ResearcherRepository researcherRepository,
        AcademicWorkSynchronizer academicWorkSynchronizer,
        PublicationSummarySynchronizer publicationSummarySynchronizer,
        AcademicDbContext dbContext)
    {
        _identifierParser = identifierParser;
        _collectionService = collectionService;
        _researcherRepository = researcherRepository;
        _academicWorkSynchronizer = academicWorkSynchronizer;
        _publicationSummarySynchronizer = publicationSummarySynchronizer;
        _dbContext = dbContext;
    }

    public async Task<ResearcherCollectResponse> CollectAsync(
        ResearcherCollectRequest request)
    {
        ResearcherCollectResponse? response = null;
        Researcher? researcher = null;
        Researcher? requestedResearcher = null;
        Researcher? existingResearcher = null;
        string? provider = null;
        int publicationSummaryCount = 0;

        response = new ResearcherCollectResponse();
        requestedResearcher = _identifierParser.Create(request);
        researcher = requestedResearcher;

        existingResearcher = await _researcherRepository.FindByIdentifiersAsync(
            requestedResearcher);

        if (existingResearcher is not null)
        {
            _researcherRepository.ApplyRequestValues(
                existingResearcher,
                requestedResearcher);
            researcher = existingResearcher;
        }

        response.Researcher = researcher;

        await _collectionService.CollectAsync(
            researcher,
            requestedResearcher,
            response.Messages);

        if (researcher.OrcidProfile is null &&
            researcher.GoogleScholarProfile is null &&
            researcher.OpenAlexProfile is null &&
            researcher.WebOfScienceProfile is null)
        {
            response.Messages.Add(
                "[HATA] Hiçbir akademik sağlayıcıdan veri alınamadığı için " +
                "veritabanına yazılmadı.");
            response.IsSaved = false;
            return response;
        }

        try
        {
            await using IDbContextTransaction transaction =
                await _dbContext.Database.BeginTransactionAsync();

            await _researcherRepository.SaveAsync(researcher);
            await _academicWorkSynchronizer.SyncAsync(researcher);
            publicationSummaryCount = await _publicationSummarySynchronizer.SyncAsync(
                researcher.Id);
            await transaction.CommitAsync();
            response.Messages.Add(
                $"[OK] Yayın özeti: {publicationSummaryCount} benzersiz yayın hazırlandı.");

            provider = AcademicDatabase.ProviderName;
            response.DatabaseProvider = provider;
            response.IsSaved = true;
            response.Messages.Add(
                $"[OK] Veritabanı: {provider} kaydı tamamlandı " +
                $"(akademisyen ID: {researcher.Id}).");
            response.Messages.Add(string.Empty);
        }
        catch (Exception exception)
        {
            response.Messages.Add($"[HATA] Veritabanı: {exception.Message}");
            response.Messages.Add(string.Empty);
        }

        return response;
    }
}
