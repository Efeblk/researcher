using System.Net;
using System.Text.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.RateLimiting;
using Microsoft.Extensions.Configuration;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.TrDizin;

public sealed class TrDizinClient(HttpClient httpClient, IConfiguration configuration)
{
    private const long MaximumResponseBytes = 8L * 1024 * 1024;

    public async Task<TrDizinProfile?> GetByOrcidAsync(string orcid, CancellationToken cancellationToken = default)
    {
        string root = (configuration["TrDizin:ApiBaseUrl"] ?? "https://search.trdizin.gov.tr").TrimEnd('/');
        string? authorJson = await GetAsync($"{root}/api/public/yazar/orcid?orcid={Uri.EscapeDataString(orcid)}", true, cancellationToken);
        if (authorJson is null) return null;
        using JsonDocument authorDocument = JsonDocument.Parse(authorJson);
        JsonElement author = authorDocument.RootElement;
        if (author.ValueKind != JsonValueKind.Object || !string.Equals(Text(author, "orcid"), orcid,
                StringComparison.OrdinalIgnoreCase) || !Long(author, "id").HasValue)
            return null;

        long authorId = Long(author, "id")!.Value;
        string publicationsJson = (await GetAsync($"{root}/api/authorPublicationsById/{authorId}", false, cancellationToken))!;
        using JsonDocument publicationsDocument = JsonDocument.Parse(publicationsJson);
        JsonElement hits = publicationsDocument.RootElement.GetProperty("hits");
        JsonElement items = hits.GetProperty("hits");
        int total = hits.GetProperty("total").GetProperty("value").GetInt32();
        if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() != total)
            throw new InvalidDataException("TR Dizin author publication response was incomplete.");

        List<TrDizinWork> works = [];
        foreach (JsonElement item in items.EnumerateArray())
        {
            string? publicationId = Text(item, "_id") ?? FirstText(item.GetProperty("fields"), "id");
            if (string.IsNullOrWhiteSpace(publicationId)) throw new InvalidDataException("TR Dizin publication id was missing.");
            string detailJson = (await GetAsync($"{root}/api/publicationById/{Uri.EscapeDataString(publicationId)}", false, cancellationToken))!;
            using JsonDocument detailDocument = JsonDocument.Parse(detailJson);
            JsonElement detailHits = detailDocument.RootElement.GetProperty("hits").GetProperty("hits");
            if (detailHits.ValueKind != JsonValueKind.Array || detailHits.GetArrayLength() != 1)
                throw new InvalidDataException("TR Dizin publication detail response was invalid.");
            JsonElement source = detailHits[0].GetProperty("_source");
            works.Add(Map(publicationId, source, detailJson));
        }
        return new()
        {
            Orcid = orcid, AuthorId = authorId, DisplayName = Text(author, "fullName"),
            PublicationCount = Int(author, "orderPublicationCount"), CitationCount = Int(author, "orderCitationCount"),
            LastUpdatedAt = DateTime.UtcNow, RawAuthorJson = authorJson, RawPublicationsJson = publicationsJson,
            Works = works
        };
    }

    private async Task<string?> GetAsync(string url, bool expectedNotFound, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("application/json");
        if (expectedNotFound) request.Options.Set(ProviderRateLimitHandler.ExpectedNotFound, true);
        request.Options.Set(ProviderRateLimitHandler.ResponseBufferLimit, MaximumResponseBytes);
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound && expectedNotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    internal static TrDizinWork Map(string id, JsonElement source, string raw) => new()
    {
        PublicationId = id,
        Title = Text(source, "orderTitle") ?? Text(source, "title") ?? FirstObjectText(source, "abstracts", "title"),
        Doi = Text(source, "doi"), PublicationYear = NumberOrText(source, "publicationYear") ?? NumberOrText(source, "year") ?? IssueYear(source),
        PublicationType = Text(source, "publicationType") ?? Text(source, "docType"),
        Authors = JoinObjectText(source, "authors", "inPublicationName"),
        Journal = Text(source, "journalTitle") ?? Text(source, "journalName") ?? NestedText(source, "journal", "name"),
        CitationCount = Int(source, "orderCitationCount"), RawDataJson = raw
    };

    private static string? Text(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(name, out JsonElement item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
    private static int? Int(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(name, out JsonElement item) && item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out int number) ? number : null;
    private static int? NumberOrText(JsonElement value, string name) => Int(value, name) ??
        (int.TryParse(Text(value, name), out int number) ? number : null);
    private static long? Long(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(name, out JsonElement item) && item.TryGetInt64(out long number) ? number : null;
    private static string? FirstText(JsonElement value, string name) => value.TryGetProperty(name, out JsonElement array) &&
        array.ValueKind == JsonValueKind.Array && array.GetArrayLength() > 0 ? array[0].ToString() : null;
    private static string? FirstObjectText(JsonElement value, string arrayName, string name) =>
        value.TryGetProperty(arrayName, out JsonElement array) && array.ValueKind == JsonValueKind.Array && array.GetArrayLength() > 0
            ? Text(array[0], name) : null;
    private static string? JoinObjectText(JsonElement value, string arrayName, string name) =>
        value.TryGetProperty(arrayName, out JsonElement array) && array.ValueKind == JsonValueKind.Array
            ? string.Join("; ", array.EnumerateArray().Select(item => Text(item, name)).Where(item => !string.IsNullOrWhiteSpace(item))) : null;
    private static int? IssueYear(JsonElement value) => value.TryGetProperty("issue", out JsonElement issue) &&
        int.TryParse(Text(issue, "year"), out int year) ? year : null;
    private static string? NestedText(JsonElement value, string objectName, string name) =>
        value.TryGetProperty(objectName, out JsonElement item) ? Text(item, name) : null;
}
