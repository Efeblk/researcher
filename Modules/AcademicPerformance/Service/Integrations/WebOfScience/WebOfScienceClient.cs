using System.Net;
using System.Text.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using Microsoft.Extensions.Configuration;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;

public sealed class WebOfScienceClient
{
    private const string DefaultApiBaseUrl =
        "https://api.clarivate.com/apis/wos-starter/v1";
    private const int DocumentsPageSize = 50;
    private const int DefaultMaximumPages = 100;

    private static readonly string[] DefaultDatabaseIds = ["WOS", "WOK"];

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public WebOfScienceClient(
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task FillResearcherAsync(
        Researcher researcher,
        string researcherIdentifier)
    {
        Dictionary<string, List<string>>? documentPagesByDatabase = new Dictionary<string, List<string>>(
            StringComparer.OrdinalIgnoreCase);
        List<string>? databaseIds = GetDatabaseIds();

        foreach (string databaseId in databaseIds)
        {
            documentPagesByDatabase[databaseId] = await GetDocumentPagesAsync(
                researcherIdentifier,
                databaseId);
        }

        WebOfScienceProfile? profile = CreateProfile(researcherIdentifier, documentPagesByDatabase);

        researcher.WebOfScienceResearcherId = researcherIdentifier;

        if (researcher.WebOfScienceProfile is null)
        {
            researcher.WebOfScienceProfile = profile;
            return;
        }

        ApplyProfile(profile, researcher.WebOfScienceProfile);
    }

    private async Task<List<string>> GetDocumentPagesAsync(
        string researcherIdentifier,
        string databaseId)
    {
        string? responseJson = null;

        int page = 1;
        int total = 0;
        int limit = DocumentsPageSize;
        int maximumPages = int.TryParse(_configuration["WebOfScience:MaximumPages"], out int configured)
            && configured > 0 ? configured : DefaultMaximumPages;

        List<string>? pages = [];
        string? query = Uri.EscapeDataString($"AI=({researcherIdentifier})");

        do
        {
            responseJson = await GetJsonAsync(
                $"documents?q={query}&db={Uri.EscapeDataString(databaseId)}" +
                $"&page={page}&limit={DocumentsPageSize}&sortField=PY%2BD");
            pages.Add(responseJson);
            (total, limit) = ReadPagination(responseJson, DocumentsPageSize);
            if ((long)page * limit < total && page >= maximumPages)
                throw new HttpRequestException("Web of Science sayfa sınırı aşıldı; eksik veri kaydedilmedi.");
            page++;
        }
        while ((long)(page - 1) * limit < total);

        return pages;
    }

    internal List<string> GetDatabaseIds()
    {
        IConfigurationSection? databaseIdsSection = _configuration.GetSection(
            "WebOfScience:DatabaseIds");
        List<string>? databaseIds = databaseIdsSection
            .GetChildren()
            .Select(item => item.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (databaseIds.Count > 0)
        {
            return databaseIds;
        }

        string? legacyDatabaseId = _configuration["WebOfScience:DatabaseId"];

        if (!string.IsNullOrWhiteSpace(legacyDatabaseId))
        {
            return [legacyDatabaseId.Trim().ToUpperInvariant()];
        }

        return [.. DefaultDatabaseIds];
    }

    private async Task<string> GetJsonAsync(string relativePath)
    {
        HttpRequestMessage? request = null;
        HttpResponseMessage? response = null;
        string? body = null;

        string? baseUrl = _configuration["WebOfScience:ApiBaseUrl"]
            ?? DefaultApiBaseUrl;
        string? apiKey = _configuration["WebOfScience:ApiKey"];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "WebOfScience:ApiKey User Secret değeri bulunamadı.");
        }

        try
        {
            request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{baseUrl.TrimEnd('/')}/{relativePath}");
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.Add("X-ApiKey", apiKey.Trim());

            response = await _httpClient.SendAsync(request);
            body = await response.Content.ReadAsStringAsync();

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new ArgumentException(
                    "Web of Science ResearcherID bulunamadı.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Web of Science API {(int)response.StatusCode} " +
                    $"({response.ReasonPhrase}) döndürdü: " +
                    $"{GetErrorMessage(body)}");
            }

            return body;
        }
        finally
        {
            response?.Dispose();
            request?.Dispose();
        }
    }

    private static WebOfScienceProfile CreateProfile(
        string researcherIdentifier,
        Dictionary<string, List<string>> documentPagesByDatabase)
    {
        List<string>? allDocumentPages = documentPagesByDatabase
            .Values
            .SelectMany(pages => pages)
            .ToList();
        List<WebOfScienceWork>? works = CreateWorks(documentPagesByDatabase);

        if (works.Count == 0)
        {
            throw new ArgumentException(
                "Bu ResearcherID için Web of Science yayını bulunamadı.");
        }

        WebOfScienceProfile? profile = new WebOfScienceProfile();
        profile.DisplayName = FindResearcherDisplayName(
            allDocumentPages,
            researcherIdentifier);
        profile.HIndex = CalculateHIndex(works);
        profile.DocumentsCount = works.Count;
        profile.TotalTimesCited = CalculateTotalTimesCited(works);
        profile.LastUpdatedAt = DateTime.UtcNow;
        profile.DocumentPagesJson = CreateRawDatabasePages(
            documentPagesByDatabase);
        profile.Works = works;
        profile.PeerReviews = [];
        return profile;
    }

    private static string? FindResearcherDisplayName(
        List<string> documentPages,
        string researcherIdentifier)
    {
        int pageIndex = 0;

        for (pageIndex = 0; pageIndex < documentPages.Count; pageIndex++)
        {
            using JsonDocument? document = JsonDocument.Parse(
                documentPages[pageIndex]);
            JsonElement hits = GetProperty(document.RootElement, "hits");

            if (hits.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement hit in hits.EnumerateArray())
            {
                JsonElement authors = GetProperty(hit, "names", "authors");

                if (authors.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (JsonElement author in authors.EnumerateArray())
                {
                    string? authorResearcherId = GetString(
                        author,
                        "researcherId");

                    if (!string.Equals(
                            authorResearcherId,
                            researcherIdentifier,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    return GetString(author, "displayName") ??
                        GetString(author, "wosStandard");
                }
            }
        }

        return null;
    }

    private static int? CalculateHIndex(List<WebOfScienceWork> works)
    {
        int index = 0;

        if (works.Count == 0 || works.Any(work => !work.TimesCited.HasValue))
        {
            return null;
        }

        List<int>? citationCounts = works
            .Select(work => work.TimesCited!.Value)
            .OrderByDescending(count => count)
            .ToList();

        for (index = 0; index < citationCounts.Count; index++)
        {
            if (citationCounts[index] < index + 1)
            {
                return index;
            }
        }

        return citationCounts.Count;
    }

    private static int? CalculateTotalTimesCited(List<WebOfScienceWork> works)
    {
        if (works.Count == 0 || works.Any(work => !work.TimesCited.HasValue))
        {
            return null;
        }

        return works.Sum(work => work.TimesCited!.Value);
    }

    private static List<WebOfScienceWork> CreateWorks(
        Dictionary<string, List<string>> documentPagesByDatabase)
    {
        List<WebOfScienceWork>? works = [];
        Dictionary<string, WebOfScienceWork>? worksByIdentifier = new Dictionary<string, WebOfScienceWork>(
            StringComparer.OrdinalIgnoreCase);

        foreach (KeyValuePair<string, List<string>> databasePages in
                 documentPagesByDatabase)
        {
            AddWorks(
                databasePages.Key,
                databasePages.Value,
                works,
                worksByIdentifier);
        }

        return works;
    }

    private static void AddWorks(
        string databaseId,
        List<string> pages,
        List<WebOfScienceWork> works,
        Dictionary<string, WebOfScienceWork> worksByIdentifier)
    {
        int pageIndex = 0;

        for (pageIndex = 0; pageIndex < pages.Count; pageIndex++)
        {
            using JsonDocument? document = JsonDocument.Parse(pages[pageIndex]);
            JsonElement hits = GetProperty(document.RootElement, "hits");

            if (hits.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement hit in hits.EnumerateArray())
            {
                WebOfScienceWork? work = CreateWork(hit);
                string? key = work.Uid ?? work.Doi ??
                    $"{work.Title}|{work.PublicationYear}";
                WebOfScienceWork? existingWork = null;

                if (worksByIdentifier.TryGetValue(key, out existingWork))
                {
                    MergeWork(existingWork, work, databaseId);
                    continue;
                }

                worksByIdentifier[key] = work;
                works.Add(work);
            }
        }
    }

    private static void MergeWork(
        WebOfScienceWork target,
        WebOfScienceWork source,
        string sourceDatabaseId)
    {
        target.Title ??= source.Title;
        target.WorkTypes ??= source.WorkTypes;
        target.PublicationYear ??= source.PublicationYear;
        target.PublicationDate ??= source.PublicationDate;
        target.SourceTitle ??= source.SourceTitle;
        target.Volume ??= source.Volume;
        target.Issue ??= source.Issue;
        target.Collection ??= source.Collection;
        target.Doi ??= source.Doi;

        if (string.Equals(
                sourceDatabaseId,
                "WOK",
                StringComparison.OrdinalIgnoreCase) &&
            source.TimesCited.HasValue)
        {
            target.TimesCited = source.TimesCited;
            target.CitationsJson = source.CitationsJson;
        }
        else if (!target.TimesCited.HasValue)
        {
            target.TimesCited = source.TimesCited;
            target.CitationsJson = source.CitationsJson;
        }
    }

    private static WebOfScienceWork CreateWork(JsonElement hit)
    {
        JsonElement source = GetProperty(hit, "source");
        JsonElement citations = GetProperty(hit, "citations");

        WebOfScienceWork? work = new WebOfScienceWork();
        work.Uid = GetString(hit, "uid");
        work.Title = GetString(hit, "title");
        work.WorkTypes = JoinStrings(hit, "types");
        work.PublicationYear = GetInt32(source, "publishYear");
        work.PublicationDate = GetDateTime(source, "sortDate");
        work.SourceTitle = GetString(source, "sourceTitle");
        work.Volume = GetString(source, "volume");
        work.Issue = GetString(source, "issue");
        work.Collection = GetString(hit, "collection");
        work.Doi = GetString(hit, "identifiers", "doi");
        work.TimesCited = GetCitationCount(citations);
        work.CitationsJson = citations.ValueKind == JsonValueKind.Undefined
            ? null
            : citations.GetRawText();
        work.RawDataJson = hit.GetRawText();
        return work;
    }

    private static void ApplyProfile(
        WebOfScienceProfile source,
        WebOfScienceProfile target)
    {
        target.DisplayName = source.DisplayName;
        target.FirstName = source.FirstName;
        target.LastName = source.LastName;
        target.Orcid = source.Orcid;
        target.IsClaimed = source.IsClaimed;
        target.PrimaryOrganization = source.PrimaryOrganization;
        target.PrimaryAddress = source.PrimaryAddress;
        target.PrimaryCountry = source.PrimaryCountry;
        target.Departments = source.Departments;
        target.HIndex = source.HIndex;
        target.DocumentsCount = source.DocumentsCount;
        target.TotalCitingPublications = source.TotalCitingPublications;
        target.TotalCitingWithoutSelf = source.TotalCitingWithoutSelf;
        target.TotalTimesCited = source.TotalTimesCited;
        target.TotalTimesCitedWithoutSelf = source.TotalTimesCitedWithoutSelf;
        target.PeerReviewsCount = source.PeerReviewsCount;
        target.LastUpdatedAt = source.LastUpdatedAt;
        target.AlternativeNamesJson = source.AlternativeNamesJson;
        target.AffiliationsJson = source.AffiliationsJson;
        target.AuthorPositionsJson = source.AuthorPositionsJson;
        target.SubjectCategoriesJson = source.SubjectCategoriesJson;
        target.AwardsJson = source.AwardsJson;
        target.RawDataJson = source.RawDataJson;
        target.DocumentPagesJson = source.DocumentPagesJson;
        target.PeerReviewPagesJson = source.PeerReviewPagesJson;
        target.Works = source.Works;
        target.PeerReviews = source.PeerReviews;
    }

    private static (int Total, int Limit) ReadPagination(
        string responseJson,
        int defaultLimit)
    {
        using JsonDocument? document = JsonDocument.Parse(responseJson);
        JsonElement metadata = GetProperty(document.RootElement, "metadata");
        int total = GetInt32(metadata, "total") ?? 0;
        int limit = GetInt32(metadata, "limit") ?? defaultLimit;

        return (total, Math.Max(1, limit));
    }

    private static int? GetCitationCount(JsonElement citations)
    {
        int? firstCount = null;
        int? coreCollectionCount = null;

        if (citations.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement citation in citations.EnumerateArray())
        {
            int? count = GetInt32(citation, "count");
            string? database = GetString(citation, "db");

            firstCount ??= count;

            if (string.Equals(database, "WOK", StringComparison.OrdinalIgnoreCase))
            {
                return count;
            }

            if (string.Equals(database, "WOS", StringComparison.OrdinalIgnoreCase))
            {
                coreCollectionCount ??= count;
            }
        }

        return coreCollectionCount ?? firstCount;
    }

    private static string? GetErrorMessage(string body)
    {
        string? parsedMessage = null;

        try
        {
            using JsonDocument? document = JsonDocument.Parse(body);
            JsonElement error = GetProperty(document.RootElement, "error");

            parsedMessage = GetString(error, "details") ??
                GetString(error, "title") ??
                GetString(document.RootElement, "message") ??
                GetString(document.RootElement, "error_description");

            if (!string.IsNullOrWhiteSpace(parsedMessage))
            {
                return parsedMessage;
            }

            return document.RootElement.GetRawText();
        }
        catch (JsonException)
        {
            return string.IsNullOrWhiteSpace(body)
                ? "Ayrıntı verilmedi."
                : body.Trim();
        }
    }

    private static string CreateRawPageArray(List<string> pages)
    {
        return $"[{string.Join(",", pages)}]";
    }

    private static string CreateRawDatabasePages(
        Dictionary<string, List<string>> documentPagesByDatabase)
    {
        List<string>? databaseProperties = documentPagesByDatabase
            .Select(databasePages =>
                $"{JsonSerializer.Serialize(databasePages.Key)}:" +
                CreateRawPageArray(databasePages.Value))
            .ToList();

        return $"{{{string.Join(",", databaseProperties)}}}";
    }

    private static string? JoinStrings(JsonElement element, params string[] path)
    {
        JsonElement values = GetProperty(element, path);

        if (values.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return string.Join(", ", values
            .EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string? GetString(JsonElement element, params string[] path)
    {
        JsonElement value = GetProperty(element, path);

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int? GetInt32(JsonElement element, params string[] path)
    {
        JsonElement value = GetProperty(element, path);
        int parsedValue = 0;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out parsedValue))
        {
            return parsedValue;
        }

        if (value.ValueKind == JsonValueKind.String &&
            int.TryParse(value.GetString(), out parsedValue))
        {
            return parsedValue;
        }

        return null;
    }

    private static DateTime? GetDateTime(JsonElement element, params string[] path)
    {
        string? value = GetString(element, path);
        DateTime parsedValue = DateTime.MinValue;

        return DateTime.TryParse(value, out parsedValue)
            ? parsedValue
            : null;
    }

    private static JsonElement GetProperty(JsonElement element, params string[] path)
    {
        JsonElement current = element;
        int index = 0;

        for (index = 0; index < path.Length; index++)
        {
            if (current.ValueKind != JsonValueKind.Object ||
                !current.TryGetProperty(path[index], out current))
            {
                return default;
            }
        }

        return current;
    }
}
