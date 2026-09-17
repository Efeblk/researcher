using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis.Persistence;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Persistence;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.RateLimiting;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis.Collection;

public sealed class YoksisCollectionHandler
{
    private readonly YoksisCollectionService _collectionService;
    private readonly YoksisRecordSynchronizer _recordSynchronizer;
    private readonly YoksisAcademicWorkSynchronizer _workSynchronizer;
    private readonly ResearcherRepository _researcherRepository;
    private readonly PublicationSummarySynchronizer _summarySynchronizer;
    private readonly AcademicDbContext _dbContext;
    private readonly CanonicalWorkSynchronizer _canonicalWorkSynchronizer;
    private readonly YoksisCollectionCache _collectionCache;
    private readonly bool _enabled;

    public YoksisCollectionHandler(
        YoksisCollectionService collectionService,
        YoksisRecordSynchronizer recordSynchronizer,
        YoksisAcademicWorkSynchronizer workSynchronizer,
        ResearcherRepository researcherRepository,
        PublicationSummarySynchronizer summarySynchronizer,
        AcademicDbContext dbContext,
        YoksisCollectionCache collectionCache,
        CanonicalWorkSynchronizer? canonicalWorkSynchronizer = null,
        IConfiguration? configuration = null)
    {
        _collectionService = collectionService;
        _recordSynchronizer = recordSynchronizer;
        _workSynchronizer = workSynchronizer;
        _researcherRepository = researcherRepository;
        _summarySynchronizer = summarySynchronizer;
        _dbContext = dbContext;
        _collectionCache = collectionCache;
        _canonicalWorkSynchronizer = canonicalWorkSynchronizer ?? new CanonicalWorkSynchronizer(dbContext);
        _enabled = configuration?.GetValue("ProviderRequestLimits:Yoksis:Enabled", true) ?? true;
    }

