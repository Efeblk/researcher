using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Files;
using Microsoft.Extensions.Configuration;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;

public sealed class ResearcherCollectionHandler
{
    private readonly ResearcherIdentifierParser _identifierParser;
    private readonly ResearcherCollectionService _collectionService;
    private readonly ResearcherRepository _researcherRepository;
    private readonly AcademicDatabaseInitializer _databaseInitializer;
    private readonly AcademicWorkSynchronizer _academicWorkSynchronizer;
    private readonly AcademicPdfDownloader _academicPdfDownloader;
    private readonly IConfiguration _configuration;

    public ResearcherCollectionHandler(
        ResearcherIdentifierParser identifierParser,
        ResearcherCollectionService collectionService,
        ResearcherRepository researcherRepository,
        AcademicDatabaseInitializer databaseInitializer,
        AcademicWorkSynchronizer academicWorkSynchronizer,
        AcademicPdfDownloader academicPdfDownloader,
        IConfiguration configuration)
    {
        _identifierParser = identifierParser;
        _collectionService = collectionService;
        _researcherRepository = researcherRepository;
        _databaseInitializer = databaseInitializer;
        _academicWorkSynchronizer = academicWorkSynchronizer;
        _academicPdfDownloader = academicPdfDownloader;
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

        try
        {
            await _researcherRepository.SaveAsync(researcher);
            await _academicWorkSynchronizer.SyncAsync(researcher);
            await _academicPdfDownloader.DownloadAvailableAsync(
                researcher.Id,
                response.Messages);

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
