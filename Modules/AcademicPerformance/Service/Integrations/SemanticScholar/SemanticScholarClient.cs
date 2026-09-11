using System.Net;
using System.Text.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Crossref;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.RateLimiting;
using Microsoft.Extensions.Options;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.SemanticScholar;

public sealed record SemanticScholarSnapshot(SemanticScholarPaper Paper, List<SemanticScholarCitation> Citations);
public sealed record SemanticScholarCitationPage(int? Total, int? NextOffset, List<SemanticScholarCitation> Citations);

public sealed class SemanticScholarClient(HttpClient httpClient, IOptions<SemanticScholarOptions> configured)
{
    private const string PaperFields = "paperId,externalIds,title,abstract,url,year,venue,publicationTypes,publicationDate,journal,fieldsOfStudy,s2FieldsOfStudy,openAccessPdf,citationCount,referenceCount,influentialCitationCount,authors,tldr,textAvailability";
    private const string CitationFields = "contexts,contextsWithIntent,intents,isInfluential,citingPaper.paperId,citingPaper.title,citingPaper.externalIds,citingPaper.authors";

    public async Task<SemanticScholarSnapshot> GetAsync(string doi, CancellationToken cancellationToken = default)
    {
        SemanticScholarOptions options = configured.Value;
        SemanticScholarPaper paper = await GetPaperAsync(doi, cancellationToken);
        List<SemanticScholarCitation> citations = [];
        if (paper.Found && !string.IsNullOrWhiteSpace(paper.PaperId) && options.MaximumCitationsPerPaper > 0)
            await FetchCitationsAsync(options.ApiBaseUrl.TrimEnd('/'), paper, citations, options, cancellationToken);
        paper.CitationsFetched = citations.Count;
        return new(paper, citations);
    }