    public async Task<YoksisCollectResponse> CollectAsync(
        YoksisCollectRequest request,
        IProgress<YoksisCollectionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.PersonelId))
            throw new ArgumentException("PersonelID is required.");
        if (request.PersonelId.Trim().Length > 200)
            throw new ArgumentException("PersonelID must be at most 200 characters.");
        string personelId = request.PersonelId.Trim();
        string tcKimlikNo = YoksisCollectionService.ValidateTcKimlikNo(
            request.TcKimlikNo);

        if (!_enabled)
        {
            const string message = "[ATLANDI] YÖKSİS: yerel yapılandırmada devre dışı.";
            progress?.Report(new() { Stage = "disabled", Message = message });
            return new()
            {
                IsDisabled = true,
                PersonelId = personelId,
                Messages = [message]
            };
        }

        using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, ProviderCallScope.Cancellation);
        cancellationToken = linkedCancellation.Token;
        await ValidateIdentityOwnershipAsync(personelId, tcKimlikNo, cancellationToken);

        if (!request.UpdatedAfter.HasValue)
        {
            YoksisCollectResponse? cachedResponse = await _collectionCache.TryGetFreshAsync(
                personelId,
                tcKimlikNo,
                cancellationToken);
            if (cachedResponse is not null)
            {
                cachedResponse.IsCached = true;
                cachedResponse.PublicationSummaryCount = await _dbContext.PublicationSummaries
                    .AsNoTracking()
                    .CountAsync(summary => summary.PersonelId == personelId, cancellationToken);
                cachedResponse.Messages =
                [
                    "[OK] YÖKSİS verileri son başarılı toplamadan önbellekten yüklendi."
                ];
                YoksisCollectionService.RemoveUnrequestedResponseData(
                    cachedResponse,
                    request);
                progress?.Report(new()
                {
                    Stage = "cached",
                    Message = "YÖKSİS verileri önbellekten yüklendi.",
                    RecordCount = cachedResponse.TotalRecordCount
                });
                progress?.Report(new()
                {
                    Stage = "completed",
                    Message = YoksisCollectionProgressMessages.Completion(cachedResponse),
                    RecordCount = cachedResponse.TotalRecordCount
                });
                return cachedResponse;
            }
        }

        progress?.Report(new() { Stage = "starting", Message = "YÖKSİS toplaması başlatıldı." });
        YoksisCollectResponse? response = await _collectionService.CollectAsync(
            request, progress, cancellationToken);
        int persistenceMessageStart = response.Messages.Count;

        try
        {
            progress?.Report(new()
            {
                Stage = "persistence",
                Message = "Toplanan kayıtlar veritabanına kaydediliyor."
            });
            await using IDbContextTransaction transaction =
                await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            await _canonicalWorkSynchronizer.AcquireWriteGateAsync(cancellationToken);
            await _canonicalWorkSynchronizer.AcquireResearcherLockAsync(personelId, cancellationToken);

            Researcher? requestedResearcher = CreateResearcher(response);
            requestedResearcher.PersonelId = personelId;
            requestedResearcher.TcKimlikNo = tcKimlikNo;
            Researcher? researcher = await ResolveResearcherAsync(
                requestedResearcher);
            await _researcherRepository.SaveAsync(researcher);
            response.YoksisRecordCount = await _recordSynchronizer.SyncAsync(
                researcher.PersonelId,
                response,
                isIncremental: request.UpdatedAfter.HasValue,
                cancellationToken);
            int publicationCount = await _workSynchronizer.SyncAsync(
                researcher.PersonelId,
                response,
                isIncremental: request.UpdatedAfter.HasValue,
                cancellationToken);
            await _canonicalWorkSynchronizer.SyncAsync(researcher.PersonelId, cancellationToken);
            response.PersonelId = researcher.PersonelId;
            response.ResearcherDisplayName = CreateDisplayName(researcher);
            response.YoksisPublicationCount = publicationCount;
            response.PublicationSummaryCount =
                await _summarySynchronizer.SyncAsync(researcher.PersonelId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            response.IsSaved = true;
            response.Messages.Add(
                $"[OK] YÖKSİS verileri: {response.YoksisRecordCount} kayıt " +
                "YoksisRecords tablosuna yazıldı.");
            response.Messages.Add(
                $"[OK] YÖKSİS yayınları: {publicationCount} kayıt " +
                "ortak yayın tablosuna yazıldı.");
            response.Messages.Add(
                $"[OK] Yayın özeti: {response.PublicationSummaryCount} " +
                "benzersiz yayın hazırlandı.");

            if (!request.UpdatedAfter.HasValue && IsCompleteSuccessfulCollection(response))
            {
                await _collectionCache.ReplaceAsync(
                    researcher.PersonelId,
                    tcKimlikNo,
                    DateTime.UtcNow,
                    response,
                    cancellationToken);
            }
            else
            {
                await _collectionCache.InvalidateAsync(
                    researcher.PersonelId,
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            response.IsSaved = false;
            response.IsCached = false;
            if (response.Messages.Count > persistenceMessageStart)
            {
                response.Messages.RemoveRange(
                    persistenceMessageStart,
                    response.Messages.Count - persistenceMessageStart);
            }
            response.Messages.Add(
                $"[HATA] YÖKSİS yayınları veritabanına yazılamadı: " +
                exception.Message);
        }

        YoksisCollectionService.RemoveUnrequestedResponseData(
            response,
            request);
        progress?.Report(new()
        {
            Stage = "completed",
            Message = YoksisCollectionProgressMessages.Completion(response),
            RecordCount = response.TotalRecordCount
        });
        return response;
    }

    internal static bool IsCompleteSuccessfulCollection(
        YoksisCollectResponse response)
    {
        if (response.StopReason is not null ||
            response.Categories.Any(category =>
                !category.IsSuccess || category.FailedDetailCount != 0))
        {
            return false;
        }

        return YoksisOperationCatalog.All.All(operation =>
            response.Categories.Any(category =>
                category.OperationName == operation.OperationName &&
                category.IsSuccess) &&
            (string.IsNullOrWhiteSpace(operation.DetailOperationName) ||
                response.Categories.Any(category =>
                    category.OperationName == operation.DetailOperationName &&
                    category.IsSuccess &&
                    category.FailedDetailCount == 0)));
    }

    private async Task ValidateIdentityOwnershipAsync(
        string personelId,
        string tcKimlikNo,
        CancellationToken cancellationToken)
    {
        var matches = await _dbContext.Researchers
            .AsNoTracking()
            .Where(researcher =>
                researcher.PersonelId == personelId ||
                researcher.TcKimlikNo == tcKimlikNo)
            .Select(researcher => new
            {
                researcher.PersonelId,
                researcher.TcKimlikNo
            })
            .Take(2)
            .ToListAsync(cancellationToken);

        bool hasConflict = matches.Any(researcher =>
            (!researcher.PersonelId.Equals(
                personelId,
                StringComparison.OrdinalIgnoreCase) &&
                researcher.TcKimlikNo == tcKimlikNo) ||
            (researcher.PersonelId.Equals(
                personelId,
                StringComparison.OrdinalIgnoreCase) &&
                researcher.TcKimlikNo is not null &&
                researcher.TcKimlikNo != tcKimlikNo));
        if (hasConflict)
        {
            throw new ArgumentException(
                "T.C. kimlik numarası farklı bir personel kaydıyla eşleşiyor.");
        }
    }

    private async Task<Researcher> ResolveResearcherAsync(
        Researcher requestedResearcher)
    {
        Researcher? researcher = await _researcherRepository.FindByIdentifiersAsync(
            requestedResearcher);

        if (researcher is null)
            return requestedResearcher;

        _researcherRepository.ApplyRequestValues(researcher, requestedResearcher);
        return researcher;
    }

    private static Researcher CreateResearcher(
        YoksisCollectResponse response)
    {
        YoksisOperationResult? identityCategory = response.Categories.FirstOrDefault(category =>
            category.OperationName == "getPersonelLinkV1");
        Dictionary<string, string?>? identityRecord = identityCategory?.Records.FirstOrDefault();
        Researcher? researcher = new Researcher();

        if (identityRecord is null)
        {
            return researcher;
        }

        researcher.FirstName = Get(identityRecord, "PERSONEL_ADI");
        researcher.LastName = Get(identityRecord, "PERSONEL_SOYADI");
        researcher.AcademicTitle = Get(identityRecord, "KADRO_UNVAN_ADI");
        researcher.Department = Get(identityRecord, "KADRO_YERI");
        return researcher;
    }

    private static string? Get(
        Dictionary<string, string?> record,
        string fieldName)
    {
        string? value = record.GetValueOrDefault(fieldName);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string CreateDisplayName(Researcher researcher)
    {
        string? displayName = string.Join(
            " ",
            new[] { researcher.FirstName, researcher.LastName }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        return string.IsNullOrWhiteSpace(displayName)
            ? "Akademisyen"
            : displayName;
    }
}
