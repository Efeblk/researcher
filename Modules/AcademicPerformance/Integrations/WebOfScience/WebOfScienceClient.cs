using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.WebOfScience;

public sealed class WebOfScienceClient
{
    private const string Endpoint = "https://api.clarivate.com/apis/wos-researcher/researchers";

    private static readonly Regex ResearcherIdPattern = new(
        @"^[A-Z]+-\d{4}-\d{4}$",
        RegexOptions.IgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;

    public WebOfScienceClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _apiKey = configuration["Clarivate:ApiKey"];
    }

    public async Task<WebOfScienceData> GetResearcherAsync(string? researcherId)
    {
        string? encodedResearcherId = null;
        string? url = null;
        HttpRequestMessage? request = null;
        HttpResponseMessage? response = null;
        WebOfScienceApiResponse? apiResponse = null;
        WebOfScienceAffiliation? primaryAffiliation = null;
        WebOfScienceData? webOfScienceData = null;

        if (string.IsNullOrWhiteSpace(researcherId) ||
            !ResearcherIdPattern.IsMatch(researcherId))
        {
            throw new ArgumentException(
                "ResearcherID A-1009-2008 biçiminde olmalı.");
        }

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new ArgumentException("Clarivate API anahtarı boş olamaz.");
        }

        try
        {
            encodedResearcherId = Uri.EscapeDataString(researcherId);
            url = $"{Endpoint}/{encodedResearcherId}";

            request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-ApiKey", _apiKey);
            request.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            apiResponse = await response.Content.ReadFromJsonAsync<WebOfScienceApiResponse>(
                JsonOptions);

            if (apiResponse is null)
            {
                throw new InvalidOperationException(
                    "Web of Science akademisyen kaydı boş döndü.");
            }

            primaryAffiliation = apiResponse.Organization?
                .PrimaryAffiliations?
                .FirstOrDefault();

            webOfScienceData = new WebOfScienceData();
            webOfScienceData.Rid = apiResponse.Ids?.Rids?.FirstOrDefault() ?? researcherId;
            webOfScienceData.FullName = apiResponse.Name?.FullName;
            webOfScienceData.FirstName = apiResponse.Name?.FirstName;
            webOfScienceData.LastName = apiResponse.Name?.LastName;
            webOfScienceData.PrimaryAffiliation = primaryAffiliation?.EnhancedName
                ?? primaryAffiliation?.Name;
            webOfScienceData.Address = primaryAffiliation?.Address;
            webOfScienceData.Country = primaryAffiliation?.Country;
            webOfScienceData.IsClaimed = apiResponse.ClaimStatus;
            webOfScienceData.DocumentCount = apiResponse.MetricsAllTime?.Documents?.Count;
            webOfScienceData.TotalTimesCited = apiResponse.MetricsAllTime?.TotalTimesCited;
            webOfScienceData.TotalCitingPublications = apiResponse
                .MetricsAllTime?
                .TotalCitingPublications;
            webOfScienceData.HIndex = apiResponse.MetricsAllTime?.HIndex;

            return webOfScienceData;
        }
        finally
        {
            response?.Dispose();
            request?.Dispose();
        }
    }
}
