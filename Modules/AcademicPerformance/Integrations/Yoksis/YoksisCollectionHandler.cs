using System.Text.RegularExpressions;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis;

public sealed class YoksisCollectionHandler
{
    private static readonly Regex OrcidPattern = new(
        @"^\d{4}-\d{4}-\d{4}-\d{3}[\dX]$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex WebOfScienceResearcherIdPattern = new(
        @"^[A-Z]{1,3}-\d{4}-\d{4}$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly YoksisCollectionService _collectionService;
    private readonly YoksisAcademicWorkSynchronizer _workSynchronizer;
    private readonly ResearcherRepository _researcherRepository;
    private readonly PublicationSummarySynchronizer _summarySynchronizer;
    private readonly AcademicDatabaseInitializer _databaseInitializer;

    public YoksisCollectionHandler(
        YoksisCollectionService collectionService,
        YoksisAcademicWorkSynchronizer workSynchronizer,
        ResearcherRepository researcherRepository,
        PublicationSummarySynchronizer summarySynchronizer,
        AcademicDatabaseInitializer databaseInitializer)
    {
        _collectionService = collectionService;
        _workSynchronizer = workSynchronizer;
        _researcherRepository = researcherRepository;
        _summarySynchronizer = summarySynchronizer;
        _databaseInitializer = databaseInitializer;
    }

    public async Task<YoksisCollectResponse> CollectAsync(
        YoksisCollectRequest request)
    {
        YoksisCollectResponse? response = null;

        await _databaseInitializer.EnsureReadyAsync();
        response = await _collectionService.CollectAsync(request);

        try
        {
            Researcher? researcher = null;
            Researcher? requestedResearcher = null;
            int publicationCount = 0;

            requestedResearcher = CreateResearcher(response);
            researcher = await ResolveResearcherAsync(
                request.ResearcherId,
                requestedResearcher);
            await _researcherRepository.SaveAsync(researcher);
            publicationCount = await _workSynchronizer.SyncAsync(
                researcher.Id,
                response);

            response.ResearcherId = researcher.Id;
            response.ResearcherDisplayName = CreateDisplayName(researcher);
            response.YoksisPublicationCount = publicationCount;
            response.PublicationSummaryCount =
                await _summarySynchronizer.SyncAsync(researcher.Id);
            response.IsSaved = true;
            response.Messages.Add(
                $"[OK] YÖKSİS yayınları: {publicationCount} kayıt " +
                "ortak yayın tablosuna yazıldı.");
            response.Messages.Add(
                $"[OK] Yayın özeti: {response.PublicationSummaryCount} " +
                "benzersiz yayın hazırlandı.");
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
        return response;
    }

    private async Task<Researcher> ResolveResearcherAsync(
        int? requestedResearcherId,
        Researcher requestedResearcher)
    {
        Researcher? researcher = null;

        if (requestedResearcherId.HasValue && requestedResearcherId.Value > 0)
        {
            researcher = await _researcherRepository.FindByIdAsync(
                requestedResearcherId.Value);

            if (researcher is null)
            {
                throw new ArgumentException(
                    "YÖKSİS verisinin bağlanacağı akademisyen bulunamadı.");
            }

            _researcherRepository.ApplyRequestValues(
                researcher,
                requestedResearcher);
            return researcher;
        }

        if (string.IsNullOrWhiteSpace(requestedResearcher.YoksisResearcherId) &&
            string.IsNullOrWhiteSpace(requestedResearcher.Orcid) &&
            string.IsNullOrWhiteSpace(
                requestedResearcher.WebOfScienceResearcherId))
        {
            throw new InvalidOperationException(
                "YÖKSİS personel yanıtında akademisyeni güvenle " +
                "eşleştirecek Araştırmacı ID, ORCID veya ResearcherID gelmedi.");
        }

        researcher = await _researcherRepository.FindByIdentifiersAsync(
            requestedResearcher);

        if (researcher is null)
        {
            return requestedResearcher;
        }

        _researcherRepository.ApplyRequestValues(researcher, requestedResearcher);
        return researcher;
    }

    private static Researcher CreateResearcher(
        YoksisCollectResponse response)
    {
        YoksisOperationResult? identityCategory = null;
        Dictionary<string, string?>? identityRecord = null;
        Researcher? researcher = null;

        identityCategory = response.Categories.FirstOrDefault(category =>
            category.OperationName == "getPersonelLinkV1");
        identityRecord = identityCategory?.Records.FirstOrDefault();
        researcher = new Researcher();

        if (identityRecord is null)
        {
            return researcher;
        }

        researcher.YoksisResearcherId = Get(identityRecord, "ARASTIRMACI_ID");
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
        string? normalized = null;

        normalized = value?.Trim();
        return !string.IsNullOrWhiteSpace(normalized) &&
            OrcidPattern.IsMatch(normalized)
            ? normalized.ToUpperInvariant()
            : null;
    }

    private static string? NormalizeResearcherId(string? value)
    {
        string? normalized = null;

        normalized = value?.Trim();
        return !string.IsNullOrWhiteSpace(normalized) &&
            WebOfScienceResearcherIdPattern.IsMatch(normalized)
            ? normalized.ToUpperInvariant()
            : null;
    }

    private static string? Get(
        Dictionary<string, string?> record,
        string fieldName)
    {
        string? value = null;

        value = record.GetValueOrDefault(fieldName);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string CreateDisplayName(Researcher researcher)
    {
        string? displayName = null;

        displayName = string.Join(
            " ",
            new[] { researcher.FirstName, researcher.LastName }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        return string.IsNullOrWhiteSpace(displayName)
            ? "Akademisyen"
            : displayName;
    }
}
