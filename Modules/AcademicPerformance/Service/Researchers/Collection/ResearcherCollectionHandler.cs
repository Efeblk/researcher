using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Persistence;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Crossref;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.RateLimiting;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.SemanticScholar;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Collection;

public sealed class ResearcherCollectionHandler
{
    private readonly ResearcherIdentifierParser _identifierParser;
    private readonly ResearcherCollectionService _collectionService;
    private readonly ResearcherRepository _researcherRepository;
    private readonly AcademicWorkSynchronizer _academicWorkSynchronizer;
    private readonly PublicationSummarySynchronizer _publicationSummarySynchronizer;
    private readonly AcademicDbContext _dbContext;
    private readonly CrossrefEnrichmentService _crossrefEnrichmentService;
    private readonly SemanticScholarEnrichmentService? _semanticScholarEnrichmentService;
    private readonly SemanticScholarWorkSourceSynchronizer? _semanticScholarWorkSourceSynchronizer;
    private readonly CanonicalWorkSynchronizer _canonicalWorkSynchronizer;

    public ResearcherCollectionHandler(
        ResearcherIdentifierParser identifierParser,
        ResearcherCollectionService collectionService,
        ResearcherRepository researcherRepository,
        AcademicWorkSynchronizer academicWorkSynchronizer,
        PublicationSummarySynchronizer publicationSummarySynchronizer,
        CrossrefEnrichmentService crossrefEnrichmentService,
        AcademicDbContext dbContext,
        SemanticScholarEnrichmentService? semanticScholarEnrichmentService = null,
        SemanticScholarWorkSourceSynchronizer? semanticScholarWorkSourceSynchronizer = null,
        CanonicalWorkSynchronizer? canonicalWorkSynchronizer = null)
    {
        _identifierParser = identifierParser;
        _collectionService = collectionService;
        _researcherRepository = researcherRepository;
        _academicWorkSynchronizer = academicWorkSynchronizer;
        _publicationSummarySynchronizer = publicationSummarySynchronizer;
        _crossrefEnrichmentService = crossrefEnrichmentService;
        _semanticScholarEnrichmentService = semanticScholarEnrichmentService;
        _semanticScholarWorkSourceSynchronizer = semanticScholarWorkSourceSynchronizer;
        _canonicalWorkSynchronizer = canonicalWorkSynchronizer ?? new CanonicalWorkSynchronizer(dbContext);
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
        requestedResearcher.TcKimlikNo = request.TcKimlikNo;
        requestedResearcher.ScopusId = string.IsNullOrWhiteSpace(request.ScopusId)
            ? requestedResearcher.ScopusId
            : ResearcherIdentifierParser.NormalizeScopusId(request.ScopusId);
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

        response.ProviderFeedback = await _collectionService.CollectAsync(
            researcher,
            requestedResearcher,
            response.Messages);

        if (researcher.OrcidProfile is null &&
            researcher.GoogleScholarProfile is null &&
            researcher.OpenAlexProfile is null &&
            researcher.ScopusProfile is null &&
            researcher.WebOfScienceProfile is null && researcher.TrDizinProfile is null)
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
            await _canonicalWorkSynchronizer.AcquireWriteGateAsync();
            await _canonicalWorkSynchronizer.AcquireResearcherLockAsync(researcher.PersonelId);

            await _researcherRepository.SaveAsync(researcher);
            await _academicWorkSynchronizer.SyncAsync(researcher);
            await _canonicalWorkSynchronizer.SyncAsync(researcher.PersonelId);
            int publicationSummaryCount = await _publicationSummarySynchronizer.SyncAsync(researcher.PersonelId);
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
            ProviderCollectionFeedback crossrefFeedback = NewEnrichmentFeedback(
                response, "Crossref");
            try
            {
                int enriched = await _crossrefEnrichmentService.EnrichAsync(researcher.PersonelId,
                    default, crossrefFeedback);
                if (enriched > 0)
                {
                    publicationSummaryCount = await SynchronizeCrossrefAsync(researcher);
                    response.Messages.Add($"[OK] Crossref: {enriched} DOI sorgusu işlendi.");
                }
            }
            catch (CrossrefPartialEnrichmentException exception)
            {
                PartialEnrichment(crossrefFeedback, exception.CompletedCount, exception.InnerException);
                publicationSummaryCount = await SynchronizeCrossrefAsync(researcher);
                if (!ProviderCallScope.HasFailure("Crossref"))
                    ProviderCallScope.Record("Crossref", false);
                response.Messages.Add($"[OK] Crossref: {exception.CompletedCount} DOI sorgusu işlendi.");
                response.Messages.Add($"[HATA] Crossref zenginleştirmesi tamamlanamadı: {exception.Message}");
                response.Messages.Add(string.Empty);
            }
            catch (Exception exception)
            {
                PartialEnrichment(crossrefFeedback, 0, exception);
                if (!ProviderCallScope.HasFailure("Crossref"))
                    ProviderCallScope.Record("Crossref", false);
                response.Messages.Add($"[HATA] Crossref zenginleştirmesi tamamlanamadı: {exception.Message}");
                response.Messages.Add(string.Empty);
            }
            ProviderCollectionFeedback semanticScholarFeedback = NewEnrichmentFeedback(
                response, "Semantic Scholar");
            try
            {
                if (_semanticScholarEnrichmentService is null)
                {
                    semanticScholarFeedback.Status = "Skipped";
                    semanticScholarFeedback.Reasons.Add(new()
                    {
                        Code = "ServiceUnavailable",
                        Description = "Semantic Scholar zenginleştirme hizmeti kullanılamıyor."
                    });
                }
                int enriched = _semanticScholarEnrichmentService is null ? 0 :
                    await _semanticScholarEnrichmentService.EnrichAsync(researcher.PersonelId,
                        default, semanticScholarFeedback);
                if (_semanticScholarWorkSourceSynchronizer is not null)
                    await _semanticScholarWorkSourceSynchronizer.SyncAsync(researcher.PersonelId);
                response.Messages.Add($"[OK] Semantic Scholar: {enriched} DOI işlendi.");
            }
            catch (SemanticScholarPartialEnrichmentException exception)
            {
                ApplySemanticPartial(semanticScholarFeedback, exception.CompletedCount,
                    exception.InnerException);
                if (_semanticScholarWorkSourceSynchronizer is not null)
                    await _semanticScholarWorkSourceSynchronizer.SyncAsync(researcher.PersonelId);
                if (!ProviderCallScope.HasFailure("SemanticScholar")) ProviderCallScope.Record("SemanticScholar", true);
                response.Messages.Add($"[OK] Semantic Scholar: {exception.CompletedCount} DOI işlendi.");
                response.Messages.Add($"[HATA] Semantic Scholar zenginleştirmesi tamamlanamadı: {exception.Message}");
            }
            catch (Exception exception)
            {
                PartialEnrichment(semanticScholarFeedback, 0, exception);
                if (!ProviderCallScope.HasFailure("SemanticScholar")) ProviderCallScope.Record("SemanticScholar", false);
                response.Messages.Add($"[HATA] Semantic Scholar zenginleştirmesi tamamlanamadı: {exception.Message}");
            }
        }
        catch (Exception exception)
        {
            if (FindSqlException(exception) is { Number: 2628 or 8152 })
            {
                response.FailureCode = "PersistenceDataTooLong";
                response.Messages.Add("[HATA] Veritabanı: Sağlayıcı metaverisi veritabanı alanına sığmadı.");
            }
            else
            {
                response.FailureCode = "PersistenceFailure";
                response.Messages.Add($"[HATA] Veritabanı: {exception.Message}");
            }
            response.Messages.Add(string.Empty);
        }

