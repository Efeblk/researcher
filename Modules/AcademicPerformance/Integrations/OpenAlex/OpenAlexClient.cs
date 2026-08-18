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
        string? responseJson = null;
        OpenAlexData? openAlexData = null;
        OpenAlexWorksDownload? worksDownload = null;

        if (string.IsNullOrWhiteSpace(researcher.Orcid) || !OrcidPattern.IsMatch(researcher.Orcid))
        {
            throw new ArgumentException("Numara 0000-0000-0000-000X biçiminde olmalı.");
        }

        orcidUrl = Uri.EscapeDataString($"https://orcid.org/{researcher.Orcid}");
        url = $"{BaseUrl}/authors/{orcidUrl}";

        responseJson = await _httpClient.GetStringAsync(url);
        openAlexData = JsonSerializer.Deserialize<OpenAlexData>(responseJson, JsonOptions);

        if (openAlexData is null)
        {
            throw new InvalidOperationException("Akademisyen kaydı boş döndü.");
        }

        worksDownload = await GetAllWorksAsync(openAlexData.AuthorId);
        openAlexData.RawDataJson = responseJson;
        openAlexData.WorksResponsePagesJson = worksDownload.ResponsePagesJson;
        openAlexData.Works = worksDownload.Works;
        researcher.OpenAlex = openAlexData;
    }

    private async Task<OpenAlexWorksDownload> GetAllWorksAsync(string? authorUrl)
    {
        string? authorId = null;
        string? filter = null;
        string? cursor = null;
        string? encodedCursor = null;
        string? url = null;
        string? responseJson = null;
        OpenAlexWorksResponse? response = null;
        OpenAlexWorksDownload? download = null;
        List<OpenAlexWork>? works = null;
        List<OpenAlexWork>? pageWorks = null;
        List<string>? responsePages = null;
        OpenAlexWork? work = null;
        int index = 0;

        if (string.IsNullOrWhiteSpace(authorUrl))
        {
            throw new ArgumentException("OpenAlex yazar adresi boş olamaz.");
        }

        authorId = authorUrl.Split('/').Last();
        filter = Uri.EscapeDataString($"author.id:{authorId}");
        cursor = "*";
        works = [];
        responsePages = [];

        while (!string.IsNullOrWhiteSpace(cursor))
        {
            encodedCursor = Uri.EscapeDataString(cursor);
            url = $"{BaseUrl}/works" +
                  $"?filter={filter}" +
                  "&sort=publication_date:desc" +
                  $"&per_page={PageSize}" +
                  $"&cursor={encodedCursor}";

            responseJson = await _httpClient.GetStringAsync(url);
            responsePages.Add(responseJson);
            response = JsonSerializer.Deserialize<OpenAlexWorksResponse>(
                responseJson,
                JsonOptions);
            pageWorks = response?.Results ?? [];
            AssignRawWorkJson(responseJson, pageWorks);

            if (pageWorks.Count == 0)
            {
                break;
            }

            for (index = 0; index < pageWorks.Count; index++)
            {
                work = pageWorks[index];
                work.Abstract = ReconstructAbstract(work.AbstractInvertedIndex);
                work.Authors = GetAuthorNames(work.Authorships);
                work.Institutions = GetInstitutionNames(work.Authorships);
                work.Keywords = GetNames(work.KeywordValues);
                work.Topics = GetNames(work.TopicValues);
                work.IsOpenAccess = work.OpenAccess?.IsOpenAccess;
                work.OpenAccessStatus = work.OpenAccess?.Status;
                work.OpenAccessUrl = work.OpenAccess?.Url;
                work.FullTextUrl = work.BestOpenAccessLocation?.PdfUrl
                    ?? work.ContentUrls?.Pdf
                    ?? work.OpenAccess?.Url;
                work.License = work.BestOpenAccessLocation?.License
                    ?? work.PrimaryLocation?.License;
                work.Version = work.BestOpenAccessLocation?.Version
                    ?? work.PrimaryLocation?.Version;
                work.Volume = work.Biblio?.Volume;
                work.Issue = work.Biblio?.Issue;
                work.FirstPage = work.Biblio?.FirstPage;
                work.LastPage = work.Biblio?.LastPage;
                work.SourceId = work.PrimaryLocation?.Source?.Id;
                work.SourceName = work.PrimaryLocation?.Source?.DisplayName;
                work.SourceType = work.PrimaryLocation?.Source?.Type;
                work.SourceUrl = work.PrimaryLocation?.LandingPageUrl;
            }

            works.AddRange(pageWorks);
            cursor = response?.Meta?.NextCursor;
        }

        download = new OpenAlexWorksDownload();
        download.Works = works;
        download.ResponsePagesJson = CreateJsonArray(responsePages);

        return download;
    }

    private static void AssignRawWorkJson(
        string responseJson,
        List<OpenAlexWork> works)
    {
        JsonDocument? document = null;
        JsonElement resultsElement = default;
        JsonElement workElement = default;
        int index = 0;
        int workCount = 0;

        using (document = JsonDocument.Parse(responseJson))
        {
            if (!document.RootElement.TryGetProperty("results", out resultsElement) ||
                resultsElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            workCount = Math.Min(works.Count, resultsElement.GetArrayLength());

            for (index = 0; index < workCount; index++)
            {
                workElement = resultsElement[index];
                works[index].RawDataJson = workElement.GetRawText();
            }
        }
    }

    private static string CreateJsonArray(List<string> jsonValues)
    {
        return $"[{string.Join(",", jsonValues)}]";
    }

    private static string? ReconstructAbstract(
        Dictionary<string, List<int>>? invertedIndex)
    {
        int maximumPosition = -1;
        int positionIndex = 0;
        int position = 0;
        string[]? words = null;
        string? abstractText = null;
        KeyValuePair<string, List<int>> entry = default;

        if (invertedIndex is null || invertedIndex.Count == 0)
        {
            return null;
        }

        foreach (KeyValuePair<string, List<int>> currentEntry in invertedIndex)
        {
            entry = currentEntry;

            for (positionIndex = 0; positionIndex < entry.Value.Count; positionIndex++)
            {
                position = entry.Value[positionIndex];

                if (position > maximumPosition)
                {
                    maximumPosition = position;
                }
            }
        }

        if (maximumPosition < 0)
        {
            return null;
        }

        words = new string[maximumPosition + 1];

        foreach (KeyValuePair<string, List<int>> currentEntry in invertedIndex)
        {
            entry = currentEntry;

            for (positionIndex = 0; positionIndex < entry.Value.Count; positionIndex++)
            {
                position = entry.Value[positionIndex];

                if (position >= 0 && position < words.Length)
                {
                    words[position] = entry.Key;
                }
            }
        }

        abstractText = string.Join(" ", words.Where(word => !string.IsNullOrWhiteSpace(word)));

        return string.IsNullOrWhiteSpace(abstractText)
            ? null
            : abstractText;
    }

    private static string? GetAuthorNames(List<OpenAlexAuthorship>? authorships)
    {
        List<string>? names = null;
        int index = 0;
        string? name = null;

        if (authorships is null)
        {
            return null;
        }

        names = [];

        for (index = 0; index < authorships.Count; index++)
        {
            name = authorships[index].Author?.DisplayName;

            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name);
            }
        }

        return names.Count == 0
            ? null
            : string.Join("; ", names);
    }

    private static string? GetInstitutionNames(List<OpenAlexAuthorship>? authorships)
    {
        HashSet<string>? names = null;
        List<OpenAlexInstitution>? institutions = null;
        int authorshipIndex = 0;
        int institutionIndex = 0;
        string? name = null;

        if (authorships is null)
        {
            return null;
        }

        names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (authorshipIndex = 0; authorshipIndex < authorships.Count; authorshipIndex++)
        {
            institutions = authorships[authorshipIndex].Institutions;

            if (institutions is null)
            {
                continue;
            }

            for (institutionIndex = 0; institutionIndex < institutions.Count; institutionIndex++)
            {
                name = institutions[institutionIndex].DisplayName;

                if (!string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name);
                }
            }
        }

        return names.Count == 0
            ? null
            : string.Join("; ", names);
    }

    private static string? GetNames(List<OpenAlexNamedValue>? values)
    {
        List<string>? names = null;
        int index = 0;
        string? name = null;

        if (values is null)
        {
            return null;
        }

        names = [];

        for (index = 0; index < values.Count; index++)
        {
            name = values[index].DisplayName;

            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name);
            }
        }

        return names.Count == 0
            ? null
            : string.Join("; ", names);
    }
}
