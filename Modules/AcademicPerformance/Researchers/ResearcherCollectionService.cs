using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.GoogleScholar;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works;
using Microsoft.Extensions.Configuration;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;

public sealed class ResearcherCollectionService
{
    private const int DefaultMaxAgeHours = 24;

    private readonly OpenAlexClient _openAlexClient;
    private readonly GoogleScholarClient _googleScholarClient;
    private readonly WebOfScienceClient _webOfScienceClient;
    private readonly AcademicWorkCategorizer _academicWorkCategorizer;
    private readonly ResearcherCollectionFeedback _collectionFeedback;
    private readonly IConfiguration _configuration;
    private readonly TimeSpan _providerCacheMaxAge;

    public ResearcherCollectionService(
        OpenAlexClient openAlexClient,
        GoogleScholarClient googleScholarClient,
        WebOfScienceClient webOfScienceClient,
        AcademicWorkCategorizer academicWorkCategorizer,
        ResearcherCollectionFeedback collectionFeedback,
        IConfiguration configuration)
    {
        int maxAgeHours = 0;

        _openAlexClient = openAlexClient;
        _googleScholarClient = googleScholarClient;
        _webOfScienceClient = webOfScienceClient;
        _academicWorkCategorizer = academicWorkCategorizer;
        _collectionFeedback = collectionFeedback;
        _configuration = configuration;

        if (!int.TryParse(
                configuration["ProviderCache:MaxAgeHours"],
                out maxAgeHours) ||
            maxAgeHours <= 0)
        {
            maxAgeHours = DefaultMaxAgeHours;
        }

        _providerCacheMaxAge = TimeSpan.FromHours(maxAgeHours);
    }

    public async Task CollectAsync(
        Researcher researcher,
        Researcher requestedIdentifiers,
        List<string> messages)
    {
        await CollectOpenAlexAsync(researcher, requestedIdentifiers.Orcid, messages);
        await CollectGoogleScholarAsync(
            researcher,
            requestedIdentifiers.GoogleScholarId,
            messages);
        await CollectWebOfScienceAsync(
            researcher,
            requestedIdentifiers.WebOfScienceResearcherId,
            messages);
        _academicWorkCategorizer.Categorize(researcher);
        _collectionFeedback.Add(researcher, requestedIdentifiers, messages);
    }

    private async Task CollectOpenAlexAsync(
        Researcher researcher,
        string? requestedOrcid,
        List<string> messages)
    {
        if (string.IsNullOrWhiteSpace(requestedOrcid))
        {
            AddMessage(messages, "[ATLANDI] OpenAlex: ORCID verilmedi.");
            return;
        }

        if (IdentifiersMatch(researcher.Orcid, requestedOrcid) &&
            IsProviderDataCurrent(researcher.OpenAlex?.LastUpdatedAt) &&
            HasCompleteOpenAlexRawData(researcher.OpenAlex))
        {
            AddCachedDataMessage(
                messages,
                "OpenAlex",
                researcher.OpenAlex?.LastUpdatedAt);
            return;
        }

        AddMessage(messages, $"[İŞLEM] OpenAlex sorgulanıyor: {researcher.Orcid}");

        try
        {
            await _openAlexClient.FillResearcherAsync(researcher);

            if (researcher.OpenAlex is not null)
            {
                researcher.OpenAlex.LastUpdatedAt = DateTime.UtcNow;
            }
        }
        catch (ArgumentException exception)
        {
            AddMessage(messages, $"[HATA] Geçersiz ORCID: {exception.Message}");
        }
        catch (HttpRequestException exception)
        {
            AddMessage(messages, $"[HATA] OpenAlex'e bağlanılamadı: {exception.Message}");
        }
        catch (Exception exception)
        {
            AddMessage(messages, $"[HATA] OpenAlex: {exception.Message}");
        }
    }

    private async Task CollectGoogleScholarAsync(
        Researcher researcher,
        string? requestedScholarId,
        List<string> messages)
    {
        string? serpApiKey = null;

        if (string.IsNullOrWhiteSpace(requestedScholarId))
        {
            AddMessage(
                messages,
                "[ATLANDI] Google Scholar: ID verilmedi.");
            return;
        }

        if (IdentifiersMatch(researcher.GoogleScholar?.ScholarId, requestedScholarId) &&
            IsProviderDataCurrent(researcher.GoogleScholar?.LastUpdatedAt) &&
            HasCompleteGoogleScholarRawData(researcher.GoogleScholar))
        {
            AddCachedDataMessage(
                messages,
                "Google Scholar",
                researcher.GoogleScholar?.LastUpdatedAt);
            return;
        }

        serpApiKey = _configuration["SerpApi:ApiKey"];

        if (string.IsNullOrWhiteSpace(serpApiKey))
        {
            AddMessage(
                messages,
                "[HATA] Google Scholar: SerpAPI anahtarı bulunamadı.");
            return;
        }

        AddMessage(
            messages,
            $"[İŞLEM] Google Scholar sorgulanıyor: {researcher.GoogleScholarId}");

        try
        {
            researcher.GoogleScholar = await _googleScholarClient.GetAuthorAsync(
                researcher.GoogleScholarId);
            researcher.GoogleScholar.LastUpdatedAt = DateTime.UtcNow;
        }
        catch (ArgumentException exception)
        {
            AddMessage(
                messages,
                $"[HATA] Geçersiz Google Scholar ID: {exception.Message}");
        }
        catch (HttpRequestException exception)
        {
            AddMessage(
                messages,
                $"[HATA] Google Scholar'a bağlanılamadı: {exception.Message}");
        }
        catch (Exception exception)
        {
            AddMessage(messages, $"[HATA] Google Scholar: {exception.Message}");
        }
    }

