using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Persistence;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Data.SqlClient;

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
        if (string.IsNullOrWhiteSpace(request.PersonelId))
            throw new ArgumentException("PersonelID is required.");
        if (request.PersonelId.Trim().Length > 200)
            throw new ArgumentException("PersonelID must be at most 200 characters.");

        ResearcherCollectResponse response = new();
        Researcher requestedResearcher = _identifierParser.Create(request);
        requestedResearcher.PersonelId = request.PersonelId.Trim();
        requestedResearcher.ScopusId = NormalizeOptional(request.ScopusId);
        Researcher researcher = requestedResearcher;

        Researcher? existingResearcher = await _researcherRepository.FindByIdentifiersAsync(
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
            int publicationSummaryCount = await _publicationSummarySynchronizer.SyncAsync(
                researcher.PersonelId);
            await transaction.CommitAsync();
            response.Messages.Add(
                $"[OK] Yayın özeti: {publicationSummaryCount} benzersiz yayın hazırlandı.");

            string provider = AcademicDatabase.ProviderName;
            response.DatabaseProvider = provider;
            response.IsSaved = true;
            response.Messages.Add(
                $"[OK] Veritabanı: {provider} kaydı tamamlandı " +
                $"(PersonelID: {researcher.PersonelId}).");
            response.Messages.Add(string.Empty);
        }
        catch (Exception exception)
        {
            if (FindSqlException(exception) is { Number: 2628 or 8152 })
            {
                response.FailureCode = "PersistenceDataTooLong";
                response.Messages.Add("[HATA] Veritabanı: Sağlayıcı metaverisi veritabanı alanına sığmadı.");
            }
            else
                response.Messages.Add($"[HATA] Veritabanı: {exception.Message}");
            response.Messages.Add(string.Empty);
        }

        return response;
    }

    private static SqlException? FindSqlException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is SqlException sqlException)
                return sqlException;
        return null;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
