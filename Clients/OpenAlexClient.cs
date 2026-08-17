using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

public sealed class OpenAlexClient
{
    private const string BaseUrl = "https://api.openalex.org";

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

    public async Task<OpenAlexAuthor> GetAuthorAsync(string? orcid)
    {
        string? orcidUrl = null;
        string? url = null;
        OpenAlexAuthor? author = null;

        if (string.IsNullOrWhiteSpace(orcid) || !OrcidPattern.IsMatch(orcid))
        {
            throw new ArgumentException("Numara 0000-0000-0000-000X biçiminde olmalı.");
        }
 
        orcidUrl = Uri.EscapeDataString($"https://orcid.org/{orcid}");
        url = $"{BaseUrl}/authors/{orcidUrl}";

        author = await _httpClient.GetFromJsonAsync<OpenAlexAuthor>(url, JsonOptions);

        return author ?? throw new InvalidOperationException("Akademisyen kaydı boş döndü.");
    }

    public async Task<List<OpenAlexWork>> GetLatestWorksAsync(string? authorUrl, int count)
    {
        string? authorId = null;
        string? filter = null;
        string? select = null;
        string? url = null;
        OpenAlexWorksResponse? response = null;

        if (string.IsNullOrWhiteSpace(authorUrl))
        {
            throw new ArgumentException("OpenAlex yazar adresi boş olamaz.");
        }

        authorId = authorUrl.Split('/').Last();
        filter = Uri.EscapeDataString($"author.id:{authorId}");
        select = Uri.EscapeDataString("title,publication_year,doi,type,cited_by_count");

        url = $"{BaseUrl}/works" +
              $"?filter={filter}" +
              $"&select={select}" +
              "&sort=publication_date:desc" +
              $"&per-page={count}";

        response = await _httpClient.GetFromJsonAsync<OpenAlexWorksResponse>(url, JsonOptions);

        return response?.Results ?? [];
    }
}
