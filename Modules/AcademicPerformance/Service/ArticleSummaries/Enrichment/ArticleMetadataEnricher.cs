using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Crossref;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.RateLimiting;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries.Enrichment;

public sealed partial class ArticleMetadataEnricher(
    OpenAlexClient openAlexClient,
    CrossrefClient crossrefClient,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IMemoryCache cache,
    IOptions<ArticleMetadataEnrichmentOptions> options)
{
    private const string CacheKeyPrefix = "ArticleMetadataEnrichment:";
    private static readonly IReadOnlyList<AcademicWorkSource> NoSources = [];

    public async Task<ArticleMetadataResult> EnrichAsync(
        string personelId,
        string? doi,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string normalizedDoi = CrossrefClient.NormalizeDoi(doi);
        if (normalizedDoi.Length > 512 || !DoiRegex().IsMatch(normalizedDoi))
        {
            return new(null, NoSources, "InvalidDoi");
        }

        string cacheKey = CacheKeyPrefix + normalizedDoi;
        if (cache.TryGetValue(cacheKey, out ArticleMetadataResult? cached) && cached is not null)
        {
            return Clone(cached);
        }

        List<AcademicWorkSource> sources = [];
        List<string> providers = [];
        string? abstractText = null;
        bool providerFailed = false;

        try
        {
            OpenAlexWork? openAlex = await WithTimeoutAsync(
                token => openAlexClient.GetWorkByDoiAsync(normalizedDoi, token),
                cancellationToken);
            if (openAlex is not null)
            {
                abstractText = openAlex.Abstract ??
                    ArticleAbstractReader.FromPayload(openAlex.RawDataJson, "OpenAlex");
                sources.AddRange(AcademicWorkSourceDiscovery.FromPayload(
                    openAlex.RawDataJson, "OpenAlex"));
                AddSource(sources, openAlex.Url, "Landing", "OpenAlex.Work", null);
                AddSource(sources, openAlex.OpenAccessUrl,
                    AcademicWorkSourceDiscovery.LooksLikePdf(openAlex.OpenAccessUrl ?? string.Empty)
                        ? "Pdf" : "Landing",
                    "OpenAlex.BestOpenAccess", true);
                providers.Add("OpenAlex");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            providerFailed = true;
        }
        catch (Exception exception) when (IsProviderFailure(exception))
        {
            providerFailed = true;
        }

        try
        {
            CrossrefWork crossref = await WithTimeoutAsync(
                token => crossrefClient.GetAsync(personelId, normalizedDoi, token),
                cancellationToken);
            if (crossref.Found)
            {
                abstractText ??= crossref.Abstract ??
                    ArticleAbstractReader.FromPayload(crossref.RawDataJson, "Crossref");
                sources.AddRange(AcademicWorkSourceDiscovery.FromPayload(
                    crossref.RawDataJson, "Crossref"));
                AddSource(sources, crossref.Url, "Landing", "Crossref.Work", null);
                providers.Add("Crossref");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            providerFailed = true;
        }
        catch (Exception exception) when (IsProviderFailure(exception))
        {
            providerFailed = true;
        }

        string? email = GetUnpaywallEmail();
        if (email is not null)
        {
            try
            {
                UnpaywallMetadata? unpaywall = await WithTimeoutAsync(
                    token => GetUnpaywallAsync(normalizedDoi, email, token),
                    cancellationToken);
                if (unpaywall is not null)
                {
                    sources.AddRange(unpaywall.Sources);
                    providers.Add("Unpaywall");
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                providerFailed = true;
            }
            catch (Exception exception) when (IsProviderFailure(exception))
            {
                providerFailed = true;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<AcademicWorkSource> boundedSources = AcademicWorkSourceDiscovery
            .Distinct(sources)
            .Take(options.Value.MaximumCandidates)
            .ToArray();
        bool found = !string.IsNullOrWhiteSpace(abstractText) || boundedSources.Count > 0;
        string status = found
            ? providers.Count == 0 ? "Enriched" : "Enriched:" + string.Join(',', providers.Distinct())
            : providerFailed ? "Unavailable" : "NotFound";
        if (email is null)
        {
            status += ";UnpaywallNotConfigured";
        }
        ArticleMetadataResult result = new(abstractText, boundedSources, status);

        if (found)
        {
            cache.Set(cacheKey, result, TimeSpan.FromMinutes(options.Value.PositiveCacheMinutes));
        }
        else if (!providerFailed)
        {
            cache.Set(cacheKey, result, TimeSpan.FromMinutes(options.Value.NegativeCacheMinutes));
        }

        return Clone(result);
    }

    private async Task<T> WithTimeoutAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.RequestTimeoutSeconds));
        return await operation(timeout.Token);
    }

    private async Task<UnpaywallMetadata?> GetUnpaywallAsync(
        string doi,
        string email,
        CancellationToken cancellationToken)
    {
        Uri baseUri = GetTrustedBaseUri(
            configuration["Unpaywall:ApiBaseUrl"] ?? "https://api.unpaywall.org",
            "Unpaywall:ApiBaseUrl");
        UriBuilder requestUri = new(new Uri(baseUri,
            "v2/" + Uri.EscapeDataString(doi)));
        requestUri.Query = "email=" + Uri.EscapeDataString(email);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri.Uri);
        request.Headers.Accept.ParseAdd("application/json");
        request.Options.Set(ProviderRateLimitHandler.ExpectedNotFound, true);
        request.Options.Set(ProviderRateLimitHandler.ResponseBufferLimit,
            options.Value.MaximumResponseBytes);
        HttpClient httpClient = httpClientFactory.CreateClient(
            ArticleMetadataEnrichmentServiceCollectionExtensions.UnpaywallHttpClient);
        using HttpResponseMessage response = await httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(
            options.Value.MaximumResponseBytes, cancellationToken);
        string raw = await response.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument document = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = 64 });
        JsonElement root = document.RootElement;
        string returnedDoi = CrossrefClient.NormalizeDoi(Text(root, "doi"));
        if (!returnedDoi.Equals(doi, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Unpaywall returned metadata for a different DOI.");
        }

        List<AcademicWorkSource> sources = [];
        if (root.TryGetProperty("best_oa_location", out JsonElement best) &&
            best.ValueKind == JsonValueKind.Object)
        {
            AddUnpaywallLocation(best, "Unpaywall.BestOpenAccess", sources);
        }

        if (root.TryGetProperty("oa_locations", out JsonElement locations) &&
            locations.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (JsonElement location in locations.EnumerateArray()
                .Take(options.Value.MaximumCandidates))
            {
                if (location.ValueKind == JsonValueKind.Object)
                {
                    AddUnpaywallLocation(location, $"Unpaywall.Location[{index}]", sources);
                }

                index++;
            }
        }

        return new(AcademicWorkSourceDiscovery.Distinct(sources));
    }

    private static void AddUnpaywallLocation(
        JsonElement location,
        string origin,
        List<AcademicWorkSource> sources)
    {
        AddSource(sources, Text(location, "url_for_pdf"), "Pdf", origin + ".Pdf", true);
        string? landing = Text(location, "url_for_landing_page") ??
            Text(location, "landing_page_url") ?? Text(location, "url");
        AddSource(sources, landing, "Landing", origin + ".Landing", true);
    }

    private static void AddSource(
        List<AcademicWorkSource> sources,
        string? value,
        string kind,
        string origin,
        bool? isOpenAccess)
    {
        string url = value?.Trim() ?? string.Empty;
        if (url.Length is 0 or > 2000 || !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return;
        }

        sources.Add(AcademicWorkSourceDiscovery.Create(url, kind, origin, isOpenAccess));
    }

    private string? GetUnpaywallEmail()
    {
        string email = configuration["Unpaywall:Email"]?.Trim() ?? string.Empty;
        return email.Length is > 3 and <= 254 && email.Contains('@') &&
            !email.Any(char.IsWhiteSpace)
                ? email
                : null;
    }

    private static Uri GetTrustedBaseUri(string value, string settingName)
    {
        if (!Uri.TryCreate(value.TrimEnd('/') + "/", UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException($"{settingName} must use HTTPS or loopback HTTP.");
        }

        return uri;
    }

    private static string? Text(JsonElement value, string propertyName)
    {
        return value.ValueKind == JsonValueKind.Object &&
            value.TryGetProperty(propertyName, out JsonElement property) &&
            property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
    }

    private static bool IsProviderFailure(Exception exception)
    {
        return exception is HttpRequestException or InvalidDataException or JsonException or
            InvalidOperationException;
    }

    private static ArticleMetadataResult Clone(ArticleMetadataResult result)
    {
        return new(result.Abstract, result.Sources.Select(source => new AcademicWorkSource
        {
            Url = source.Url,
            Kind = source.Kind,
            Origin = source.Origin,
            IsOpenAccess = source.IsOpenAccess
        }).ToArray(), result.Status);
    }

    private sealed record UnpaywallMetadata(IReadOnlyList<AcademicWorkSource> Sources);

    [GeneratedRegex(@"^10\.\d{4,9}/[^\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex DoiRegex();
}
