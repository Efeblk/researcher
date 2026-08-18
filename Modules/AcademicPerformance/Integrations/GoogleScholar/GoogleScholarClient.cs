using Microsoft.Extensions.Configuration;
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
    private readonly bool _collectArticleDetails;

    public GoogleScholarClient(HttpClient httpClient, IConfiguration configuration)
    {
        bool collectArticleDetails = false;

        _httpClient = httpClient;
        _apiKey = configuration["SerpApi:ApiKey"];
        bool.TryParse(
            configuration["GoogleScholar:CollectArticleDetails"],
            out collectArticleDetails);
        _collectArticleDetails = collectArticleDetails;
    }

    public async Task<GoogleScholarData> GetAuthorAsync(string? scholarId)
    {
        string? encodedScholarId = null;
        string? encodedApiKey = null;
        string? url = null;
        string? responseJson = null;
        GoogleScholarAuthorResponse? response = null;
        GoogleScholarAuthor? author = null;
        GoogleScholarCitedBy? citedBy = null;
        GoogleScholarData? googleScholarData = null;
        List<GoogleScholarWork>? works = null;
        List<GoogleScholarWork>? pageWorks = null;
        List<string>? responsePages = null;
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
        responsePages = [];

        while (true)
        {
            url = $"{Endpoint}" +
                  $"&author_id={encodedScholarId}" +
                  $"&start={start}" +
                  $"&num={PageSize}" +
                  $"&api_key={encodedApiKey}" +
                  "&hl=en" +
                  "&output=json";

            responseJson = await _httpClient.GetStringAsync(url);
            responsePages.Add(responseJson);
            response = JsonSerializer.Deserialize<GoogleScholarAuthorResponse>(
                responseJson,
                JsonOptions);
            author ??= response?.Author;
            citedBy ??= response?.CitedBy;
            pageWorks = response?.Articles ?? [];
            AssignRawWorkJson(responseJson, pageWorks);

            for (index = 0; index < pageWorks.Count; index++)
            {
                work = pageWorks[index];
                work.CitedByCount = work.CitedBy?.Value;
                work.CitedByUrl = work.CitedBy?.Link;
                work.CitedBySerpApiUrl = work.CitedBy?.SerpApiLink;
                work.CitesId = work.CitedBy?.CitesId;
            }

            works.AddRange(pageWorks);

            if (pageWorks.Count < PageSize)
            {
                break;
            }

            start += PageSize;
        }

        if (_collectArticleDetails)
        {
            await FillArticleDetailsAsync(scholarId, works);
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
        googleScholarData.RawDataJson = responsePages.FirstOrDefault();
        googleScholarData.ResponsePagesJson = CreateJsonArray(responsePages);
        googleScholarData.Interests = author?.Interests ?? [];
        googleScholarData.Works = works;

        return googleScholarData;
    }

    private async Task FillArticleDetailsAsync(
        string scholarId,
        List<GoogleScholarWork> works)
    {
        string? encodedScholarId = null;
        string? encodedApiKey = null;
        string? encodedCitationId = null;
        string? url = null;
        string? responseBody = null;
        HttpResponseMessage? response = null;
        int index = 0;
        GoogleScholarWork? work = null;

        encodedScholarId = Uri.EscapeDataString(scholarId);
        encodedApiKey = Uri.EscapeDataString(_apiKey!);

        for (index = 0; index < works.Count; index++)
        {
            work = works[index];

            if (string.IsNullOrWhiteSpace(work.CitationId))
            {
                continue;
            }

            encodedCitationId = Uri.EscapeDataString(work.CitationId);
            url = $"{Endpoint}" +
                  $"&author_id={encodedScholarId}" +
                  "&view_op=view_citation" +
                  $"&citation_id={encodedCitationId}" +
                  $"&api_key={encodedApiKey}" +
                  "&hl=en" +
                  "&output=json";

            try
            {
                using (response = await _httpClient.GetAsync(url))
                {
                    responseBody = await response.Content.ReadAsStringAsync();
                    work.DetailRawDataJson = response.IsSuccessStatusCode
                        ? responseBody
                        : null;
                }
            }
            catch (HttpRequestException)
            {
                work.DetailRawDataJson = null;
            }
            catch (TaskCanceledException)
            {
                work.DetailRawDataJson = null;
            }
        }
    }

    private static void AssignRawWorkJson(
        string responseJson,
        List<GoogleScholarWork> works)
    {
        JsonDocument? document = null;
        JsonElement articlesElement = default;
        JsonElement workElement = default;
        int index = 0;
        int workCount = 0;

        using (document = JsonDocument.Parse(responseJson))
        {
            if (!document.RootElement.TryGetProperty("articles", out articlesElement) ||
                articlesElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            workCount = Math.Min(works.Count, articlesElement.GetArrayLength());

            for (index = 0; index < workCount; index++)
            {
                workElement = articlesElement[index];
                works[index].RawDataJson = workElement.GetRawText();
            }
        }
    }

    private static string CreateJsonArray(List<string> jsonValues)
    {
        return $"[{string.Join(",", jsonValues)}]";
    }
}
