using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.RateLimiting;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Scopus;

public sealed class ScopusClient(HttpClient httpClient, IConfiguration configuration)
{
    private const int DefaultMaximumPages = 100;
    private const int PageSize = 25;
    private const long DefaultMaximumResponseBytes = 4L * 1024 * 1024;

    public async Task FillResearcherAsync(
        Researcher researcher,
        string scopusId,
        CancellationToken cancellationToken = default)
    {
        string normalizedId = Researchers.Collection.ResearcherIdentifierParser.NormalizeScopusId(scopusId);
        Uri baseUri = GetApiBaseUri();
        EnsureCredentials();

        using JsonDocument authorResponse = await GetJsonAsync(
            baseUri,
            $"author/author_id/{Uri.EscapeDataString(normalizedId)}?view=ENHANCED",
            cancellationToken);
        ScopusProfile profile = CreateProfile(authorResponse.RootElement, normalizedId);
        profile.RawDataJson = SanitizeJson(authorResponse.RootElement);

        List<string> pages = [];
        Dictionary<string, ScopusWork> works = new(StringComparer.OrdinalIgnoreCase);
        string cursor = "*";
        int? total = null;
        int page = 0;
        int maximumPages = GetMaximumPages();
        while (page < maximumPages)
        {
            using JsonDocument searchResponse = await GetJsonAsync(
                baseUri,
                "search/scopus?query=" + Uri.EscapeDataString($"AU-ID({normalizedId})") +
                $"&count={PageSize}&view=COMPLETE&cursor={Uri.EscapeDataString(cursor)}",
                cancellationToken);
            JsonElement searchResults = GetObject(searchResponse.RootElement, "search-results");
            int? reportedTotal = GetInteger(searchResults, "opensearch:totalResults");
            if (searchResults.ValueKind != JsonValueKind.Object || !reportedTotal.HasValue || reportedTotal < 0)
                throw new HttpRequestException("Scopus search returned an invalid result count.");
            if (total.HasValue && total != reportedTotal)
                throw new HttpRequestException("Scopus search result count changed during pagination.");
            total = reportedTotal;

            pages.Add(SanitizeJson(searchResponse.RootElement));
            int before = works.Count;
            AddWorks(searchResults, works);
            if (works.Count > total.Value)
                throw new HttpRequestException("Scopus search returned more works than its result count.");
            page++;
            if (works.Count >= total.Value)
                break;
            if (works.Count == before)
                throw new HttpRequestException("Scopus search pagination made no progress.");
            string? nextCursor = GetString(GetObject(searchResults, "cursor"), "@next");
            if (string.IsNullOrWhiteSpace(nextCursor) || nextCursor == cursor)
                throw new HttpRequestException("Scopus search did not return a valid next cursor.");
            cursor = nextCursor;
        }

        if (!total.HasValue || works.Count < total.Value)
            throw new HttpRequestException(
                $"Scopus results exceeded the configured {maximumPages}-page safety limit; incomplete data was not saved.");

        profile.Works = works.Values.ToList();
        profile.SearchPagesJson = JsonSerializer.Serialize(
            pages.Select(pageJson => JsonNode.Parse(pageJson)).ToList());
        profile.LastUpdatedAt = DateTime.UtcNow;
        researcher.ScopusId = normalizedId;
        researcher.ScopusProfile = profile;
    }