        return response;
    }

    private static ProviderCollectionFeedback NewEnrichmentFeedback(
        ResearcherCollectResponse response, string provider)
    {
        ProviderCollectionFeedback feedback = new()
        {
            Provider = provider, Unit = "DOI", ExpectedCount = null
        };
        response.ProviderFeedback.Add(feedback);
        return feedback;
    }

    private static void PartialEnrichment(ProviderCollectionFeedback feedback,
        int processed, string code, string description)
    {
        feedback.Status = processed > 0 ? "Partial" : "Failed";
        feedback.RetrievedCount = processed;
        feedback.Reasons.Add(new()
        {
            Code = code, Description = description, AffectedCount = null
        });
    }

    internal static void PartialEnrichment(ProviderCollectionFeedback feedback,
        int processed, Exception? exception)
    {
        (string? code, string? description) = exception is null ? (null, null) :
            ProviderCollectionException.Classify(exception);
        PartialEnrichment(feedback, processed, code ?? "ProviderError",
            description ?? "DOI zenginleştirmesi tamamlanamadı; tamamlanan sonuçlar korundu.");
        feedback.Reasons[^1].AffectedCount = 1;
        int cached = feedback.Reasons
            .Where(reason => reason.Code == "Cached")
            .Sum(reason => reason.AffectedCount ?? 0);
        int? notAttempted = feedback.ExpectedCount.HasValue
            ? Math.Max(0, feedback.ExpectedCount.Value - cached - processed - 1)
            : null;
        if (notAttempted > 0)
        {
            feedback.Reasons.Add(new()
            {
                Code = "NotAttempted",
                Description = "Önceki hata nedeniyle DOI sorgulanmadı.",
                AffectedCount = notAttempted
            });
        }
    }

    internal static void ApplySemanticPartial(ProviderCollectionFeedback feedback,
        int processed, Exception? exception)
    {
        if (feedback.Reasons.Any(reason => reason.Code == "Deferred"))
        {
            feedback.Status = "Partial";
            feedback.RetrievedCount = processed;
            return;
        }
        PartialEnrichment(feedback, processed, exception);
    }

    private async Task<int> SynchronizeCrossrefAsync(Researcher researcher)
    {
        await using IDbContextTransaction transaction = await _dbContext.Database.BeginTransactionAsync();
        await _canonicalWorkSynchronizer.AcquireWriteGateAsync();
        await _canonicalWorkSynchronizer.AcquireResearcherLockAsync(researcher.PersonelId);
        await _academicWorkSynchronizer.SyncAsync(researcher);
        await _canonicalWorkSynchronizer.SyncAsync(researcher.PersonelId);
        int count = await _publicationSummarySynchronizer.SyncAsync(researcher.PersonelId);
        await transaction.CommitAsync();
        return count;
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
