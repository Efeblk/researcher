using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Orcid;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.GoogleScholar;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.TrDizin;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Scopus;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.RateLimiting;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Collection;

public sealed class ResearcherCollectionService
{
    private const int DefaultMaxAgeHours = 24;

    private readonly OrcidClient _orcidClient;
    private readonly GoogleScholarClient _googleScholarClient;
    private readonly OpenAlexClient _openAlexClient;
    private readonly WebOfScienceClient _webOfScienceClient;
    private readonly TrDizinClient _trDizinClient;
    private readonly ScopusClient? _scopusClient;
    private readonly AcademicWorkCategorizer _academicWorkCategorizer;
    private readonly ResearcherCollectionFeedback _collectionFeedback;
    private readonly TimeSpan _providerCacheMaxAge;
    private readonly IReadOnlyDictionary<string, bool> _providerEnabled;

    public ResearcherCollectionService(
        OrcidClient orcidClient,
        GoogleScholarClient googleScholarClient,
        OpenAlexClient openAlexClient,
        WebOfScienceClient webOfScienceClient,
        TrDizinClient trDizinClient,
        AcademicWorkCategorizer academicWorkCategorizer,
        ResearcherCollectionFeedback collectionFeedback,
        IConfiguration configuration,
        ScopusClient? scopusClient = null)
    {
        int maxAgeHours = 0;

        _orcidClient = orcidClient;
        _googleScholarClient = googleScholarClient;
        _openAlexClient = openAlexClient;
        _webOfScienceClient = webOfScienceClient;
        _trDizinClient = trDizinClient;
        _scopusClient = scopusClient;
        _academicWorkCategorizer = academicWorkCategorizer;
        _collectionFeedback = collectionFeedback;
        _providerEnabled = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["ORCID"] = configuration.GetValue("ProviderRequestLimits:Orcid:Enabled", true),
            ["OpenAlex"] = configuration.GetValue("ProviderRequestLimits:OpenAlex:Enabled", true),
            ["Google Scholar"] = configuration.GetValue("ProviderRequestLimits:SearchApi:Enabled", true),
            ["Web of Science"] = configuration.GetValue("ProviderRequestLimits:WebOfScience:Enabled", true),
            ["Scopus"] = configuration.GetValue("ProviderRequestLimits:Scopus:Enabled", true),
            ["TR Dizin"] = configuration.GetValue("ProviderRequestLimits:TrDizin:Enabled", true)
        };

        if (!int.TryParse(
                configuration["ProviderCache:MaxAgeHours"],
                out maxAgeHours) ||
            maxAgeHours <= 0)
        {
            maxAgeHours = DefaultMaxAgeHours;
        }