    private async Task<JsonDocument> GetJsonAsync(
        Uri baseUri,
        string relativeUrl,
        CancellationToken cancellationToken)
    {
        Uri uri = new(baseUri, relativeUrl);
        if (!baseUri.IsBaseOf(uri))
            throw new InvalidOperationException("Scopus request URL is outside Scopus:ApiBaseUrl.");

        using HttpRequestMessage request = new(HttpMethod.Get, uri);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Add("X-ELS-APIKey", configuration["Scopus:ApiKey"]!.Trim());
        if (!string.IsNullOrWhiteSpace(configuration["Scopus:InstToken"]))
            request.Headers.Add("X-ELS-Insttoken", configuration["Scopus:InstToken"]!.Trim());
        request.Options.Set(ProviderRateLimitHandler.ResponseBufferLimit, GetMaximumResponseBytes());

        using HttpResponseMessage response = await httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Scopus request failed with HTTP {(int)response.StatusCode}.");

        await response.Content.LoadIntoBufferAsync(GetMaximumResponseBytes(), cancellationToken);
        try
        {
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (JsonException exception)
        {
            throw new HttpRequestException("Scopus returned invalid JSON.", exception);
        }
    }

    private void EnsureCredentials()
    {
        if (string.IsNullOrWhiteSpace(configuration["Scopus:ApiKey"]))
            throw new InvalidOperationException("Scopus credentials are not configured.");
    }

    private Uri GetApiBaseUri()
    {
        string value = configuration["Scopus:ApiBaseUrl"] ?? "https://api.elsevier.com/content/";
        if (!Uri.TryCreate(value.TrimEnd('/') + "/", UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("Scopus:ApiBaseUrl must use HTTPS or loopback HTTP.");
        return uri;
    }

    private int GetMaximumPages() =>
        int.TryParse(configuration["Scopus:MaximumPages"], out int value) && value > 0
            ? value : DefaultMaximumPages;

    private long GetMaximumResponseBytes() =>
        long.TryParse(configuration["ProviderResponses:MaximumResponseBytes"], out long value) &&
        value is >= 1024 and <= 16L * 1024 * 1024 ? value : DefaultMaximumResponseBytes;

    private static ScopusProfile CreateProfile(JsonElement root, string expectedId)
    {
        JsonElement responses = GetProperty(root, "author-retrieval-response");
        JsonElement author = responses.ValueKind == JsonValueKind.Array && responses.GetArrayLength() > 0
            ? responses[0] : responses.ValueKind == JsonValueKind.Object ? responses : default;
        JsonElement core = GetObject(author, "coredata");
        string identifier = (GetString(core, "dc:identifier") ?? string.Empty)
            .Replace("AUTHOR_ID:", string.Empty, StringComparison.OrdinalIgnoreCase);
        if (!identifier.Equals(expectedId, StringComparison.Ordinal))
            throw new HttpRequestException("Scopus returned an unexpected author record.");

        JsonElement authorProfile = GetObject(author, "author-profile");
        JsonElement preferredName = GetObject(authorProfile, "preferred-name");
        if (preferredName.ValueKind != JsonValueKind.Object)
            preferredName = GetObject(author, "preferred-name");
        JsonElement affiliation = GetCurrentAffiliation(authorProfile);
        if (affiliation.ValueKind != JsonValueKind.Object)
            affiliation = GetCurrentAffiliation(author);
        return new()
        {
            ScopusAuthorId = identifier,
            DisplayName = GetString(preferredName, "indexed-name") ??
                string.Join(' ', new[] { GetString(preferredName, "given-name"), GetString(preferredName, "surname") }
                    .Where(value => !string.IsNullOrWhiteSpace(value))),
            CurrentAffiliation = GetString(affiliation, "affiliation-name"),
            DocumentsCount = GetInteger(core, "document-count"),
            CitationCount = GetInteger(core, "citation-count"),
            CitedByCount = GetInteger(core, "cited-by-count"),
            HIndex = GetInteger(author, "h-index") ?? GetInteger(core, "h-index")
        };
    }

    private static void AddWorks(JsonElement searchResults, Dictionary<string, ScopusWork> works)
    {
        JsonElement entries = GetProperty(searchResults, "entry");
        if (entries.ValueKind != JsonValueKind.Array)
            return;
        foreach (JsonElement entry in entries.EnumerateArray())
        {
            string? identifier = GetString(entry, "dc:identifier");
            string? eid = GetString(entry, "eid");
            string workId = !string.IsNullOrWhiteSpace(identifier)
                ? identifier.Replace("SCOPUS_ID:", string.Empty, StringComparison.OrdinalIgnoreCase)
                : eid ?? string.Empty;
            if (string.IsNullOrWhiteSpace(workId))
                continue;
            if (works.ContainsKey(workId))
                continue;
            works.Add(workId, new()
            {
                ScopusWorkId = workId,
                Eid = eid,
                Title = GetString(entry, "dc:title"),
                PublicationYear = GetYear(entry, "prism:coverDate"),
                PublicationDate = GetDate(entry, "prism:coverDate"),
                Doi = GetString(entry, "prism:doi"),
                WorkType = GetString(entry, "subtypeDescription") ?? GetString(entry, "subtype"),
                CitedByCount = GetInteger(entry, "citedby-count"),
                Authors = GetAuthors(entry),
                SourceName = GetString(entry, "prism:publicationName"),
                Url = GetLink(entry),
                IsOpenAccess = GetString(entry, "openaccess") == "1",
                RawDataJson = SanitizeJson(entry)
            });
        }
    }

    private static JsonElement GetCurrentAffiliation(JsonElement element)
    {
        JsonElement current = GetProperty(element, "affiliation-current");
        if (current.ValueKind == JsonValueKind.Object)
        {
            JsonElement nested = GetObject(current, "affiliation");
            return nested.ValueKind == JsonValueKind.Object ? nested : current;
        }
        if (current.ValueKind == JsonValueKind.Array && current.GetArrayLength() > 0)
        {
            JsonElement first = current[0];
            JsonElement nested = GetObject(first, "affiliation");
            return nested.ValueKind == JsonValueKind.Object ? nested : first;
        }
        return default;
    }

    private static string SanitizeJson(JsonElement element)
    {
        JsonNode? node = JsonNode.Parse(element.GetRawText());
        SanitizeNode(node);
        return node?.ToJsonString() ?? "{}";
    }

    private static void SanitizeNode(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach ((string key, JsonNode? value) in obj.ToList())
            {
                if (key.Equals("apiKey", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("insttoken", StringComparison.OrdinalIgnoreCase))
                    obj.Remove(key);
                else if (value is JsonValue scalar && scalar.TryGetValue(out string? text) &&
                    Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) && !string.IsNullOrEmpty(uri.Query))
                {
                    List<string> retained = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                        .Where(part =>
                        {
                            string name = Uri.UnescapeDataString(part.Split('=', 2)[0]);
                            return !name.Equals("apiKey", StringComparison.OrdinalIgnoreCase) &&
                                !name.Equals("insttoken", StringComparison.OrdinalIgnoreCase);
                        }).ToList();
                    obj[key] = new UriBuilder(uri) { Query = string.Join('&', retained) }.Uri.AbsoluteUri;
                }
                else
                    SanitizeNode(value);
            }
        }
        else if (node is JsonArray array)
            foreach (JsonNode? item in array)
                SanitizeNode(item);
    }

    private static string? GetAuthors(JsonElement entry)
    {
        JsonElement authors = GetProperty(entry, "author");
        if (authors.ValueKind != JsonValueKind.Array)
            return GetString(entry, "dc:creator");
        List<string> names = authors.EnumerateArray()
            .Select(author => GetString(author, "authname") ??
                string.Join(' ', new[] { GetString(author, "given-name"), GetString(author, "surname") }
                    .Where(value => !string.IsNullOrWhiteSpace(value))))
            .Where(name => !string.IsNullOrWhiteSpace(name)).Distinct().ToList();
        return names.Count == 0 ? null : string.Join(", ", names);
    }

    private static string? GetLink(JsonElement entry)
    {
        JsonElement links = GetProperty(entry, "link");
        if (links.ValueKind != JsonValueKind.Array)
            return null;
        foreach (JsonElement link in links.EnumerateArray())
            if (GetString(link, "@ref") == "scopus")
                return SanitizeUri(GetString(link, "@href"));
        return links.EnumerateArray().Select(link => SanitizeUri(GetString(link, "@href")))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static string? SanitizeUri(string? text)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) || string.IsNullOrEmpty(uri.Query))
            return text;
        List<string> retained = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(part =>
            {
                string name = Uri.UnescapeDataString(part.Split('=', 2)[0]);
                return !name.Equals("apiKey", StringComparison.OrdinalIgnoreCase) &&
                    !name.Equals("insttoken", StringComparison.OrdinalIgnoreCase);
            }).ToList();
        return new UriBuilder(uri) { Query = string.Join('&', retained) }.Uri.AbsoluteUri;
    }

    private static DateTime? GetDate(JsonElement element, string property) =>
        DateTime.TryParseExact(GetString(element, property), "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out DateTime value) ? value : null;

    private static int? GetYear(JsonElement element, string property)
    {
        string? value = GetString(element, property);
        return value?.Length >= 4 && int.TryParse(value[..4], out int year) ? year : null;
    }

    private static int? GetInteger(JsonElement element, string property)
    {
        JsonElement value = GetProperty(element, property);
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
            return number;
        return int.TryParse(GetString(element, property), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out number) ? number : null;
    }

    private static string? GetString(JsonElement element, string property)
    {
        JsonElement value = GetProperty(element, property);
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static JsonElement GetObject(JsonElement element, string property)
    {
        JsonElement value = GetProperty(element, property);
        return value.ValueKind == JsonValueKind.Object ? value : default;
    }

    private static JsonElement GetProperty(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out JsonElement value)
            ? value : default;
}
