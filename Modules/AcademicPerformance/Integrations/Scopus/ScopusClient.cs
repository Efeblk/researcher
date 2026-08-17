using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Scopus;

public sealed class ScopusClient
{
    private const string Endpoint = "https://api.elsevier.com/content/author/author_id";

    private static readonly Regex AuthorIdPattern = new(
        @"^\d+$",
        RegexOptions.None);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;

    public ScopusClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _apiKey = configuration["Elsevier:ApiKey"];
    }

    public async Task<ScopusData> GetAuthorAsync(string? scopusAuthorId)
    {
        string? encodedAuthorId = null;
        string? url = null;
        HttpRequestMessage? request = null;
        HttpResponseMessage? response = null;
        ScopusApiResponse? apiResponse = null;
        JsonElement authorElement = default;
        ScopusAuthorResponse? author = null;
        ScopusPreferredName? preferredName = null;
        ScopusInstitution? institution = null;
        ScopusData? scopusData = null;

        if (string.IsNullOrWhiteSpace(scopusAuthorId) ||
            !AuthorIdPattern.IsMatch(scopusAuthorId))
        {
            throw new ArgumentException("Scopus Author ID yalnızca rakamlardan oluşmalı.");
        }

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new ArgumentException("Elsevier API anahtarı boş olamaz.");
        }

        try
        {
            encodedAuthorId = Uri.EscapeDataString(scopusAuthorId);
            url = $"{Endpoint}/{encodedAuthorId}?view=STANDARD";

            request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-ELS-APIKey", _apiKey);
            request.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            apiResponse = await response.Content.ReadFromJsonAsync<ScopusApiResponse>(JsonOptions);
            authorElement = apiResponse?.Author ?? default;
            author = DeserializeAuthor(authorElement);

            if (author is null)
            {
                throw new InvalidOperationException("Scopus akademisyen kaydı boş döndü.");
            }

            preferredName = author.AuthorProfile?.PreferredName;
            institution = author.AuthorProfile?
                .CurrentAffiliation?
                .Affiliation?
                .Institution;

            scopusData = new ScopusData();
            scopusData.AuthorId = scopusAuthorId;
            scopusData.GivenName = preferredName?.GivenName;
            scopusData.Surname = preferredName?.Surname;
            scopusData.AffiliationName = institution?.DisplayName;
            scopusData.AffiliationCity = institution?.Address?.City;
            scopusData.AffiliationCountry = institution?.Address?.Country;
            scopusData.DocumentCount = ParseNullableInt(author.CoreData?.DocumentCount);
            scopusData.CitedByCount = ParseNullableInt(author.CoreData?.CitedByCount);
            scopusData.CitationCount = ParseNullableInt(author.CoreData?.CitationCount);
            scopusData.HIndex = ParseNullableInt(author.HIndex);

            return scopusData;
        }
        finally
        {
            response?.Dispose();
            request?.Dispose();
        }
    }

    private static int? ParseNullableInt(string? value)
    {
        int parsedValue = 0;
        bool isParsed = false;

        isParsed = int.TryParse(value, out parsedValue);

        if (!isParsed)
        {
            return null;
        }

        return parsedValue;
    }

    private static ScopusAuthorResponse? DeserializeAuthor(JsonElement authorElement)
    {
        JsonElement firstAuthorElement = default;
        ScopusAuthorResponse? author = null;

        if (authorElement.ValueKind == JsonValueKind.Object)
        {
            author = authorElement.Deserialize<ScopusAuthorResponse>(JsonOptions);
            return author;
        }

        if (authorElement.ValueKind == JsonValueKind.Array &&
            authorElement.GetArrayLength() > 0)
        {
            firstAuthorElement = authorElement[0];
            author = firstAuthorElement.Deserialize<ScopusAuthorResponse>(JsonOptions);
        }

        return author;
    }
}
