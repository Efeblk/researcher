using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;

public sealed class OpenAlexClient
{
    private const string BaseUrl = "https://api.openalex.org";
    private const int PageSize = 100;

    private static readonly Regex OrcidPattern = new(
        @"^\d{4}-\d{4}-\d{4}-\d{3}[\dX]$",
        RegexOptions.IgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public OpenAlexClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task FillResearcherAsync(Researcher researcher)
    {
        string? orcidUrl = null;
        string? url = null;
        OpenAlexData? openAlexData = null;

        if (string.IsNullOrWhiteSpace(researcher.Orcid) || !OrcidPattern.IsMatch(researcher.Orcid))
        {
            throw new ArgumentException("Numara 0000-0000-0000-000X biçiminde olmalı.");
        }
 
        orcidUrl = Uri.EscapeDataString($"https://orcid.org/{researcher.Orcid}");
        url = $"{BaseUrl}/authors/{orcidUrl}";

        openAlexData = await _httpClient.GetFromJsonAsync<OpenAlexData>(url, JsonOptions);

        if (openAlexData is null)
        {
            throw new InvalidOperationException("Akademisyen kaydı boş döndü.");
        }

        openAlexData.Works = await GetAllWorksAsync(openAlexData.AuthorId);
        researcher.OpenAlex = openAlexData;
    }

    private async Task<List<OpenAlexWork>> GetAllWorksAsync(string? authorUrl)
    {
        string? authorId = null;
        string? filter = null;
        string? select = null;
        string? cursor = null;
        string? encodedCursor = null;
        string? url = null;
        OpenAlexWorksResponse? response = null;
        List<OpenAlexWork>? works = null;
        List<OpenAlexWork>? pageWorks = null;

        if (string.IsNullOrWhiteSpace(authorUrl))
        {
            throw new ArgumentException("OpenAlex yazar adresi boş olamaz.");
        }

        authorId = authorUrl.Split('/').Last();
        filter = Uri.EscapeDataString($"author.id:{authorId}");
        select = Uri.EscapeDataString("title,publication_year,doi,type,cited_by_count");
        cursor = "*";
        works = [];

        while (!string.IsNullOrWhiteSpace(cursor))
        {
            encodedCursor = Uri.EscapeDataString(cursor);
            url = $"{BaseUrl}/works" +
                  $"?filter={filter}" +
                  $"&select={select}" +
                  "&sort=publication_date:desc" +
                  $"&per_page={PageSize}" +
                  $"&cursor={encodedCursor}";

            response = await _httpClient.GetFromJsonAsync<OpenAlexWorksResponse>(url, JsonOptions);
            pageWorks = response?.Results ?? [];

            if (pageWorks.Count == 0)
            {
                break;
            }

            works.AddRange(pageWorks);
            cursor = response?.Meta?.NextCursor;
        }

        return works;
    }
}