    private async Task CollectWebOfScienceAsync(
        Researcher researcher,
        string? requestedResearcherId,
        List<string> messages)
    {
        string? clarivateApiKey = null;

        if (string.IsNullOrWhiteSpace(requestedResearcherId))
        {
            AddMessage(
                messages,
                "[ATLANDI] Web of Science: ResearcherID verilmedi.");
            return;
        }

        if (IdentifiersMatch(researcher.WebOfScience?.Rid, requestedResearcherId) &&
            IsProviderDataCurrent(researcher.WebOfScience?.LastUpdatedAt))
        {
            AddCachedDataMessage(
                messages,
                "Web of Science",
                researcher.WebOfScience?.LastUpdatedAt);
            return;
        }

        clarivateApiKey = _configuration["Clarivate:ApiKey"];

        if (string.IsNullOrWhiteSpace(clarivateApiKey))
        {
            AddMessage(
                messages,
                "[HATA] Web of Science: Clarivate API anahtarı bulunamadı.");
            return;
        }

        AddMessage(
            messages,
            $"[İŞLEM] Web of Science sorgulanıyor: " +
            $"{researcher.WebOfScienceResearcherId}");

        try
        {
            researcher.WebOfScience = await _webOfScienceClient.GetResearcherAsync(
                researcher.WebOfScienceResearcherId);
            researcher.WebOfScience.LastUpdatedAt = DateTime.UtcNow;
        }
        catch (ArgumentException exception)
        {
            AddMessage(
                messages,
                $"[HATA] Geçersiz Web of Science ResearcherID: {exception.Message}");
        }
        catch (HttpRequestException exception)
        {
            AddMessage(
                messages,
                $"[HATA] Web of Science'a bağlanılamadı: {exception.Message}");
        }
        catch (Exception exception)
        {
            AddMessage(messages, $"[HATA] Web of Science: {exception.Message}");
        }
    }

    private static void AddMessage(List<string> messages, string message)
    {
        messages.Add(message);
        messages.Add(string.Empty);
    }

    private bool IsProviderDataCurrent(DateTime? lastUpdatedAt)
    {
        DateTime oldestAcceptedUpdate = DateTime.UtcNow - _providerCacheMaxAge;
        DateTime lastUpdateUtc = DateTime.MinValue;

        if (!lastUpdatedAt.HasValue)
        {
            return false;
        }

        lastUpdateUtc = lastUpdatedAt.Value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(lastUpdatedAt.Value, DateTimeKind.Utc)
            : lastUpdatedAt.Value.ToUniversalTime();

        return lastUpdateUtc >= oldestAcceptedUpdate;
    }

    private static bool IdentifiersMatch(string? firstIdentifier, string? secondIdentifier)
    {
        return !string.IsNullOrWhiteSpace(firstIdentifier) &&
               !string.IsNullOrWhiteSpace(secondIdentifier) &&
               firstIdentifier.Equals(
                   secondIdentifier,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasCompleteOpenAlexRawData(OpenAlexData? openAlexData)
    {
        int index = 0;
        OpenAlexWork? work = null;

        if (openAlexData is null ||
            string.IsNullOrWhiteSpace(openAlexData.RawDataJson) ||
            string.IsNullOrWhiteSpace(openAlexData.WorksResponsePagesJson) ||
            openAlexData.Works is null)
        {
            return false;
        }

        for (index = 0; index < openAlexData.Works.Count; index++)
        {
            work = openAlexData.Works[index];

            if (string.IsNullOrWhiteSpace(work.RawDataJson))
            {
                return false;
            }
        }

        return true;
    }

    private bool HasCompleteGoogleScholarRawData(
        GoogleScholarData? googleScholarData)
    {
        bool collectArticleDetails = false;
        int index = 0;
        GoogleScholarWork? work = null;

        if (googleScholarData is null ||
            string.IsNullOrWhiteSpace(googleScholarData.RawDataJson) ||
            string.IsNullOrWhiteSpace(googleScholarData.ResponsePagesJson) ||
            googleScholarData.Works is null)
        {
            return false;
        }

        bool.TryParse(
            _configuration["GoogleScholar:CollectArticleDetails"],
            out collectArticleDetails);

        for (index = 0; index < googleScholarData.Works.Count; index++)
        {
            work = googleScholarData.Works[index];

            if (string.IsNullOrWhiteSpace(work.RawDataJson))
            {
                return false;
            }

            if (collectArticleDetails &&
                !string.IsNullOrWhiteSpace(work.CitationId) &&
                string.IsNullOrWhiteSpace(work.DetailRawDataJson))
            {
                return false;
            }
        }

        return true;
    }

    private static void AddCachedDataMessage(
        List<string> messages,
        string providerName,
        DateTime? lastUpdatedAt)
    {
        string? localUpdateTime = null;
        DateTime updateTimeUtc = DateTime.MinValue;

        if (lastUpdatedAt.HasValue)
        {
            updateTimeUtc = lastUpdatedAt.Value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(lastUpdatedAt.Value, DateTimeKind.Utc)
                : lastUpdatedAt.Value.ToUniversalTime();
            localUpdateTime = updateTimeUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
        }

        AddMessage(
            messages,
            $"[ÖNBELLEK] {providerName} verisi güncel; API sorgusu yapılmadı. " +
            $"Son güncelleme: {localUpdateTime}");
    }
}
