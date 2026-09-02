using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using Microsoft.Extensions.Configuration;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.GoogleScholar;

public sealed class GoogleScholarClient
{
    private const int DefaultMaximumPages = 100;

    private static readonly Regex YearPattern = new(@"\b(19|20)\d{2}\b");

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public GoogleScholarClient(
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task FillResearcherAsync(
        Researcher researcher,
        string googleScholarId)
    {
        string apiKey = GetRequiredApiKey();
        string apiBaseUrl = _configuration["SearchApi:ApiBaseUrl"]
            ?? "https://www.searchapi.io/api/v1/search";
        int maximumPages = GetMaximumPages();
        List<JsonElement> pages = [];
        List<GoogleScholarWork> works = [];
        GoogleScholarProfile? profile = null;
        int page = 1;
        bool hasNextPage = false;

        do
        {
            using JsonDocument response = await SendAsync(
                apiBaseUrl,
                apiKey,
                googleScholarId,
                page);
            JsonElement root = response.RootElement;

            ThrowIfApiError(root);
            pages.Add(root.Clone());

            if (profile is null)
            {
                profile = CreateProfile(root, googleScholarId);
            }

            AddWorks(root, works);
            hasNextPage = HasNextPage(root);
            page++;
        }
        while (hasNextPage && page <= maximumPages);

        if (hasNextPage)
        {
            throw new HttpRequestException(
                $"Google Scholar yayını {maximumPages} sayfalık güvenlik " +
                "sınırını aştı; eksik veri kaydedilmedi.");
        }

        if (profile is null)
        {
            throw new HttpRequestException(
                "SearchApi boş bir Google Scholar yanıtı döndürdü.");
        }
        profile.Works = works
            .GroupBy(work => work.CitationId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        profile.DocumentsCount = profile.Works.Count;
        profile.RawDataJson = JsonSerializer.Serialize(pages);
        profile.LastUpdatedAt = DateTime.UtcNow;

        researcher.GoogleScholarId = googleScholarId;
        researcher.GoogleScholarProfile = profile;
        ApplyNameWhenMissing(researcher, profile.DisplayName);
    }

    private async Task<JsonDocument> SendAsync(
        string apiBaseUrl,
        string apiKey,
        string googleScholarId,
        int page)
    {
        string separator = apiBaseUrl.Contains('?')
            ? "&"
            : "?";
        string url = apiBaseUrl + separator +
            "engine=google_scholar_author" +
            $"&author_id={Uri.EscapeDataString(googleScholarId)}" +
            "&hl=en" +
            $"&page={page.ToString(CultureInfo.InvariantCulture)}";
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using HttpResponseMessage response = await _httpClient.SendAsync(request);
        string content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"SearchApi HTTP {(int)response.StatusCode}: " +
                GetApiError(content, response.ReasonPhrase));
        }

        try
        {
            return JsonDocument.Parse(content);
        }
        catch (JsonException exception)
        {
            throw new HttpRequestException(
                "SearchApi geçerli JSON döndürmedi.",
                exception);
        }
    }

    private static GoogleScholarProfile CreateProfile(
        JsonElement root,
        string googleScholarId)
    {
        JsonElement author = GetObject(root, "author");
        JsonElement citedBy = GetObject(root, "cited_by");

        if (author.ValueKind != JsonValueKind.Object)
        {
            throw new HttpRequestException(
                "Bu ID için herkese açık Google Scholar profili bulunamadı.");
        }
        GoogleScholarProfile profile = new()
        {
            DisplayName = GetString(author, "name"),
            Affiliations = GetString(author, "affiliations"),
            University = GetString(author, "university"),
            VerifiedEmail = GetString(author, "email"),
            ProfileUrl = $"https://scholar.google.com/citations?user={googleScholarId}",
            InterestsJson = SerializeProperty(author, "interests"),
            CitationHistogramJson = SerializeProperty(citedBy, "histogram")
        };

        ApplyMetrics(citedBy, profile);
        return profile;
    }

    private static void ApplyMetrics(
        JsonElement citedBy,
        GoogleScholarProfile profile)
    {
        JsonElement table = GetObject(citedBy, "table");
        JsonElement headers = GetProperty(table, "headers");
        JsonElement rows = GetProperty(table, "rows");

        if (headers.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement header in headers.EnumerateArray())
            {
                Match match = YearPattern.Match(GetElementText(header) ?? string.Empty);

                if (match.Success && int.TryParse(match.Value, out int sinceYear))
                {
                    profile.MetricsSinceYear = sinceYear;
                }
            }
        }

