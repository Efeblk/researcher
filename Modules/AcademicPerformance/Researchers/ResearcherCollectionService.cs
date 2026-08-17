using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.GoogleScholar;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Scopus;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
using Microsoft.Extensions.Configuration;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;

public sealed class ResearcherCollectionService
{
    private const int DefaultMaxAgeHours = 24;

    private readonly OpenAlexClient _openAlexClient;
    private readonly GoogleScholarClient _googleScholarClient;
    private readonly ScopusClient _scopusClient;
    private readonly WebOfScienceClient _webOfScienceClient;
    private readonly IConfiguration _configuration;
    private readonly TimeSpan _providerCacheMaxAge;

    public ResearcherCollectionService(
        OpenAlexClient openAlexClient,
        GoogleScholarClient googleScholarClient,
        ScopusClient scopusClient,
        WebOfScienceClient webOfScienceClient,
        IConfiguration configuration)
    {
        int maxAgeHours = 0;

        _openAlexClient = openAlexClient;
        _googleScholarClient = googleScholarClient;
        _scopusClient = scopusClient;
        _webOfScienceClient = webOfScienceClient;
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
        await CollectScopusAsync(
            researcher,
            requestedIdentifiers.ScopusAuthorId,
            messages);
        await CollectWebOfScienceAsync(
            researcher,
            requestedIdentifiers.WebOfScienceResearcherId,
            messages);
    }

    private async Task CollectOpenAlexAsync(
        Researcher researcher,
        string? requestedOrcid,
        List<string> messages)
    {
        if (string.IsNullOrWhiteSpace(requestedOrcid))
        {
            AddMessage(messages, "Bu istekte ORCID verilmediği için OpenAlex sorgusu yapılmadı.");
            return;
        }

        if (IdentifiersMatch(researcher.Orcid, requestedOrcid) &&
            IsProviderDataCurrent(researcher.OpenAlex?.LastUpdatedAt))
        {
            AddCachedDataMessage(
                messages,
                "OpenAlex",
                researcher.OpenAlex?.LastUpdatedAt);
            return;
        }

        AddMessage(messages, $"ORCID sorgulanıyor: {researcher.Orcid}");

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
            AddMessage(messages, $"Geçersiz ORCID: {exception.Message}");
        }
        catch (HttpRequestException exception)
        {
            AddMessage(messages, $"OpenAlex'e bağlanılamadı: {exception.Message}");
        }
        catch (Exception exception)
        {
            AddMessage(messages, $"OpenAlex hatası: {exception.Message}");
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
                "Bu istekte Google Scholar ID verilmediği için sorgu yapılmadı.");
            return;
        }

        if (IdentifiersMatch(researcher.GoogleScholar?.ScholarId, requestedScholarId) &&
            IsProviderDataCurrent(researcher.GoogleScholar?.LastUpdatedAt))
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
                "SerpAPI anahtarı bulunmadığı için Google Scholar sorgusu yapılmadı.");
            return;
        }

        AddMessage(messages, $"Google Scholar ID sorgulanıyor: {researcher.GoogleScholarId}");

        try
        {
            researcher.GoogleScholar = await _googleScholarClient.GetAuthorAsync(
                researcher.GoogleScholarId);
            researcher.GoogleScholar.LastUpdatedAt = DateTime.UtcNow;
        }
        catch (ArgumentException exception)
        {
            AddMessage(messages, $"Geçersiz Google Scholar ID: {exception.Message}");
        }
        catch (HttpRequestException exception)
        {
            AddMessage(messages, $"Google Scholar'a bağlanılamadı: {exception.Message}");
        }
        catch (Exception exception)
        {
            AddMessage(messages, $"Google Scholar hatası: {exception.Message}");
        }
    }

    private async Task CollectScopusAsync(
        Researcher researcher,
        string? requestedScopusAuthorId,
        List<string> messages)
    {
        string? elsevierApiKey = null;

        if (string.IsNullOrWhiteSpace(requestedScopusAuthorId))
        {
            AddMessage(
                messages,
                "Bu istekte Scopus Author ID verilmediği için sorgu yapılmadı.");
            return;
        }

        if (IdentifiersMatch(researcher.Scopus?.AuthorId, requestedScopusAuthorId) &&
            IsProviderDataCurrent(researcher.Scopus?.LastUpdatedAt))
        {
            AddCachedDataMessage(
                messages,
                "Scopus",
                researcher.Scopus?.LastUpdatedAt);
            return;
        }

        elsevierApiKey = _configuration["Elsevier:ApiKey"];

        if (string.IsNullOrWhiteSpace(elsevierApiKey))
        {
            AddMessage(
                messages,
                "Elsevier API anahtarı bulunmadığı için Scopus sorgusu yapılmadı.");
            return;
        }

        AddMessage(messages, $"Scopus Author ID sorgulanıyor: {researcher.ScopusAuthorId}");

        try
        {
            researcher.Scopus = await _scopusClient.GetAuthorAsync(researcher.ScopusAuthorId);
            researcher.Scopus.LastUpdatedAt = DateTime.UtcNow;
        }
        catch (ArgumentException exception)
        {
            AddMessage(messages, $"Geçersiz Scopus Author ID: {exception.Message}");
        }
        catch (HttpRequestException exception)
        {
            AddMessage(messages, $"Scopus'a bağlanılamadı: {exception.Message}");
        }
        catch (Exception exception)
        {
            AddMessage(messages, $"Scopus hatası: {exception.Message}");
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
                "Bu istekte Web of Science ResearcherID verilmediği için sorgu yapılmadı.");
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
                "Clarivate API anahtarı bulunmadığı için Web of Science sorgusu yapılmadı.");
            return;
        }

        AddMessage(
            messages,
            $"Web of Science ResearcherID sorgulanıyor: {researcher.WebOfScienceResearcherId}");

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
                $"Geçersiz Web of Science ResearcherID: {exception.Message}");
        }
        catch (HttpRequestException exception)
        {
            AddMessage(messages, $"Web of Science'a bağlanılamadı: {exception.Message}");
        }
        catch (Exception exception)
        {
            AddMessage(messages, $"Web of Science hatası: {exception.Message}");
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
            $"{providerName} verisi güncel. API sorgusu yapılmadı. " +
            $"Son güncelleme: {localUpdateTime}");
    }
}
