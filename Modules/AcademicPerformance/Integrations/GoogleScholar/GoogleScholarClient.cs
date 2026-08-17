using Microsoft.Extensions.Configuration;
using System.Net.Http.Json;
using System.Text.Json;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.GoogleScholar;

public sealed class GoogleScholarClient
{
    private const string Endpoint = "https://serpapi.com/search?engine=google_scholar_author";
    private const int PageSize = 100;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;

    public GoogleScholarClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _apiKey = configuration["SerpApi:ApiKey"];
    }

    public async Task<GoogleScholarData> GetAuthorAsync(string? scholarId)
    {
        string? encodedScholarId = null;
        string? encodedApiKey = null;
        string? url = null;
        GoogleScholarAuthorResponse? response = null;
        GoogleScholarAuthor? author = null;
        GoogleScholarCitedBy? citedBy = null;
        GoogleScholarData? googleScholarData = null;
        List<GoogleScholarWork>? works = null;
        List<GoogleScholarWork>? pageWorks = null;
        int start = 0;
        int index = 0;
        GoogleScholarWork? work = null;

        if (string.IsNullOrWhiteSpace(scholarId))
        {
            throw new ArgumentException("Google Scholar ID boş olamaz.");
        }

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new ArgumentException("SerpAPI anahtarı boş olamaz.");
        }

        encodedScholarId = Uri.EscapeDataString(scholarId);
        encodedApiKey = Uri.EscapeDataString(_apiKey);
        works = [];

        while (true)
        {
            url = $"{Endpoint}" +
                  $"&author_id={encodedScholarId}" +
                  $"&start={start}" +
                  $"&num={PageSize}" +
                  $"&api_key={encodedApiKey}" +
                  "&hl=en" +
                  "&output=json";

            response = await _httpClient.GetFromJsonAsync<GoogleScholarAuthorResponse>(
                url,
                JsonOptions);
            author ??= response?.Author;
            citedBy ??= response?.CitedBy;
            pageWorks = response?.Articles ?? [];

            for (index = 0; index < pageWorks.Count; index++)
            {
                work = pageWorks[index];
                work.CitedByCount = work.CitedBy?.Value;
            }

            works.AddRange(pageWorks);

            if (pageWorks.Count < PageSize)
            {
                break;
            }

            start += PageSize;
        }

        googleScholarData = new GoogleScholarData();
        googleScholarData.ScholarId = scholarId;
        googleScholarData.Name = author?.Name;
        googleScholarData.Affiliations = author?.Affiliations;
        googleScholarData.Email = author?.Email;
        googleScholarData.CitationCount = citedBy?.Table?
            .FirstOrDefault(row => row.Citations is not null)?
            .Citations?
            .All;
        googleScholarData.HIndex = citedBy?.Table?
            .FirstOrDefault(row => row.HIndex is not null)?
            .HIndex?
            .All;
        googleScholarData.I10Index = citedBy?.Table?
            .FirstOrDefault(row => row.I10Index is not null)?
            .I10Index?
            .All;
        googleScholarData.Interests = author?.Interests ?? [];
        googleScholarData.Works = works;

        return googleScholarData;
    }
}