    public async Task<SemanticScholarPaper> GetPaperAsync(string doi, CancellationToken cancellationToken = default)
    {
        SemanticScholarOptions options = configured.Value;
        string root = options.ApiBaseUrl.TrimEnd('/');
        using HttpRequestMessage request = CreateRequest($"{root}/paper/DOI:{Uri.EscapeDataString(doi)}?fields={Uri.EscapeDataString(PaperFields)}", options);
        request.Options.Set(ProviderRateLimitHandler.ExpectedNotFound, true);
        request.Options.Set(ProviderRateLimitHandler.ResponseBufferLimit, 4L * 1024 * 1024);
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new() { NormalizedDoi = doi, Found = false, FetchedAt = DateTime.UtcNow };
        }
        response.EnsureSuccessStatusCode();
        string raw = await response.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument document = JsonDocument.Parse(raw);
        JsonElement rootElement = document.RootElement;
        string? returnedDoi = ExternalId(rootElement, "DOI");
        if (!string.Equals(CrossrefClient.NormalizeDoi(returnedDoi), doi, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Semantic Scholar returned metadata for a different DOI.");
        SemanticScholarPaper paper = ParsePaper(rootElement, doi, raw);
        if (string.IsNullOrWhiteSpace(paper.PaperId)) throw new InvalidDataException("Semantic Scholar paper response has no paperId.");
        return paper;
    }

    public async Task<SemanticScholarCitationPage> GetCitationPageAsync(string paperId, int offset, int limit,
        CancellationToken cancellationToken = default)
    {
        SemanticScholarOptions options = configured.Value;
        string root = options.ApiBaseUrl.TrimEnd('/');
        string url = $"{root}/paper/{Uri.EscapeDataString(paperId)}/citations?offset={offset}&limit={limit}&fields={Uri.EscapeDataString(CitationFields)}";
        using HttpRequestMessage request = CreateRequest(url, options);
        request.Options.Set(ProviderRateLimitHandler.ResponseBufferLimit, 8L * 1024 * 1024);
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        string raw = await response.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument document = JsonDocument.Parse(raw);
        JsonElement page = document.RootElement;
        if (!page.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Semantic Scholar citation page has no data array.");
        List<SemanticScholarCitation> citations = data.EnumerateArray().Select(ParseCitation).Where(x => x is not null)
            .Cast<SemanticScholarCitation>().DistinctBy(x => x.CitingPaperId).ToList();
        int? next = Int(page, "next");
        if (next is not null && next <= offset) throw new InvalidDataException("Semantic Scholar returned a non-advancing citation offset.");
        return new(Int(page, "total"), next, citations);
    }

    public static string NormalizeDoi(string? value) => CrossrefClient.NormalizeDoi(value);

    private async Task FetchCitationsAsync(string root, SemanticScholarPaper paper,
        List<SemanticScholarCitation> target, SemanticScholarOptions options, CancellationToken cancellationToken)
    {
        int offset = 0;
        HashSet<string> seen = new(StringComparer.Ordinal);
        while (target.Count < options.MaximumCitationsPerPaper)
        {
            int limit = Math.Min(options.CitationPageSize, options.MaximumCitationsPerPaper - target.Count);
            string url = $"{root}/paper/{Uri.EscapeDataString(paper.PaperId!)}/citations?offset={offset}&limit={limit}&fields={Uri.EscapeDataString(CitationFields)}";
            using HttpRequestMessage request = CreateRequest(url, options);
            request.Options.Set(ProviderRateLimitHandler.ResponseBufferLimit, 8L * 1024 * 1024);
            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            string raw = await response.Content.ReadAsStringAsync(cancellationToken);
            using JsonDocument document = JsonDocument.Parse(raw);
            JsonElement page = document.RootElement;
            paper.CitationTotal ??= Int(page, "total");
            JsonElement data = page.TryGetProperty("data", out JsonElement items) ? items : default;
            if (data.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Semantic Scholar citation page has no data array.");
            int received = 0;
            foreach (JsonElement item in data.EnumerateArray())
            {
                received++;
                SemanticScholarCitation? citation = ParseCitation(item);
                if (citation is not null && seen.Add(citation.CitingPaperId)) target.Add(citation);
            }
            int requestedOffset = offset;
            offset += received;
            int? next = Int(page, "next");
            if (received == 0 || next is null)
            {
                paper.CitationsComplete = true;
                break;
            }
            if (next.Value <= requestedOffset) throw new InvalidDataException("Semantic Scholar returned a non-advancing citation offset.");
            offset = next.Value;
        }
        if (paper.CitationTotal is int total && offset >= total) paper.CitationsComplete = true;
    }

    private static SemanticScholarPaper ParsePaper(JsonElement value, string doi, string raw) => new()
    {
        NormalizedDoi = doi, Found = true, FetchedAt = DateTime.UtcNow,
        PaperId = Text(value, "paperId"), Title = Text(value, "title"), Abstract = Text(value, "abstract"),
        AuthorsJson = Json(value, "authors"), Year = Int(value, "year"), Venue = Text(value, "venue"),
        PublicationTypesJson = Json(value, "publicationTypes"), PublicationDate = Date(value, "publicationDate"), JournalJson = Json(value, "journal"),
        FieldsOfStudyJson = Json(value, "s2FieldsOfStudy") ?? Json(value, "fieldsOfStudy"),
        OpenAccessPdfJson = Json(value, "openAccessPdf"), CitationCount = Int(value, "citationCount"),
        ReferenceCount = Int(value, "referenceCount"), InfluentialCitationCount = Int(value, "influentialCitationCount"),
        Url = Text(value, "url"), TldrJson = Json(value, "tldr"), TextAvailability = Text(value, "textAvailability"), RawDataJson = raw
    };

    private static SemanticScholarCitation? ParseCitation(JsonElement value)
    {
        if (!value.TryGetProperty("citingPaper", out JsonElement paper) || paper.ValueKind != JsonValueKind.Object) return null;
        string? paperId = Text(paper, "paperId");
        if (string.IsNullOrWhiteSpace(paperId)) return null;
        SemanticScholarCitation result = new()
        {
            CitingPaperId = paperId, CitingDoi = NormalizeOptionalDoi(ExternalId(paper, "DOI")),
            CitingTitle = Text(paper, "title"), CitingAuthorsJson = Json(paper, "authors"),
            IsInfluential = Bool(value, "isInfluential"), IntentsJson = Json(value, "intents"), RawDataJson = value.GetRawText()
        };
        if (value.TryGetProperty("contextsWithIntent", out JsonElement contexts) && contexts.ValueKind == JsonValueKind.Array)
        {
            int ordinal = 0;
            foreach (JsonElement context in contexts.EnumerateArray())
            {
                string? text = Text(context, "context");
                if (!string.IsNullOrWhiteSpace(text)) result.Contexts.Add(new() { Ordinal = ordinal++, Context = text, IntentsJson = Json(context, "intents") });
            }
        }
        if (result.Contexts.Count == 0 && value.TryGetProperty("contexts", out JsonElement plain) && plain.ValueKind == JsonValueKind.Array)
        {
            int ordinal = 0;
            foreach (JsonElement context in plain.EnumerateArray())
                if (context.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(context.GetString()))
                    result.Contexts.Add(new() { Ordinal = ordinal++, Context = context.GetString()! });
        }
        return result;
    }

    private static HttpRequestMessage CreateRequest(string url, SemanticScholarOptions options)
    {
        HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("application/json");
        if (!string.IsNullOrWhiteSpace(options.ApiKey)) request.Headers.Add("x-api-key", options.ApiKey.Trim());
        return request;
    }
    private static string? NormalizeOptionalDoi(string? value) { string doi = NormalizeDoi(value); return doi.Length == 0 ? null : doi; }
    private static string? ExternalId(JsonElement value, string name) => value.TryGetProperty("externalIds", out JsonElement ids) && ids.ValueKind == JsonValueKind.Object ? Text(ids, name) : null;
    private static string? Text(JsonElement value, string name) => value.TryGetProperty(name, out JsonElement item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
    private static int? Int(JsonElement value, string name) => value.TryGetProperty(name, out JsonElement item) && item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out int number) ? number : null;
    private static bool? Bool(JsonElement value, string name) => value.TryGetProperty(name, out JsonElement item) && item.ValueKind is JsonValueKind.True or JsonValueKind.False ? item.GetBoolean() : null;
    private static DateTime? Date(JsonElement value, string name) => DateTime.TryParse(Text(value, name), out DateTime date) ? date : null;
    private static string? Json(JsonElement value, string name) => value.TryGetProperty(name, out JsonElement item) && item.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined ? item.GetRawText() : null;
}
