using System.Text.RegularExpressions;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis.Persistence;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Persistence;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;
using Microsoft.EntityFrameworkCore.Storage;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.RateLimiting;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis.Collection;

public sealed class YoksisCollectionHandler
{
    private static readonly Regex OrcidPattern = new(
        @"^\d{4}-\d{4}-\d{4}-\d{3}[\dX]$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex WebOfScienceResearcherIdPattern = new(
        @"^[A-Z]{1,3}-\d{4}-\d{4}$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly YoksisCollectionService _collectionService;
    private readonly YoksisRecordSynchronizer _recordSynchronizer;
    private readonly YoksisAcademicWorkSynchronizer _workSynchronizer;
    private readonly ResearcherRepository _researcherRepository;
    private readonly PublicationSummarySynchronizer _summarySynchronizer;
    private readonly AcademicDbContext _dbContext;
    private readonly CanonicalWorkSynchronizer _canonicalWorkSynchronizer;

    public YoksisCollectionHandler(
        YoksisCollectionService collectionService,
        YoksisRecordSynchronizer recordSynchronizer,
        YoksisAcademicWorkSynchronizer workSynchronizer,
        ResearcherRepository researcherRepository,
        PublicationSummarySynchronizer summarySynchronizer,
        AcademicDbContext dbContext,
        CanonicalWorkSynchronizer? canonicalWorkSynchronizer = null)
    {
        _collectionService = collectionService;
        _recordSynchronizer = recordSynchronizer;
        _workSynchronizer = workSynchronizer;
        _researcherRepository = researcherRepository;
        _summarySynchronizer = summarySynchronizer;
        _dbContext = dbContext;
        _canonicalWorkSynchronizer = canonicalWorkSynchronizer ?? new CanonicalWorkSynchronizer(dbContext);
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

        using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, ProviderCallScope.Cancellation);
        cancellationToken = linkedCancellation.Token;
        progress?.Report(new() { Stage = "starting", Message = "YÖKSİS toplaması başlatıldı." });
        YoksisCollectResponse? response = await _collectionService.CollectAsync(
            request, progress, cancellationToken);

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
            await transaction.CommitAsync(cancellationToken);
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
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            response.IsSaved = false;
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
            Message = response.IsSaved ? "YÖKSİS toplaması tamamlandı." : "YÖKSİS kayıt aşaması başarısız oldu.",
            RecordCount = response.TotalRecordCount
        });
        return response;
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

        researcher.Orcid = NormalizeOrcid(Get(identityRecord, "ORCID"));
        researcher.WebOfScienceResearcherId = NormalizeResearcherId(
            Get(identityRecord, "RESEARCHER_ID"));
        researcher.FirstName = Get(identityRecord, "PERSONEL_ADI");
        researcher.LastName = Get(identityRecord, "PERSONEL_SOYADI");
        researcher.AcademicTitle = Get(identityRecord, "KADRO_UNVAN_ADI");
        researcher.Department = Get(identityRecord, "KADRO_YERI");
        return researcher;
    }

    private static string? NormalizeOrcid(string? value)
    {
        string? normalized = value?.Trim();
        return !string.IsNullOrWhiteSpace(normalized) &&
            OrcidPattern.IsMatch(normalized)
            ? normalized.ToUpperInvariant()
            : null;
    }

    private static string? NormalizeResearcherId(string? value)
    {
        string? normalized = value?.Trim();
        return !string.IsNullOrWhiteSpace(normalized) &&
            WebOfScienceResearcherIdPattern.IsMatch(normalized)
            ? normalized.ToUpperInvariant()
            : null;
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
