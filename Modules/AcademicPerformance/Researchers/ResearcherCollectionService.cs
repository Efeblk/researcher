using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Orcid;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;

public sealed class ResearcherCollectionService
{
    private const int DefaultMaxAgeHours = 24;

    private readonly OrcidClient _orcidClient;
    private readonly WebOfScienceClient _webOfScienceClient;
    private readonly AcademicWorkCategorizer _academicWorkCategorizer;
    private readonly ResearcherCollectionFeedback _collectionFeedback;
    private readonly TimeSpan _providerCacheMaxAge;

    public ResearcherCollectionService(
        OrcidClient orcidClient,
        WebOfScienceClient webOfScienceClient,
        AcademicWorkCategorizer academicWorkCategorizer,
        ResearcherCollectionFeedback collectionFeedback,
        IConfiguration configuration)
    {
        int maxAgeHours = 0;

        _orcidClient = orcidClient;
        _webOfScienceClient = webOfScienceClient;
        _academicWorkCategorizer = academicWorkCategorizer;
        _collectionFeedback = collectionFeedback;

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
        await CollectOrcidAsync(researcher, requestedIdentifiers.Orcid, messages);
        await CollectWebOfScienceAsync(
            researcher,
            requestedIdentifiers.WebOfScienceResearcherId,
            messages);
        _academicWorkCategorizer.Categorize(researcher);
        _collectionFeedback.Add(researcher, requestedIdentifiers, messages);
    }

    private async Task CollectWebOfScienceAsync(
        Researcher researcher,
        string? requestedResearcherId,
        List<string> messages)
    {
        if (string.IsNullOrWhiteSpace(requestedResearcherId))
        {
            AddMessage(
                messages,
                "[ATLANDI] Web of Science: ResearcherID verilmedi.");
            return;
        }

        if (IdentifiersMatch(
                researcher.WebOfScienceResearcherId,
                requestedResearcherId) &&
            IsProviderDataCurrent(
                researcher.WebOfScienceProfile?.LastUpdatedAt) &&
            HasCompleteWebOfScienceRawData(researcher.WebOfScienceProfile))
        {
            AddCachedDataMessage(
                messages,
                "Web of Science",
                researcher.WebOfScienceProfile?.LastUpdatedAt);
            return;
        }

        AddMessage(
            messages,
            $"[İŞLEM] Web of Science Starter API v1 sorgulanıyor: " +
            $"{requestedResearcherId}");

        try
        {
            await _webOfScienceClient.FillResearcherAsync(
                researcher,
                requestedResearcherId);
        }
        catch (ArgumentException exception)
        {
            AddMessage(
                messages,
                $"[HATA] Geçersiz Web of Science ResearcherID: " +
                $"{exception.Message}");
        }
        catch (HttpRequestException exception)
        {
            AddMessage(
                messages,
                $"[HATA] Web of Science API'ye bağlanılamadı: " +
                $"{exception.Message}");
        }
        catch (Exception exception)
        {
            AddMessage(messages, $"[HATA] Web of Science: {exception.Message}");
        }
    }

    private async Task CollectOrcidAsync(
        Researcher researcher,
        string? requestedOrcid,
        List<string> messages)
    {
        if (string.IsNullOrWhiteSpace(requestedOrcid))
        {
            AddMessage(messages, "[ATLANDI] ORCID: kimlik verilmedi.");
            return;
        }

        if (IdentifiersMatch(researcher.Orcid, requestedOrcid) &&
            IsProviderDataCurrent(researcher.OrcidProfile?.LastUpdatedAt) &&
            HasCompleteOrcidRawData(researcher.OrcidProfile))
        {
            AddCachedDataMessage(
                messages,
                "ORCID",
                researcher.OrcidProfile?.LastUpdatedAt);
            return;
        }

        AddMessage(messages, $"[İŞLEM] Resmî ORCID API sorgulanıyor: {researcher.Orcid}");

        try
        {
            await _orcidClient.FillResearcherAsync(researcher);
        }
        catch (ArgumentException exception)
        {
            AddMessage(messages, $"[HATA] Geçersiz ORCID: {exception.Message}");
        }
        catch (HttpRequestException exception)
        {
            AddMessage(messages, $"[HATA] ORCID API'ye bağlanılamadı: {exception.Message}");
        }
        catch (Exception exception)
        {
            AddMessage(messages, $"[HATA] ORCID: {exception.Message}");
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

    private static bool HasCompleteOrcidRawData(OrcidProfile? profile)
    {
        int index = 0;
        OrcidWork? work = null;

        if (profile is null ||
            string.IsNullOrWhiteSpace(profile.RawDataJson) ||
            profile.Works is null)
        {
            return false;
        }

        for (index = 0; index < profile.Works.Count; index++)
        {
            work = profile.Works[index];

            if (string.IsNullOrWhiteSpace(work.RawDataJson))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasCompleteWebOfScienceRawData(
        WebOfScienceProfile? profile)
    {
        int index = 0;
        WebOfScienceWork? work = null;

        if (profile is null ||
            !HasBothWebOfScienceDatabaseResponses(
                profile.DocumentPagesJson) ||
            profile.Works is null)
        {
            return false;
        }

        for (index = 0; index < profile.Works.Count; index++)
        {
            work = profile.Works[index];

            if (string.IsNullOrWhiteSpace(work.RawDataJson))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasBothWebOfScienceDatabaseResponses(
        string? documentPagesJson)
    {
        JsonDocument? document = null;
        JsonElement root = default;
        JsonElement coreCollectionPages = default;
        JsonElement allDatabasePages = default;

        if (string.IsNullOrWhiteSpace(documentPagesJson))
        {
            return false;
        }

        try
        {
            document = JsonDocument.Parse(documentPagesJson);
            root = document.RootElement;

            return root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("WOS", out coreCollectionPages) &&
                coreCollectionPages.ValueKind == JsonValueKind.Array &&
                root.TryGetProperty("WOK", out allDatabasePages) &&
                allDatabasePages.ValueKind == JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
        finally
        {
            document?.Dispose();
        }
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