        if (rows.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            List<string?> values = row
                .EnumerateArray()
                .Select(GetElementText)
                .ToList();

            if (values.Count < 2)
            {
                continue;
            }

            string label = NormalizeMetricLabel(values[0]);
            int? all = ParseInteger(values.ElementAtOrDefault(1));
            int? recent = ParseInteger(values.ElementAtOrDefault(2));

            if (label.Contains("citation", StringComparison.Ordinal))
            {
                profile.CitationCount = all;
                profile.CitationCountRecent = recent;
            }
            else if (label.Contains("i10", StringComparison.Ordinal))
            {
                profile.I10Index = all;
                profile.I10IndexRecent = recent;
            }
            else if (label.Contains("hindex", StringComparison.Ordinal))
            {
                profile.HIndex = all;
                profile.HIndexRecent = recent;
            }
        }
    }

    private static void AddWorks(
        JsonElement root,
        List<GoogleScholarWork> works)
    {
        JsonElement articles = GetProperty(root, "articles");

        if (articles.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement article in articles.EnumerateArray())
        {
            string? citationId = GetString(article, "citation_id");
            string? title = GetString(article, "title");
            string? url = GetString(article, "link");
            int? year = GetInteger(article, "year");
            JsonElement citedBy = GetObject(article, "cited_by");

            works.Add(new GoogleScholarWork
            {
                CitationId = CreateWorkId(citationId, url, title, year),
                Title = title,
                Authors = GetString(article, "authors"),
                Publication = GetString(article, "publication"),
                PublicationYear = year,
                CitedByCount = GetInteger(citedBy, "total"),
                Url = url,
                RawDataJson = article.GetRawText()
            });
        }
    }

    private static bool HasNextPage(JsonElement root)
    {
        JsonElement pagination = GetObject(root, "pagination");
        return !string.IsNullOrWhiteSpace(GetString(pagination, "next"));
    }

    private static void ThrowIfApiError(JsonElement root)
    {
        string? error = GetString(root, "error");

        if (!string.IsNullOrWhiteSpace(error))
        {
            throw new HttpRequestException($"SearchApi: {error}");
        }
    }

    private string GetRequiredApiKey()
    {
        string? apiKey = _configuration["SearchApi:ApiKey"];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "SearchApi:ApiKey User Secret değeri bulunamadı.");
        }

        return apiKey.Trim();
    }

    private int GetMaximumPages()
    {
        return int.TryParse(
                _configuration["SearchApi:MaximumPages"],
                out int maximumPages) &&
            maximumPages > 0
                ? maximumPages
                : DefaultMaximumPages;
    }

    private static string GetApiError(string content, string? fallback)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(content);
            return GetString(document.RootElement, "error")
                ?? GetString(document.RootElement, "message")
                ?? fallback
                ?? "Bilinmeyen hata";
        }
        catch (JsonException)
        {
            return string.IsNullOrWhiteSpace(content)
                ? fallback ?? "Bilinmeyen hata"
                : content[..Math.Min(content.Length, 500)];
        }
    }

    private static void ApplyNameWhenMissing(
        Researcher researcher,
        string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName) ||
            !string.IsNullOrWhiteSpace(researcher.FirstName) ||
            !string.IsNullOrWhiteSpace(researcher.LastName))
        {
            return;
        }

        string[] parts = displayName.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        researcher.FirstName = parts.FirstOrDefault();
        researcher.LastName = parts.Length > 1
            ? string.Join(' ', parts.Skip(1))
            : null;
    }

    private static string CreateWorkId(
        string? citationId,
        string? url,
        string? title,
        int? year)
    {
        if (!string.IsNullOrWhiteSpace(citationId))
        {
            return citationId.Trim();
        }

        string source = !string.IsNullOrWhiteSpace(url)
            ? url.Trim()
            : $"{title?.Trim()}|{year}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return "generated:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string NormalizeMetricLabel(string? value)
    {
        StringBuilder result = new();

        foreach (char character in value?.ToLowerInvariant() ?? string.Empty)
        {
            if (char.IsLetterOrDigit(character))
            {
                result.Append(character);
            }
        }

        return result.ToString();
    }

    private static int? ParseInteger(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string digits = new(value.Where(char.IsDigit).ToArray());
        return int.TryParse(
            digits,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out int parsed)
                ? parsed
                : null;
    }

    private static int? GetInteger(JsonElement element, string propertyName)
    {
        JsonElement property = GetProperty(element, propertyName);

        if (property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt32(out int number))
        {
            return number;
        }

        return ParseInteger(GetElementText(property));
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return GetElementText(GetProperty(element, propertyName));
    }

    private static string? GetElementText(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            _ => null
        };
    }

    private static JsonElement GetObject(JsonElement element, string propertyName)
    {
        JsonElement property = GetProperty(element, propertyName);
        return property.ValueKind == JsonValueKind.Object ? property : default;
    }

    private static JsonElement GetProperty(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out JsonElement property)
                ? property
                : default;
    }

    private static string? SerializeProperty(
        JsonElement element,
        string propertyName)
    {
        JsonElement property = GetProperty(element, propertyName);
        return property.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? null
            : property.GetRawText();
    }
}
