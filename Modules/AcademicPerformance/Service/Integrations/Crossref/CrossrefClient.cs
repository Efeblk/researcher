using System.Net;
using System.Text.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries.Enrichment;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.RateLimiting;
using Microsoft.Extensions.Configuration;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Crossref;

public sealed class CrossrefClient(HttpClient httpClient, IConfiguration configuration)
{
    private const long DefaultMaximumResponseBytes = 4L * 1024 * 1024;

    public async Task<CrossrefWork> GetAsync(string personelId, string doi, CancellationToken cancellationToken = default)
    {
        string root = GetApiBaseUrl();
        using HttpRequestMessage request = new(HttpMethod.Get, $"{root}/works/{Uri.EscapeDataString(doi)}");
        request.Headers.Accept.ParseAdd("application/json");
        string? mailto = configuration["Crossref:Mailto"];
        if (!string.IsNullOrWhiteSpace(mailto))
            request.RequestUri = new(request.RequestUri + "?mailto=" + Uri.EscapeDataString(mailto.Trim()));
        request.Options.Set(ProviderRateLimitHandler.ExpectedNotFound, true);
        request.Options.Set(ProviderRateLimitHandler.ResponseBufferLimit, 4L * 1024 * 1024);
        using HttpResponseMessage response = await httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return new() { PersonelId = personelId, Doi = doi, FetchedAt = DateTime.UtcNow, Found = false };
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(GetMaximumResponseBytes(), cancellationToken);
        string raw = await response.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument document = JsonDocument.Parse(raw);
        JsonElement message = document.RootElement.GetProperty("message");
        string returnedDoi = NormalizeDoi(Text(message, "DOI"));
        if (!returnedDoi.Equals(doi, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Crossref returned metadata for a different DOI.");
        int[]? date = DateParts(message, "published") ?? DateParts(message, "issued");
        return new()
        {
            PersonelId = personelId, Doi = doi, Found = true, FetchedAt = DateTime.UtcNow,
            Title = First(message, "title"), Authors = Authors(message), ContainerTitle = First(message, "container-title"),
            Type = Text(message, "type"), PublicationYear = date is { Length: > 0 } ? date[0] : null,
            PublicationDate = ToDate(date), CitedByCount = Int(message, "is-referenced-by-count"),
            Url = Text(message, "URL"), Abstract = ArticleAbstractReader.FromPayload(raw, "Crossref"),
            RawDataJson = raw
        };
    }

    public static string NormalizeDoi(string? value)
    {
        string result = (value ?? string.Empty).Trim();
        foreach (string prefix in new[] { "https://doi.org/", "http://doi.org/", "https://dx.doi.org/", "http://dx.doi.org/", "doi:" })
        {
            if (result.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                result = result[prefix.Length..];
            }
        }
        return result.Trim().ToLowerInvariant();
    }

    private static string? Text(JsonElement value, string name) => value.TryGetProperty(name, out JsonElement item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
    private static int? Int(JsonElement value, string name) => value.TryGetProperty(name, out JsonElement item) &&
        item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out int number) ? number : null;
    private static string? First(JsonElement value, string name) => value.TryGetProperty(name, out JsonElement item) && item.ValueKind == JsonValueKind.Array && item.GetArrayLength() > 0 ? item[0].GetString() : null;
    private static string? Authors(JsonElement value) => value.TryGetProperty("author", out JsonElement array) && array.ValueKind == JsonValueKind.Array
        ? string.Join("; ", array.EnumerateArray().Select(a => Text(a, "name") ?? string.Join(" ", new[] { Text(a, "given"), Text(a, "family") }.Where(x => !string.IsNullOrWhiteSpace(x))))) : null;
    private static int[]? DateParts(JsonElement value, string name) => value.TryGetProperty(name, out JsonElement date) &&
        date.ValueKind == JsonValueKind.Object && date.TryGetProperty("date-parts", out JsonElement parts) &&
        parts.ValueKind == JsonValueKind.Array && parts.GetArrayLength() > 0 && parts[0].ValueKind == JsonValueKind.Array
            ? parts[0].EnumerateArray().Where(x => x.TryGetInt32(out _)).Select(x => x.GetInt32()).ToArray() : null;
    private static DateTime? ToDate(int[]? parts)
    {
        try
        {
            return parts is { Length: > 0 }
                ? new DateTime(parts[0], parts.Length > 1 ? parts[1] : 1,
                    parts.Length > 2 ? parts[2] : 1)
                : null;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private long GetMaximumResponseBytes()
    {
        return long.TryParse(
                configuration["ArticleMetadataEnrichment:MaximumResponseBytes"],
                out long maximumResponseBytes) &&
            maximumResponseBytes is >= 1024 and <= 16L * 1024 * 1024
                ? maximumResponseBytes
                : DefaultMaximumResponseBytes;
    }

    private string GetApiBaseUrl()
    {
        string value = configuration["Crossref:ApiBaseUrl"] ?? "https://api.crossref.org";
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException("Crossref:ApiBaseUrl must use HTTPS or loopback HTTP.");
        }

        return value.TrimEnd('/');
    }
}