        _providerCacheMaxAge = TimeSpan.FromHours(maxAgeHours);
    }

    public async Task<List<ProviderCollectionFeedback>> CollectAsync(
        Researcher researcher,
        Researcher requestedIdentifiers,
        List<string> messages)
    {
        List<ProviderCollectionFeedback> feedback = [];
        await CollectOrcidAsync(researcher, requestedIdentifiers.Orcid, messages, feedback);
        await CollectOpenAlexAsync(
            researcher,
            requestedIdentifiers.Orcid,
            messages, feedback);
        await CollectTrDizinAsync(researcher, requestedIdentifiers.Orcid, messages, feedback);
        await CollectGoogleScholarAsync(
            researcher,
            requestedIdentifiers.GoogleScholarId,
            messages, feedback);
        await CollectWebOfScienceAsync(
            researcher,
            requestedIdentifiers.WebOfScienceResearcherId,
            messages, feedback);
        await CollectScopusAsync(
            researcher, requestedIdentifiers.ScopusId, messages, feedback);
        _academicWorkCategorizer.Categorize(researcher);
        _collectionFeedback.Add(researcher, requestedIdentifiers, messages, feedback);
        return feedback;
    }

    private async Task CollectScopusAsync(
        Researcher researcher,
        string? requestedScopusId,
        List<string> messages,
        List<ProviderCollectionFeedback> feedback)
    {
        ProviderCollectionFeedback item = NewFeedback(feedback, "Scopus");
        if (SkipDisabled(item, "Scopus", messages))
            return;
        if (string.IsNullOrWhiteSpace(requestedScopusId))
        {
            Skip(item, "MissingIdentifier", "Scopus kimliği verilmedi.");
            AddMessage(messages, "[ATLANDI] Scopus: kimlik verilmedi.");
            return;
        }
        if (IdentifiersMatch(researcher.ScopusId, requestedScopusId) &&
            IsProviderDataCurrent(researcher.ScopusProfile?.LastUpdatedAt) &&
            HasCompleteScopusRawData(researcher.ScopusProfile))
        {
            Cached(item, researcher.ScopusProfile?.Works?.Count ?? 0,
                researcher.ScopusProfile?.DocumentsCount);
            AddCachedDataMessage(messages, "Scopus", researcher.ScopusProfile?.LastUpdatedAt);
            return;
        }
        try
        {
            if (_scopusClient is null)
                throw new InvalidOperationException();
            await _scopusClient.FillResearcherAsync(researcher, requestedScopusId);
            Success(item, researcher.ScopusProfile?.Works?.Count ?? 0,
                researcher.ScopusProfile?.DocumentsCount);
            AddMessage(messages, $"[OK] Scopus: {researcher.ScopusProfile?.Works?.Count ?? 0} publication(s) collected.");
        }
        catch (ArgumentException exception)
        {
            Fail(item, "InvalidIdentifier", "Scopus kimliği geçersiz.");
            AddMessage(messages, $"[HATA] Invalid Scopus ID: {exception.Message}");
        }
        catch (InvalidOperationException)
        {
            Fail(item, "Configuration", "Scopus bağlantı ayarı eksik.");
            AddMessage(messages, "[HATA] Scopus is unavailable because credentials or endpoint configuration are invalid.");
        }
        catch (HttpRequestException exception)
        {
            FailFromException(item, exception, "Scopus");
            AddMessage(messages, "[HATA] Scopus request failed; saved complete data was retained.");
        }
    }

    private static bool HasCompleteScopusRawData(ScopusProfile? profile) =>
        profile is not null && !string.IsNullOrWhiteSpace(profile.RawDataJson) &&
        !string.IsNullOrWhiteSpace(profile.SearchPagesJson) && profile.Works is not null &&
        profile.Works.All(work => !string.IsNullOrWhiteSpace(work.RawDataJson));

    private async Task CollectTrDizinAsync(
        Researcher researcher,
        string? requestedOrcid,
        List<string> messages, List<ProviderCollectionFeedback> feedback)
    {
        ProviderCollectionFeedback item = NewFeedback(feedback, "TR Dizin");
        if (SkipDisabled(item, "TR Dizin", messages))
            return;
        if (string.IsNullOrWhiteSpace(requestedOrcid))
        {
            Skip(item, "MissingIdentifier", "ORCID verilmedi.");
            AddMessage(messages, "[ATLANDI] TR Dizin: ORCID verilmedi.");
            return;
        }
        if (IdentifiersMatch(researcher.TrDizinProfile?.Orcid, requestedOrcid) &&
            IsProviderDataCurrent(researcher.TrDizinProfile?.LastUpdatedAt))
        {
            Cached(item, researcher.TrDizinProfile?.Works?.Count ?? 0);
            AddCachedDataMessage(
                messages,
                "TR Dizin",
                researcher.TrDizinProfile?.LastUpdatedAt);
            return;
        }
        try
        {
            TrDizinProfile? profile = await _trDizinClient.GetByOrcidAsync(requestedOrcid);
            if (profile is null)
            {
                item.Status = "NotFound";
                item.Reasons.Add(Reason("NotFound", "ORCID ile eşleşen yazar bulunamadı."));
                AddMessage(messages, "[BULUNAMADI] TR Dizin: ORCID ile eşleşen yazar yok.");
                return;
            }
            researcher.TrDizinProfile = profile;
            Success(item, profile.Works?.Count ?? 0, profile.Works?.Count ?? 0);
            AddMessage(messages, $"[OK] TR Dizin: {profile.Works?.Count ?? 0} yayın alındı.");
        }
        catch (ProviderCollectionException exception)
        {
            FailWithProgress(item, exception);
            AddMessage(messages, $"[HATA] TR Dizin: {exception.SafeDescription}");
        }
        catch (Exception exception)
        {
            Fail(item, "ProviderError", "TR Dizin isteği tamamlanamadı; eksik veri kaydedilmedi.");
            AddMessage(messages, $"[HATA] TR Dizin: {exception.Message}");
        }
    }

    private async Task CollectOpenAlexAsync(
        Researcher researcher,
        string? requestedOrcid,
        List<string> messages, List<ProviderCollectionFeedback> feedback)
    {
        ProviderCollectionFeedback item = NewFeedback(feedback, "OpenAlex");
        if (SkipDisabled(item, "OpenAlex", messages))
            return;
        if (string.IsNullOrWhiteSpace(requestedOrcid))
        {
            Skip(item, "MissingIdentifier", "ORCID verilmedi.");
            AddMessage(
                messages,
                "[ATLANDI] OpenAlex: ORCID verilmedi.");
            return;
        }

        if (IdentifiersMatch(researcher.Orcid, requestedOrcid) &&
            IsProviderDataCurrent(researcher.OpenAlexProfile?.LastUpdatedAt) &&
            HasCompleteOpenAlexRawData(researcher.OpenAlexProfile))
        {
            Cached(item, researcher.OpenAlexProfile?.Works?.Count ?? 0,
                researcher.OpenAlexProfile?.WorksCount);
            AddCachedDataMessage(
                messages,
                "OpenAlex",
                researcher.OpenAlexProfile?.LastUpdatedAt);
            return;
        }

        AddMessage(
            messages,
            $"[İŞLEM] OpenAlex verisi sorgulanıyor: {requestedOrcid}");

        try
        {
            await _openAlexClient.FillResearcherAsync(researcher);
            Success(item, researcher.OpenAlexProfile?.Works?.Count ?? 0,
                researcher.OpenAlexProfile?.WorksCount);
        }
        catch (ArgumentException exception)
        {
            Fail(item, "InvalidIdentifier", "OpenAlex kimliği geçersiz.");
            AddMessage(messages, $"[HATA] OpenAlex: {exception.Message}");
        }
        catch (ProviderCollectionException exception)
        {
            FailWithProgress(item, exception);
            AddMessage(messages, $"[HATA] OpenAlex: {exception.SafeDescription}");
        }
        catch (HttpRequestException exception)
        {
            Fail(item, "ProviderError", "OpenAlex isteği tamamlanamadı; eksik veri kaydedilmedi.");
            AddMessage(
                messages,
                $"[HATA] OpenAlex API'ye bağlanılamadı: {exception.Message}");
        }
        catch (Exception exception)
        {
            Fail(item, "ProviderError", "OpenAlex isteği tamamlanamadı; eksik veri kaydedilmedi.");
            AddMessage(messages, $"[HATA] OpenAlex: {exception.Message}");
        }
    }

    private async Task CollectGoogleScholarAsync(
        Researcher researcher,
        string? requestedGoogleScholarId,
        List<string> messages, List<ProviderCollectionFeedback> feedback)
    {
        ProviderCollectionFeedback item = NewFeedback(feedback, "Google Scholar");
        if (SkipDisabled(item, "Google Scholar", messages))
            return;
        if (string.IsNullOrWhiteSpace(requestedGoogleScholarId))
        {
            Skip(item, "MissingIdentifier", "Google Scholar kimliği verilmedi.");
            AddMessage(messages, "[ATLANDI] Google Scholar: kimlik verilmedi.");
            return;
        }

        if (IdentifiersMatch(researcher.GoogleScholarId, requestedGoogleScholarId) &&
            IsProviderDataCurrent(researcher.GoogleScholarProfile?.LastUpdatedAt) &&
            HasCompleteGoogleScholarRawData(researcher.GoogleScholarProfile))
        {
            Cached(item, researcher.GoogleScholarProfile?.Works?.Count ?? 0);
            AddCachedDataMessage(
                messages,
                "Google Scholar",
                researcher.GoogleScholarProfile?.LastUpdatedAt);
            return;
        }

        AddMessage(
            messages,
            $"[İŞLEM] SearchApi üzerinden Google Scholar sorgulanıyor: " +
            requestedGoogleScholarId);

        try
        {
            await _googleScholarClient.FillResearcherAsync(
                researcher,
                requestedGoogleScholarId);
            Success(item, researcher.GoogleScholarProfile?.Works?.Count ?? 0, null);
        }
        catch (ArgumentException exception)
        {
            Fail(item, "InvalidIdentifier", "Google Scholar kimliği geçersiz.");
            AddMessage(
                messages,
                $"[HATA] Geçersiz Google Scholar ID: {exception.Message}");
        }
        catch (HttpRequestException exception)
        {
            FailFromException(item, exception, "Google Scholar");
            AddMessage(
                messages,
                $"[HATA] SearchApi'ye bağlanılamadı: {exception.Message}");
        }
        catch (Exception exception)
        {
            if (exception is ProviderCollectionException progress)
            {
                FailWithProgress(item, progress);
                AddMessage(messages, $"[HATA] Google Scholar: {progress.SafeDescription}");
            }
            else
            {
                FailFromException(item, exception, "Google Scholar");
                AddMessage(messages, $"[HATA] Google Scholar: {exception.Message}");
            }
        }
    }

    private async Task CollectWebOfScienceAsync(
        Researcher researcher,
        string? requestedResearcherId,
        List<string> messages, List<ProviderCollectionFeedback> feedback)
    {
        ProviderCollectionFeedback item = NewFeedback(feedback, "Web of Science");
        if (SkipDisabled(item, "Web of Science", messages))
            return;
        if (string.IsNullOrWhiteSpace(requestedResearcherId))
        {
            Skip(item, "MissingIdentifier", "ResearcherID verilmedi.");
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
            Cached(item, researcher.WebOfScienceProfile?.Works?.Count ?? 0);
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
            Success(item, researcher.WebOfScienceProfile?.Works?.Count ?? 0, null);
        }
        catch (ArgumentException exception)
        {
            Fail(item, "InvalidIdentifier", "Web of Science ResearcherID geçersiz.");
            AddMessage(
                messages,
                $"[HATA] Geçersiz Web of Science ResearcherID: " +
                $"{exception.Message}");
        }
        catch (HttpRequestException exception)
        {
            FailFromException(item, exception, "Web of Science");
            AddMessage(
                messages,
                $"[HATA] Web of Science API'ye bağlanılamadı: " +
                $"{exception.Message}");
        }
        catch (Exception exception)
        {
            if (exception is ProviderCollectionException progress)
            {
                item.Unit = "database record";
                FailWithProgress(item, progress);
                AddMessage(messages, $"[HATA] Web of Science: {progress.SafeDescription}");
            }
            else
            {
                FailFromException(item, exception, "Web of Science");
                AddMessage(messages, $"[HATA] Web of Science: {exception.Message}");
            }
        }
    }

    private async Task CollectOrcidAsync(
        Researcher researcher,
        string? requestedOrcid,
        List<string> messages, List<ProviderCollectionFeedback> feedback)
    {
        ProviderCollectionFeedback item = NewFeedback(feedback, "ORCID");
        if (SkipDisabled(item, "ORCID", messages))
            return;
        if (string.IsNullOrWhiteSpace(requestedOrcid))
        {
            Skip(item, "MissingIdentifier", "ORCID verilmedi.");
            AddMessage(messages, "[ATLANDI] ORCID: kimlik verilmedi.");
            return;
        }

        if (IdentifiersMatch(researcher.Orcid, requestedOrcid) &&
            IsProviderDataCurrent(researcher.OrcidProfile?.LastUpdatedAt) &&
            HasCompleteOrcidRawData(researcher.OrcidProfile))
        {
            Cached(item, researcher.OrcidProfile?.Works?.Count ?? 0);
            AddCachedDataMessage(
                messages,
                "ORCID",
                researcher.OrcidProfile?.LastUpdatedAt);
            return;
        }

        AddMessage(messages, $"[İŞLEM] Resmî ORCID API sorgulanıyor: {researcher.Orcid}");

        try
        {
            await _orcidClient.FillResearcherAsync(
                researcher, ProviderCallScope.Cancellation);
            int retrieved = researcher.OrcidProfile?.Works?.Count ?? 0;
            Success(item, retrieved, retrieved);
        }
        catch (OperationCanceledException) when (
            ProviderCallScope.Cancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            Fail(item, exception.Message.Contains("bulunamadı", StringComparison.OrdinalIgnoreCase)
                ? "NotFound" : "InvalidIdentifier",
                exception.Message.Contains("bulunamadı", StringComparison.OrdinalIgnoreCase)
                    ? "Herkese açık ORCID kaydı bulunamadı."
                    : "ORCID geçersiz.");
            AddMessage(messages, $"[HATA] Geçersiz ORCID: {exception.Message}");
        }
        catch (ProviderCollectionException exception)
        {
            FailWithProgress(item, exception);
            AddMessage(messages, $"[HATA] ORCID: {exception.SafeDescription}");
        }
        catch (HttpRequestException exception)
        {
            FailFromException(item, exception, "ORCID");
            AddMessage(messages, $"[HATA] ORCID API'ye bağlanılamadı: {exception.Message}");
        }
        catch (Exception exception)
        {
            Fail(item, "ProviderError", "ORCID isteği tamamlanamadı; eksik veri kaydedilmedi.");
            AddMessage(messages, $"[HATA] ORCID: {exception.Message}");
        }
    }

    private static ProviderCollectionFeedback NewFeedback(
        List<ProviderCollectionFeedback> feedback, string provider)
    {
        ProviderCollectionFeedback item = new() { Provider = provider };
        feedback.Add(item);
        return item;
    }

    private bool SkipDisabled(ProviderCollectionFeedback item, string provider,
        List<string> messages)
    {
        if (_providerEnabled[provider])
            return false;
        Skip(item, "Disabled", "Yerel yapılandırmada devre dışı.");
        AddMessage(messages, $"[ATLANDI] {provider}: yerel yapılandırmada devre dışı.");
        return true;
    }

    private static ProviderCollectionReason Reason(string code, string description,
        int? affectedCount = null) => new()
    {
        Code = code, Description = description, AffectedCount = affectedCount
    };

    private static void Success(ProviderCollectionFeedback item, int retrieved, int? expected)
    {
        item.Status = "Succeeded";
        item.RetrievedCount = retrieved;
        item.ExpectedCount = expected;
    }

    private static void Cached(ProviderCollectionFeedback item, int available, int? expected = null)
    {
        item.Status = "Cached";
        item.RetrievedCount = 0;
        item.ExpectedCount = expected;
        item.Reasons.Add(Reason("Cached", $"API çağrısı yapılmadı; önbellekte {available} yayın var.", available));
    }

    private static void Skip(ProviderCollectionFeedback item, string code, string description)
    {
        item.Status = "Skipped";
        item.Reasons.Add(Reason(code, description));
    }

    private static void Fail(ProviderCollectionFeedback item, string code, string description)
    {
        item.Status = "Failed";
        item.RetrievedCount = 0;
        item.Reasons.Add(Reason(code, description));
    }

    private static void FailWithProgress(ProviderCollectionFeedback item,
        ProviderCollectionException exception)
    {
        item.Status = exception.RetrievedCount > 0 ? "Partial" : "Failed";
        item.RetrievedCount = exception.RetrievedCount;
        item.RetainedCount = 0;
        item.ExpectedCount = exception.ExpectedCount;
        item.Reasons.Add(Reason(exception.CauseCode, exception.CauseDescription,
            exception.ExpectedCount.HasValue
                ? Math.Max(0, exception.ExpectedCount.Value - exception.RetrievedCount)
                : null));
    }

    private static void FailFromException(ProviderCollectionFeedback item,
        Exception exception, string provider)
    {
        (string? classifiedCode, string? classifiedDescription) =
            ProviderCollectionException.Classify(exception);
        if (classifiedCode is not null && classifiedDescription is not null)
        {
            Fail(item, classifiedCode, classifiedDescription);
            return;
        }

        string message = exception.Message;
        string code;
        string description;
        if (message.Contains("sayfa", StringComparison.OrdinalIgnoreCase) &&
            message.Contains("sınır", StringComparison.OrdinalIgnoreCase))
        {
            code = "PageLimit";
            description = $"{provider} sayfa güvenlik sınırına ulaştı; eksik veri kaydedilmedi.";
        }
        else if (message.Contains("429", StringComparison.Ordinal) ||
            message.Contains("rate", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("quota", StringComparison.OrdinalIgnoreCase))
        {
            code = "RateLimited";
            description = $"{provider} istek kotası veya hız sınırı nedeniyle tamamlanamadı.";
        }
        else if (message.Contains("401", StringComparison.Ordinal) ||
            message.Contains("403", StringComparison.Ordinal) ||
            message.Contains("API key", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("anahtar", StringComparison.OrdinalIgnoreCase))
        {
            code = "AuthenticationOrConfiguration";
            description = $"{provider} kimlik doğrulaması veya yapılandırması kabul edilmedi.";
        }
        else if (exception is JsonException ||
            message.Contains("JSON", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("geçerli", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("invalid", StringComparison.OrdinalIgnoreCase))
        {
            code = "MalformedResponse";
            description = $"{provider} geçerli bir yanıt döndürmedi; eksik veri kaydedilmedi.";
        }
        else if (message.Contains("bulunamad", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            code = "NotFound";
            description = $"{provider} kaydı bulunamadı.";
        }
        else
        {
            code = "ProviderError";
            description = $"{provider} isteği tamamlanamadı; eksik veri kaydedilmedi.";
        }
        Fail(item, code, description);
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
            string.IsNullOrWhiteSpace(profile.ActivitiesDetailsJson) ||
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

    private static bool HasCompleteGoogleScholarRawData(
        GoogleScholarProfile? profile)
    {
        return profile is not null &&
            !string.IsNullOrWhiteSpace(profile.RawDataJson) &&
            profile.Works is not null &&
            profile.Works.All(work => !string.IsNullOrWhiteSpace(work.RawDataJson));
    }

    private static bool HasCompleteOpenAlexRawData(OpenAlexProfile? profile)
    {
        return profile is not null &&
            HasOpenAlexCandidateLookup(profile.RawDataJson) &&
            !string.IsNullOrWhiteSpace(profile.WorksPagesJson) &&
            profile.Works is not null &&
            profile.Works.Count == profile.WorksCount &&
            profile.Works.All(work => !string.IsNullOrWhiteSpace(work.RawDataJson));
    }

    private static bool HasOpenAlexCandidateLookup(string? rawDataJson)
    {
        if (string.IsNullOrWhiteSpace(rawDataJson))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(rawDataJson);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty(
                    "results",
                    out JsonElement results) &&
                results.ValueKind == JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private bool HasCompleteWebOfScienceRawData(
        WebOfScienceProfile? profile)
    {
        int index = 0;
        WebOfScienceWork? work = null;

        if (profile is null ||
            !HasConfiguredWebOfScienceDatabaseResponses(
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

    private bool HasConfiguredWebOfScienceDatabaseResponses(
        string? documentPagesJson)
    {
        JsonDocument? document = null;
        JsonElement root = default;

        if (string.IsNullOrWhiteSpace(documentPagesJson))
        {
            return false;
        }

        try
        {
            document = JsonDocument.Parse(documentPagesJson);
            root = document.RootElement;

            return root.ValueKind == JsonValueKind.Object &&
                _webOfScienceClient.GetDatabaseIds().All(database =>
                    root.TryGetProperty(database, out JsonElement pages) &&
                    pages.ValueKind == JsonValueKind.Array && pages.GetArrayLength() > 0);
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
