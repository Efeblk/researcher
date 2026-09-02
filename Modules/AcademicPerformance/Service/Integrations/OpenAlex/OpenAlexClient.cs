using System.Globalization;
using System.Text.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Models;
using Microsoft.Extensions.Configuration;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;

public sealed class OpenAlexClient
{
    private const int DefaultMaximumPages = 100;

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public OpenAlexClient(
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task FillResearcherAsync(Researcher researcher)
    {
        if (string.IsNullOrWhiteSpace(researcher.Orcid))
        {
            throw new ArgumentException("OpenAlex sorgusu için ORCID gereklidir.");
        }

        string apiBaseUrl = (_configuration["OpenAlex:ApiBaseUrl"]
            ?? "https://api.openalex.org").TrimEnd('/');
        string orcid = researcher.Orcid.Trim();

        using JsonDocument authorResponse = await GetJsonAsync(
            AppendApiKey(
                $"{apiBaseUrl}/authors" +
                $"?filter=orcid:{Uri.EscapeDataString(orcid)}" +
                "&per_page=100"));
        ThrowIfApiError(authorResponse.RootElement, "OpenAlex yazarı alınamadı");

        JsonElement selectedAuthor = SelectBestAuthor(authorResponse.RootElement);
        OpenAlexProfile profile = CreateProfile(selectedAuthor);
        profile.RawDataJson = authorResponse.RootElement.GetRawText();
        List<JsonElement> workPages = [];
        List<OpenAlexWork> works = [];
        string? cursor = "*";
        int page = 0;
        int maximumPages = GetMaximumPages();
        string authorId = GetShortOpenAlexId(profile.OpenAlexAuthorId);

        while (!string.IsNullOrWhiteSpace(cursor) && page < maximumPages)
        {
            using JsonDocument worksResponse = await GetJsonAsync(AppendApiKey(
                $"{apiBaseUrl}/works" +
                $"?filter=author.id:{Uri.EscapeDataString(authorId)}" +
                "&per_page=100" +
                $"&cursor={Uri.EscapeDataString(cursor)}"));
            JsonElement root = worksResponse.RootElement;

            ThrowIfApiError(root, "OpenAlex yayınları alınamadı");
            workPages.Add(root.Clone());
            AddWorks(root, works);
            cursor = GetString(GetObject(root, "meta"), "next_cursor");
            page++;
        }

        if (!string.IsNullOrWhiteSpace(cursor))
        {
            throw new HttpRequestException(
                $"OpenAlex yayınları {maximumPages} sayfalık güvenlik sınırını " +
                "aştı; eksik veri kaydedilmedi.");
        }

        profile.Works = works
            .GroupBy(work => work.OpenAlexWorkId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        profile.WorksPagesJson = JsonSerializer.Serialize(workPages);
        profile.LastUpdatedAt = DateTime.UtcNow;
        researcher.OpenAlexProfile = profile;
    }

    private static JsonElement SelectBestAuthor(JsonElement root)
    {
        JsonElement results = GetProperty(root, "results");

        if (results.ValueKind != JsonValueKind.Array)
        {
            throw new HttpRequestException(
                "OpenAlex ORCID araması geçerli bir yazar listesi döndürmedi.");
        }

        JsonElement? selectedAuthor = null;
        int selectedWorksCount = -1;
        int selectedCitedByCount = -1;

        foreach (JsonElement author in results.EnumerateArray())
        {
            int worksCount = GetInteger(author, "works_count") ?? 0;
            int citedByCount = GetInteger(author, "cited_by_count") ?? 0;

            if (selectedAuthor is null ||
                worksCount > selectedWorksCount ||
                worksCount == selectedWorksCount &&
                citedByCount > selectedCitedByCount)
            {
                selectedAuthor = author.Clone();
                selectedWorksCount = worksCount;
                selectedCitedByCount = citedByCount;
            }
        }

        return selectedAuthor ?? throw new HttpRequestException(
            "ORCID ile eşleşen OpenAlex yazarı bulunamadı.");
    }

    private async Task<JsonDocument> GetJsonAsync(string url)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(url);
        string content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"OpenAlex HTTP {(int)response.StatusCode}: " +
                GetApiError(content, response.ReasonPhrase));
        }

