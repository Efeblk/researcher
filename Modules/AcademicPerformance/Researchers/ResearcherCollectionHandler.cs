using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works;
using Microsoft.Extensions.Configuration;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;

public sealed class ResearcherCollectionHandler
{
    private readonly ResearcherIdentifierParser _identifierParser;
    private readonly ResearcherCollectionService _collectionService;
    private readonly ResearcherRepository _researcherRepository;
    private readonly AcademicDatabaseInitializer _databaseInitializer;
    private readonly AcademicWorkSynchronizer _academicWorkSynchronizer;
    private readonly PublicationSummarySynchronizer _publicationSummarySynchronizer;
    private readonly IConfiguration _configuration;

    public ResearcherCollectionHandler(
        ResearcherIdentifierParser identifierParser,
        ResearcherCollectionService collectionService,
        ResearcherRepository researcherRepository,
        AcademicDatabaseInitializer databaseInitializer,
        AcademicWorkSynchronizer academicWorkSynchronizer,
        PublicationSummarySynchronizer publicationSummarySynchronizer,
        IConfiguration configuration)
    {
        _identifierParser = identifierParser;
        _collectionService = collectionService;
        _researcherRepository = researcherRepository;
        _databaseInitializer = databaseInitializer;
        _academicWorkSynchronizer = academicWorkSynchronizer;
        _publicationSummarySynchronizer = publicationSummarySynchronizer;
        _configuration = configuration;
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

        await _databaseInitializer.EnsureReadyAsync();
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

        if (researcher.OrcidProfile is null)
        {
            response.Messages.Add(
                "[HATA] Herkese açık ORCID kaydı alınamadığı için veritabanına yazılmadı.");
            response.IsSaved = false;
            return response;
        }

        try
        {
            await _researcherRepository.SaveAsync(researcher);
            await _academicWorkSynchronizer.SyncAsync(researcher);
            publicationSummaryCount = await _publicationSummarySynchronizer.SyncAsync(
                researcher.Id);
            response.Messages.Add(
                $"[OK] Yayın özeti: {publicationSummaryCount} benzersiz yayın hazırlandı.");

            provider = _configuration["Database:Provider"]
                ?? DatabaseConfiguration.SqliteProvider;
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
