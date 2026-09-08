using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using System.Xml;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Status;

public sealed class ProviderStatusService(HttpClient httpClient, IHttpClientFactory clientFactory,
    IConfiguration configuration)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ProviderStatusResponse? _cached = null;
    private static readonly (string Name, string Key, string Url)[] Providers =
    [
        ("Orcid", "Orcid:ApiBaseUrl", "https://pub.orcid.org/v3.0"),
        ("SearchApi", "SearchApi:ApiBaseUrl", "https://www.searchapi.io/api/v1/search"),
        ("OpenAlex", "OpenAlex:ApiBaseUrl", "https://api.openalex.org"),
        ("WebOfScience", "WebOfScience:ApiBaseUrl", "https://api.clarivate.com/apis/wos-starter/v1"),
        ("Yoksis", "Yoksis:ServiceUrl", "https://servisler.yok.gov.tr/ws/OzgecmisV2"),
        ("AnalysisService", "AnalysisService:BaseUrl", "http://localhost:5011/")
    ];

    public async Task<ProviderStatusResponse> GetAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_cached is not null && _cached.ExpiresAt > DateTime.UtcNow)
                return _cached;
            ProviderStatusDto[] results = await Task.WhenAll(Providers.Select(provider =>
                CheckAsync(provider.Name, configuration[provider.Key] ?? provider.Url, cancellationToken)));
            DateTime now = DateTime.UtcNow;
            await Task.WhenAll(results.Where(result => result.Provider != "AnalysisService").Select(async result =>
                result.LocalBudget = await ReadBudgetAsync(result.Provider, now, cancellationToken)));
            _cached = new() { CheckedAt = now, ExpiresAt = now.AddSeconds(60), Providers = [.. results] };
            return _cached;
        }
        finally { _gate.Release(); }
    }

    private async Task<ProviderStatusDto> CheckAsync(string name, string baseUrl, CancellationToken cancellationToken)
    {
        ProviderStatusDto result = new() { Provider = name,
            CheckKind = name == "Yoksis" ? "WsdlReachability" : name == "SearchApi" ? "AccountUsage" :
                name == "AnalysisService" ? "ServiceHealth" : "ApiRequest" };
        if ((name is "SearchApi" or "WebOfScience") && string.IsNullOrWhiteSpace(configuration[name + ":ApiKey"]) ||
            name == "Yoksis" && (string.IsNullOrWhiteSpace(configuration["Yoksis:Username"]) ||
                string.IsNullOrWhiteSpace(configuration["Yoksis:Password"])))
        {
            result.Status = "NotConfigured";
            return result;
        }
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        Stopwatch timer = Stopwatch.StartNew();
        try
        {
            using HttpRequestMessage request = CreateRequest(name, baseUrl);
            using HttpClient? healthClient = name == "AnalysisService" ? clientFactory.CreateClient("ProviderStatus") : null;
            using HttpResponseMessage response = await (healthClient ?? httpClient).SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            result.HttpStatusCode = (int)response.StatusCode;
            result.CheckedAt = DateTime.UtcNow;
            result.Status = (int)response.StatusCode switch
            {
                >= 200 and < 300 => name == "Yoksis" ? "Reachable" : "Healthy",
                401 or 403 => "Unauthorized",
                429 => response.Headers.Contains("X-Academic-Local-Deferral") ? "LocallyLimited" : "RateLimited",
                >= 500 => "Unavailable",
                _ => "UnexpectedResponse"
            };
            if (response.Headers.RetryAfter is not null)
                result.RetryAt = ProviderRateLimitHandler.GetRetryAt(response, DateTime.UtcNow);
            result.ProviderQuotas = ParseHeaderQuotas(response);
            if (name == "OpenAlex")
                foreach (ProviderQuotaDto quota in result.ProviderQuotas)
                {
                    quota.Unit = "credits";
                    if (quota.Window == "unspecified") quota.Window = "day";
                }
            if (response.IsSuccessStatusCode)
            {
                await response.Content.LoadIntoBufferAsync(1024 * 1024, timeout.Token);
                string body = await response.Content.ReadAsStringAsync(timeout.Token);
                if (name == "Yoksis")
                {
                    using XmlReader reader = XmlReader.Create(new StringReader(body), new XmlReaderSettings
                    { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                    XDocument document = XDocument.Load(reader);
                    if (document.Root?.Name != XName.Get("definitions", "http://schemas.xmlsoap.org/wsdl/"))
                        result.Status = "UnexpectedResponse";
                }
                else
                {
                    using JsonDocument document = JsonDocument.Parse(body);
                    JsonElement root = document.RootElement;
                    string expectedProperty = name switch
                    {
                        "Orcid" => "num-found", "OpenAlex" => "results", "WebOfScience" => "metadata",
                        "AnalysisService" => "status", _ => "account"
                    };
                    if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(expectedProperty, out _))
                        result.Status = "UnexpectedResponse";
                    if (name == "SearchApi")
                    {
                        result.ProviderQuotas = ParseSearchApiQuotas(root);
                        if (result.ProviderQuotas.Count == 0) result.Status = "UnexpectedResponse";
                    }
                }
            }
            if (name == "Yoksis")
                result.Message = "WSDL reachability only; SOAP operations and account quota are not verified.";
            if (name == "AnalysisService")
                result.Message = "Analysis host only; AI provider health and quota are not verified.";
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { result.Status = "Timeout"; }
        catch (HttpRequestException) { result.Status = "Unavailable"; }
        catch (SqlException) { result.Status = "LocalBudgetUnavailable"; }
        catch (JsonException) { result.Status = "UnexpectedResponse"; }
        catch (XmlException) { result.Status = "UnexpectedResponse"; }
        catch (UriFormatException) { result.Status = "NotConfigured"; }
        finally { result.LatencyMilliseconds = timer.ElapsedMilliseconds; }
        result.CheckedAt ??= DateTime.UtcNow;
        return result;
    }

    private HttpRequestMessage CreateRequest(string name, string baseUrl)
    {
        string url = baseUrl.TrimEnd('/');
        url = name switch
        {
            "Orcid" => url + "/search/?q=orcid&rows=0",
            "SearchApi" => new Uri(new Uri(url), "me").AbsoluteUri,
            "OpenAlex" => url + "/works?per_page=1&select=id",
            "WebOfScience" => url + "/documents?q=PY%3D1900&db=WOS&limit=1&page=1",
            "Yoksis" => url + "?wsdl",
            "AnalysisService" => url + "/health",
            _ => throw new InvalidOperationException("Unknown provider.")
        };
        Uri uri = new(url);
        if (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback) || !string.IsNullOrEmpty(uri.UserInfo))
            throw new UriFormatException("Provider URL must use HTTPS or loopback HTTP.");
        HttpRequestMessage request = new(HttpMethod.Get, uri);
        request.Headers.Accept.ParseAdd(name == "Yoksis" ? "text/xml" : "application/json");
        if (name == "OpenAlex" && !string.IsNullOrWhiteSpace(configuration["OpenAlex:ApiKey"]))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration["OpenAlex:ApiKey"]!.Trim());
        if (name == "SearchApi")
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration["SearchApi:ApiKey"]!.Trim());
        if (name == "Orcid" && !string.IsNullOrWhiteSpace(configuration["Orcid:AccessToken"]))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration["Orcid:AccessToken"]!.Trim());
        if (name == "WebOfScience")
            request.Headers.Add("X-ApiKey", configuration["WebOfScience:ApiKey"]!.Trim());
        if (name == "Yoksis")
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(
                Encoding.UTF8.GetBytes(configuration["Yoksis:Username"] + ":" + configuration["Yoksis:Password"])));
        return request;
    }

    public static List<ProviderQuotaDto> ParseSearchApiQuotas(JsonElement root)
    {
        List<ProviderQuotaDto> quotas = [];
        if (root.ValueKind != JsonValueKind.Object) return quotas;
        if (root.TryGetProperty("account", out JsonElement account) && account.ValueKind == JsonValueKind.Object)
            quotas.Add(new() { Source = "AccountApi", Window = "month", Unit = "searches",
                Limit = Number(account, "monthly_allowance"), Used = Number(account, "current_month_usage"),
                Remaining = Number(account, "remaining_credits") });
        if (root.TryGetProperty("api_usage", out JsonElement usage) && usage.ValueKind == JsonValueKind.Object)
        {
            decimal? limit = Number(usage, "hourly_rate_limit"), used = Number(usage, "searches_this_hour");
            quotas.Add(new() { Source = "AccountApi", Window = "hour", Unit = "searches", Limit = limit,
                Used = used, Remaining = limit.HasValue && used.HasValue ? Math.Max(0, limit.Value - used.Value) : null });
        }
        return quotas;
    }

    private static decimal? Number(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetDecimal(out decimal number) && number >= 0 ? number : null;

    public static List<ProviderQuotaDto> ParseHeaderQuotas(HttpResponseMessage response)
    {
        List<ProviderQuotaDto> quotas = [];
        foreach (string suffix in new[] { "", "-Day", "-Second" })
        {
            decimal? limit = Header(response, "X-RateLimit-Limit" + suffix);
            decimal? remaining = Header(response, "X-RateLimit-Remaining" + suffix);
            if (limit.HasValue || remaining.HasValue)
                quotas.Add(new() { Source = "ResponseHeaders", Window = suffix == "" ? "unspecified" : suffix[1..].ToLowerInvariant(),
                    Limit = limit, Remaining = remaining });
        }
        return quotas;
    }

    private static decimal? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) && decimal.TryParse(values.FirstOrDefault(),
            NumberStyles.Number, CultureInfo.InvariantCulture, out decimal number) && number >= 0 ? number : null;

    private async Task<LocalProviderBudgetDto> ReadBudgetAsync(string name, DateTime now, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        int limit = configuration.GetValue($"ProviderRequestLimits:{name}:DailyRequestLimit", 0);
        LocalProviderBudgetDto result = new() { DailyRequestLimit = limit > 0 ? limit : null,
            MinimumIntervalMilliseconds = configuration.GetValue($"ProviderRequestLimits:{name}:MinimumIntervalMilliseconds", 1000),
            ResetsAt = now.Date.AddDays(1) };
        try
        {
            await using SqlConnection connection = new(configuration.GetConnectionString("AcademicDatabase"));
            await connection.OpenAsync(timeout.Token);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandTimeout = 5;
            command.CommandText = "SELECT BudgetDate, RequestsToday, NextAllowedAt FROM ProviderRequestBudgets WHERE Provider = @provider";
            command.Parameters.AddWithValue("@provider", name);
            await using SqlDataReader reader = await command.ExecuteReaderAsync(timeout.Token);
            int used = 0;
            if (await reader.ReadAsync(timeout.Token))
            {
                used = reader.GetDateTime(0).Date == now.Date ? reader.GetInt32(1) : 0;
                DateTime next = DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc);
                result.NextAllowedAt = next > now ? next : null;
            }
            result.RequestsToday = used;
            result.RemainingToday = limit > 0 ? Math.Max(0, limit - used) : null;
            if (limit > 0 && used >= limit)
                result.NextAllowedAt = result.NextAllowedAt > result.ResetsAt ? result.NextAllowedAt : result.ResetsAt;
            result.Status = limit > 0 && used >= limit ? "Exhausted" : result.NextAllowedAt > now ? "Waiting" : "Available";
        }
        catch (SqlException) { result.Status = "Unavailable"; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { result.Status = "Unavailable"; }
        return result;
    }
}