        try
        {
            return JsonDocument.Parse(content);
        }
        catch (JsonException exception)
        {
            throw new HttpRequestException(
                "OpenAlex geçerli JSON döndürmedi.",
                exception);
        }
    }

    private static OpenAlexProfile CreateProfile(JsonElement root)
    {
        string? authorId = GetString(root, "id");

        if (string.IsNullOrWhiteSpace(authorId))
        {
            throw new HttpRequestException("ORCID ile eşleşen OpenAlex yazarı bulunamadı.");
        }

        JsonElement summaryStats = GetObject(root, "summary_stats");
        JsonElement institutions = GetProperty(root, "last_known_institutions");
        string? institutionName = null;

        if (institutions.ValueKind == JsonValueKind.Array)
        {
            JsonElement firstInstitution = institutions.EnumerateArray().FirstOrDefault();
            institutionName = GetString(firstInstitution, "display_name");
        }

        return new OpenAlexProfile
        {
            OpenAlexAuthorId = authorId,
            DisplayName = GetString(root, "display_name"),
            LastKnownInstitution = institutionName,
            WorksCount = GetInteger(root, "works_count") ?? 0,
            CitedByCount = GetInteger(root, "cited_by_count") ?? 0,
            HIndex = GetInteger(summaryStats, "h_index"),
            I10Index = GetInteger(summaryStats, "i10_index"),
            TwoYearMeanCitedness = GetDecimal(summaryStats, "2yr_mean_citedness"),
            CountsByYearJson = SerializeProperty(root, "counts_by_year"),
            RawDataJson = root.GetRawText()
        };
    }

    private static void AddWorks(JsonElement root, List<OpenAlexWork> works)
    {
        JsonElement results = GetProperty(root, "results");

        if (results.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement result in results.EnumerateArray())
        {
            string? workId = GetString(result, "id");

            if (string.IsNullOrWhiteSpace(workId))
            {
                continue;
            }

            JsonElement primaryLocation = GetObject(result, "primary_location");
            JsonElement bestOpenAccessLocation = GetObject(
                result,
                "best_oa_location");
            JsonElement source = GetObject(primaryLocation, "source");

            works.Add(new OpenAlexWork
            {
                OpenAlexWorkId = workId,
                Title = GetString(result, "display_name"),
                PublicationYear = GetInteger(result, "publication_year"),
                PublicationDate = GetDate(result, "publication_date"),
                Doi = GetString(result, "doi"),
                WorkType = GetString(result, "type"),
                CitedByCount = GetInteger(result, "cited_by_count") ?? 0,
                Authors = CreateAuthors(result),
                SourceName = GetString(source, "display_name"),
                Url = GetString(primaryLocation, "landing_page_url") ?? workId,
                OpenAccessUrl = GetString(bestOpenAccessLocation, "pdf_url") ??
                    GetString(bestOpenAccessLocation, "landing_page_url"),
                RawDataJson = result.GetRawText()
            });
        }
    }

    private static string? CreateAuthors(JsonElement work)
    {
        JsonElement authorships = GetProperty(work, "authorships");

        if (authorships.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        List<string> names = [];

        foreach (JsonElement authorship in authorships.EnumerateArray())
        {
            string? name = GetString(GetObject(authorship, "author"), "display_name");

            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name);
            }
        }

        return names.Count == 0 ? null : string.Join(", ", names.Distinct());
    }

    private string AppendApiKey(string url)
    {
        string? apiKey = _configuration["OpenAlex:ApiKey"];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return url;
        }

        char separator = url.Contains('?') ? '&' : '?';
        return $"{url}{separator}api_key={Uri.EscapeDataString(apiKey.Trim())}";
    }

    private int GetMaximumPages()
    {
        return int.TryParse(
                _configuration["OpenAlex:MaximumPages"],
                out int maximumPages) &&
            maximumPages > 0
                ? maximumPages
                : DefaultMaximumPages;
    }

    private static string GetShortOpenAlexId(string id)
    {
        return id.TrimEnd('/').Split('/').Last();
    }

    private static void ThrowIfApiError(JsonElement root, string fallback)
    {
        string? error = GetString(root, "error");

        if (!string.IsNullOrWhiteSpace(error))
        {
            throw new HttpRequestException(
                $"{error}: {GetString(root, "message") ?? fallback}");
        }
    }

    private static string GetApiError(string content, string? fallback)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(content);
            return GetString(document.RootElement, "message")
                ?? GetString(document.RootElement, "error")
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

    private static int? GetInteger(JsonElement element, string propertyName)
    {
        JsonElement property = GetProperty(element, propertyName);

        if (property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt32(out int value))
        {
            return value;
        }

        return int.TryParse(
            GetString(element, propertyName),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value)
                ? value
                : null;
    }

    private static decimal? GetDecimal(JsonElement element, string propertyName)
    {
        JsonElement property = GetProperty(element, propertyName);

        if (property.ValueKind == JsonValueKind.Number &&
            property.TryGetDecimal(out decimal value))
        {
            return value;
        }

        return null;
    }

    private static DateTime? GetDate(JsonElement element, string propertyName)
    {
        return DateTime.TryParseExact(
            GetString(element, propertyName),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out DateTime value)
                ? value
                : null;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        JsonElement property = GetProperty(element, propertyName);

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
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
