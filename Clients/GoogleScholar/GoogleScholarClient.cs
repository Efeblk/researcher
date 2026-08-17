using Microsoft.Extensions.Configuration;
using System.Net.Http.Json;
using System.Text.Json;

public sealed class GoogleScholarClient
{
    private const string Endpoint = "https://serpapi.com/search?engine=google_scholar_author";

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

    public async Task<GoogleScholarData> GetAuthorAsync(string? scholarId, int workCount)
    {
        string? encodedScholarId = null;
        string? encodedApiKey = null;
        string? url = null;
        GoogleScholarAuthorResponse? response = null;
        GoogleScholarData? googleScholarData = null;
        List<GoogleScholarWork>? works = null;
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

        if (workCount < 1 || workCount > 100)
        {
            throw new ArgumentException("Google Scholar yayın sayısı 1 ile 100 arasında olmalı.");
        }

        encodedScholarId = Uri.EscapeDataString(scholarId);
        encodedApiKey = Uri.EscapeDataString(_apiKey);
        url = $"{Endpoint}" +
              $"&author_id={encodedScholarId}" +
              $"&num={workCount}" +
              $"&api_key={encodedApiKey}" +
              "&output=json";

        response = await _httpClient.GetFromJsonAsync<GoogleScholarAuthorResponse>(url, JsonOptions);
        works = response?.Articles ?? [];

        for (index = 0; index < works.Count; index++)
        {
            work = works[index];
            work.CitedByCount = work.CitedBy?.Value;
        }

        googleScholarData = new GoogleScholarData();
        googleScholarData.ScholarId = scholarId;
        googleScholarData.Name = response?.Author?.Name;
        googleScholarData.Affiliations = response?.Author?.Affiliations;
        googleScholarData.Email = response?.Author?.Email;
        googleScholarData.Interests = response?.Author?.Interests ?? [];
        googleScholarData.Works = works;

        return googleScholarData;
    }
}
