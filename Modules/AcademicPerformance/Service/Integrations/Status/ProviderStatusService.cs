using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using System.Xml;
using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
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
        ("TrDizin", "TrDizin:ApiBaseUrl", "https://search.trdizin.gov.tr"),
        ("Crossref", "Crossref:ApiBaseUrl", "https://api.crossref.org"),
        ("Unpaywall", "Unpaywall:ApiBaseUrl", "https://api.unpaywall.org"),
        ("SemanticScholar", "SemanticScholar:ApiBaseUrl", "https://api.semanticscholar.org/graph/v1"),
        ("AnalysisService", "AnalysisService:BaseUrl", "http://localhost:5011/"),
        ("Gemini", "AnalysisService:BaseUrl", "http://localhost:5011/")
    ];

    public async Task<ProviderStatusResponse> GetAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_cached is not null && _cached.ExpiresAt > DateTime.UtcNow)
            {
                RefreshRemainingUsage(_cached, DateTime.UtcNow);
                return _cached;
            }
            ProviderStatusDto[] results = await Task.WhenAll(Providers.Select(provider => provider.Name == "Orcid"
                ? CheckOrcidAsync(configuration[provider.Key] ?? provider.Url, cancellationToken)
                : CheckAsync(provider.Name, configuration[provider.Key] ?? provider.Url, cancellationToken)));
            DateTime budgetAt = DateTime.UtcNow;
            await Task.WhenAll(results.Where(result => result.Provider is not ("AnalysisService" or "Gemini"))
                .Select(async result =>
                result.LocalBudget = await ReadBudgetAsync(result.Provider, budgetAt, cancellationToken)));
            DateTime now = DateTime.UtcNow;
            foreach (ProviderStatusDto result in results)
                result.RemainingUsage = BuildRemainingUsage(result, now);
            _cached = new() { CheckedAt = now, ExpiresAt = now.AddSeconds(60), Providers = [.. results] };
            return _cached;
        }
        finally { _gate.Release(); }
    }

    internal async Task<ProviderStatusDto> CheckOrcidAsync(string baseUrl, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        CancellationToken operationToken = timeout.Token;
        string? connectionString = configuration.GetConnectionString("AcademicDatabase");
        if (string.IsNullOrWhiteSpace(connectionString)) return OrcidCoordinationUnavailable();
        string endpoint;
        try { endpoint = CreateUrl("Orcid", baseUrl); }
        catch (UriFormatException) { return new() { Provider = "Orcid", Status = "NotConfigured" }; }
        string key = "Orcid:" + Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(new Uri(endpoint).GetComponents(UriComponents.SchemeAndServer,
                UriFormat.SafeUnescaped).ToLowerInvariant() + new Uri(endpoint).PathAndQuery)))[..32];
        try
        {
            await using SqlApplicationLock? gate = await SqlApplicationLock.TryAcquireAsync(
                connectionString, "ProviderStatus:" + key, 15000, operationToken);
            if (gate is null) return OrcidCoordinationUnavailable();
            await using (SqlCommand read = gate.Connection.CreateCommand())
            {
                read.CommandText = "SELECT PayloadJson, ExpiresAt FROM ProviderStatusObservations WHERE Provider=@provider AND ExpiresAt>SYSUTCDATETIME()";
                read.Parameters.AddWithValue("@provider", key);
                await using SqlDataReader reader = await read.ExecuteReaderAsync(operationToken);
                if (await reader.ReadAsync(operationToken))
                {
                    DateTime cachedExpiresAt = DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc);
                    try
                    {
                        ProviderStatusDto? cached = JsonSerializer.Deserialize<ProviderStatusDto>(reader.GetString(0));
                        return cached is { Provider: "Orcid", Transport: not null } && cached.Status != "Unknown" &&
                            (cached.Status == "LocalCoordinationPending" || cached.Transport.ObservedAt.HasValue)
                            ? cached : OrcidCoordinationPending(cachedExpiresAt);
                    }
                    catch (JsonException) { return OrcidCoordinationPending(cachedExpiresAt); }
                }
            }
            DateTime reservedAt = await GetDatabaseUtcNowAsync(gate.Connection, operationToken);
            DateTime reservedUntil = reservedAt.AddMinutes(5);
            ProviderStatusDto pending = new() { Provider = "Orcid", Status = "LocalCoordinationPending",
                CheckKind = "OfficialStatus", CheckedAt = reservedAt, RetryAt = reservedUntil,
                CacheScope = "SqlDeployment", Message = "An ORCID status check is reserved by this deployment." };
            await WriteOrcidObservationAsync(gate.Connection, key, reservedAt, reservedUntil, pending,
                operationToken);
            ProviderStatusDto result = await CheckAsync("Orcid", baseUrl, operationToken);
            result.CacheScope = "SqlDeployment";
            DateTime observedAt = await GetDatabaseUtcNowAsync(gate.Connection, operationToken);
            DateTime expiresAt = observedAt.AddMinutes(5);
            result.Transport.ExpiresAt = expiresAt;
            if (result.ReportedHealth is not null) result.ReportedHealth.ExpiresAt = expiresAt;
            await WriteOrcidObservationAsync(gate.Connection, key, observedAt, expiresAt, result,
                operationToken);
            return result;
        }
        catch (SqlException) { return OrcidCoordinationUnavailable(); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return OrcidCoordinationUnavailable(); }
    }

    private static async Task WriteOrcidObservationAsync(SqlConnection connection, string key,
        DateTime observedAt, DateTime expiresAt, ProviderStatusDto result, CancellationToken cancellationToken)
    {
        await using SqlCommand write = connection.CreateCommand();
        write.CommandTimeout = 5;
        write.CommandText = """
            MERGE ProviderStatusObservations WITH (HOLDLOCK) AS target
            USING (SELECT @provider Provider) source ON target.Provider=source.Provider
            WHEN MATCHED THEN UPDATE SET ObservedAt=@observedAt,ExpiresAt=@expiresAt,PayloadJson=@payload
            WHEN NOT MATCHED THEN INSERT (Provider,ObservedAt,ExpiresAt,PayloadJson)
                VALUES (@provider,@observedAt,@expiresAt,@payload);
            """;
        write.Parameters.AddWithValue("@provider", key);
        write.Parameters.AddWithValue("@observedAt", observedAt);
        write.Parameters.AddWithValue("@expiresAt", expiresAt);
        write.Parameters.AddWithValue("@payload", JsonSerializer.Serialize(result));
        await write.ExecuteNonQueryAsync(cancellationToken);
    }

    private static ProviderStatusDto OrcidCoordinationUnavailable() => new()
    {
        Provider = "Orcid", Status = "LocalCoordinationUnavailable", CheckKind = "OfficialStatus",
        CacheScope = "SqlDeployment", Message = "Shared ORCID status-check coordination is unavailable."
    };

    private static ProviderStatusDto OrcidCoordinationPending(DateTime? retryAt = null) => new()
    {
        Provider = "Orcid", Status = "LocalCoordinationPending", CheckKind = "OfficialStatus",
        CacheScope = "SqlDeployment", RetryAt = retryAt,
        Message = "An ORCID status check is reserved by this deployment."
    };

    private static async Task<DateTime> GetDatabaseUtcNowAsync(SqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqlCommand command = connection.CreateCommand();
        command.CommandTimeout = 5;
        command.CommandText = "SELECT SYSUTCDATETIME()";
        return DateTime.SpecifyKind(Convert.ToDateTime(await command.ExecuteScalarAsync(cancellationToken)),
            DateTimeKind.Utc);
    }

    private async Task<ProviderStatusDto> CheckAsync(string name, string baseUrl, CancellationToken cancellationToken)
    {
        ProviderStatusDto result = new() { Provider = name,
            CheckKind = name == "Orcid" ? "OfficialStatus" : name == "Yoksis" ? "WsdlReachability" :
                name == "SearchApi" ? "AccountUsage" : name == "OpenAlex" &&
                !string.IsNullOrWhiteSpace(configuration["OpenAlex:ApiKey"]) ? "AccountQuota" :
                name == "AnalysisService" ? "ServiceHealth" : name == "Gemini" ? "ProviderMetadata" :
                "ApiRequest" };
        if (name is not ("AnalysisService" or "Gemini") &&
            !configuration.GetValue($"ProviderRequestLimits:{name}:Enabled", true))
        {
            result.Status = result.Transport.Status = "Disabled";
            return result;
        }
        if ((name is "SearchApi" or "WebOfScience") && string.IsNullOrWhiteSpace(configuration[name + ":ApiKey"]) ||
            name == "Yoksis" && (string.IsNullOrWhiteSpace(configuration["Yoksis:Username"]) ||
                string.IsNullOrWhiteSpace(configuration["Yoksis:Password"])) ||
            name == "Unpaywall" && !IsValidEmail(configuration["Unpaywall:Email"]))
        {
            result.Status = result.Transport.Status = "NotConfigured";
            return result;
        }
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        Stopwatch timer = Stopwatch.StartNew();
        try
        {
            using HttpRequestMessage request = CreateRequest(name, baseUrl);
            request.Options.Set(ProviderRateLimitHandler.ResponseBufferLimit, 1024L * 1024);
            using HttpClient? healthClient = name is "AnalysisService" or "Gemini"
                ? clientFactory.CreateClient("ProviderStatus") : null;
            using HttpResponseMessage response = await (healthClient ?? httpClient).SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            result.HttpStatusCode = (int)response.StatusCode;
            result.CheckedAt = DateTime.UtcNow;
            result.Status = (int)response.StatusCode switch
            {
                _ when response.Headers.Contains("X-Academic-Provider-Disabled") => "Disabled",
                >= 200 and < 300 => name == "Yoksis" ? "Reachable" : "Healthy",
                401 or 403 => "Unauthorized",
                429 => response.Headers.Contains("X-Academic-Local-Deferral") ? "LocallyLimited" : "RateLimited",
                >= 500 => "Unavailable",
                _ => "UnexpectedResponse"
            };
            result.Transport = new() { Status = result.Status, ObservedAt = result.CheckedAt,
                HttpStatusCode = result.HttpStatusCode };
            if (response.Headers.RetryAfter is not null)
                result.RetryAt = ProviderRateLimitHandler.GetRetryAt(response, DateTime.UtcNow);
            result.ProviderQuotas = name == "Crossref"
                ? ParseCrossrefQuotas(response, result.CheckedAt)
                : ParseHeaderQuotas(response, result.CheckedAt);
            if (result.ProviderQuotas.Count > 0) { result.QuotaAvailability = "Available"; result.QuotaSource = "ObservedHeaders"; }
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
                        "Orcid" => "overallOk", "OpenAlex" => string.IsNullOrWhiteSpace(configuration["OpenAlex:ApiKey"]) ? "results" : "rate_limit", "WebOfScience" => "metadata", "TrDizin" => "orcid", "Crossref" => "message", "SemanticScholar" => "paperId",
                        "Unpaywall" => "doi", "AnalysisService" => "status", "Gemini" => "provider", _ => "account"
                    };
                    if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(expectedProperty, out _))
                        result.Status = "UnexpectedResponse";
                    if (name == "TrDizin" && !IsValidTrDizinResponse(root))
                        result.Status = "UnexpectedResponse";
                    if (name == "Unpaywall" && !IsValidUnpaywallResponse(root))
                        result.Status = "UnexpectedResponse";
                    if (name == "Crossref" && !IsValidCrossrefResponse(root))
                        result.Status = "UnexpectedResponse";
                    if (name == "SemanticScholar" && !IsValidSemanticScholarResponse(root))
                        result.Status = "UnexpectedResponse";
                    if (name == "Orcid") result.ReportedHealth = ParseOrcidHealth(root, result.CheckedAt.Value, result);
                    if (name == "SearchApi")
                    {
                        result.ProviderQuotas = ParseSearchApiQuotas(root, result.CheckedAt);
                        result.QuotaSource = "AccountApi";
                        result.QuotaAvailability = result.ProviderQuotas.Count > 0 ? "Available" : "Unknown";
                        if (result.ProviderQuotas.Count == 0) result.Status = "UnexpectedResponse";
                    }
                    if (name == "OpenAlex" && !string.IsNullOrWhiteSpace(configuration["OpenAlex:ApiKey"]))
                    { result.ProviderQuotas = ParseOpenAlexQuotas(root, result.CheckedAt); result.QuotaSource = "AccountApi";
                        result.QuotaAvailability = result.ProviderQuotas.Count > 0 ? "Available" : "Unknown";
                        if (result.ProviderQuotas.Count == 0) result.Status = "UnexpectedResponse"; }
                    if (name == "AnalysisService" && (root.ValueKind != JsonValueKind.Object ||
                        !root.TryGetProperty("status", out JsonElement serviceStatus) ||
                        serviceStatus.ValueKind != JsonValueKind.String || serviceStatus.GetString() != "Running"))
                        result.Status = "UnexpectedResponse";
                    if (name == "Gemini")
                        result.Status = ParseGeminiHealth(root);
                }
            }
            if (name == "OpenAlex")
                foreach (ProviderQuotaDto quota in result.ProviderQuotas)
                {
                    if (quota.Source == "AccountApi")
                    {
                        quota.Unit = "credits";
                        quota.Window = "day";
                        quota.Scope = "api-key";
                    }
                    else if (quota.SourceFields == "X-RateLimit-Limit,X-RateLimit-Remaining")
                    {
                        quota.Unit = "credits";
                        quota.Window = "day";
                        quota.Scope = string.IsNullOrWhiteSpace(configuration["OpenAlex:ApiKey"])
                            ? "anonymous" : "api-key";
                        if (result.CheckedAt.HasValue)
                        {
                            quota.ResetsAt = ParseOpenAlexResetAt(response, result.CheckedAt.Value);
                            if (quota.ResetsAt.HasValue) quota.SourceFields += ",X-RateLimit-Reset";
                        }
                    }
                }
            if (name == "WebOfScience")
                foreach (ProviderQuotaDto quota in result.ProviderQuotas)
                    if (quota.SourceFields is
                        "X-RateLimit-Limit-Day,X-RateLimit-Remaining-Day" or
                        "X-RateLimit-Limit-Second,X-RateLimit-Remaining-Second")
                    {
                        quota.Unit = "requests";
                        quota.Scope = "api-key";
                    }
            DateTime expiresAt = (result.CheckedAt ?? DateTime.UtcNow).AddSeconds(60);
            result.Transport.ExpiresAt = expiresAt;
            foreach (ProviderQuotaDto quota in result.ProviderQuotas) quota.ExpiresAt = expiresAt;
            if (name == "Yoksis")
                result.Message = "WSDL reachability only; SOAP operations and account quota are not verified.";
            if (name == "AnalysisService")
                result.Message = "Analysis host only; AI provider health and quota are not verified.";
            if (name == "Gemini")
                result.Transport.Status = result.Status;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { result.Status = result.Transport.Status = "Timeout"; }
        catch (HttpRequestException) { result.Status = result.Transport.Status = "Unavailable"; }
        catch (SqlException) { result.Status = "LocalBudgetUnavailable"; }
        catch (JsonException) { result.Status = "UnexpectedResponse"; }
        catch (XmlException) { result.Status = "UnexpectedResponse"; }
        catch (UriFormatException) { result.Status = result.Transport.Status = "NotConfigured"; }
        finally { result.LatencyMilliseconds = result.Transport.LatencyMilliseconds = timer.ElapsedMilliseconds; }
        result.CheckedAt ??= DateTime.UtcNow;
        result.Transport.ExpiresAt ??= result.CheckedAt.Value.AddSeconds(60);
        return result;
    }

    internal HttpRequestMessage CreateRequest(string name, string baseUrl)
    {
        string url = CreateUrl(name, baseUrl);
        Uri uri = new(url);
        HttpRequestMessage request = new(HttpMethod.Get, uri);
        request.Headers.Accept.ParseAdd(name == "Yoksis" ? "text/xml" : "application/json");
        if (name == "OpenAlex" && !string.IsNullOrWhiteSpace(configuration["OpenAlex:ApiKey"]))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration["OpenAlex:ApiKey"]!.Trim());
        if (name == "SearchApi")
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration["SearchApi:ApiKey"]!.Trim());
        if (name == "WebOfScience")
            request.Headers.Add("X-ApiKey", configuration["WebOfScience:ApiKey"]!.Trim());
        if (name == "SemanticScholar" && !string.IsNullOrWhiteSpace(configuration["SemanticScholar:ApiKey"]))
            request.Headers.Add("x-api-key", configuration["SemanticScholar:ApiKey"]!.Trim());
        if (name == "Gemini" && !string.IsNullOrWhiteSpace(configuration["AnalysisService:ApiKey"]))
            request.Headers.Add("X-Analysis-Key", configuration["AnalysisService:ApiKey"]!.Trim());
        if (name == "Yoksis")
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(
                Encoding.UTF8.GetBytes(configuration["Yoksis:Username"] + ":" + configuration["Yoksis:Password"])));
        return request;
    }

    private string CreateUrl(string name, string baseUrl)
    {
        string url = baseUrl.TrimEnd('/');
        url = name switch
        {
            "Orcid" => url + (new Uri(url).Host.Equals("api.orcid.org", StringComparison.OrdinalIgnoreCase) ? "/apiStatus" : "/pubStatus"),
            "SearchApi" => new Uri(new Uri(url), "me").AbsoluteUri,
            "OpenAlex" => url + (string.IsNullOrWhiteSpace(configuration["OpenAlex:ApiKey"]) ? "/works?per_page=1&select=id" : "/rate-limit"),
            "WebOfScience" => url + "/documents?q=PY%3D1900&db=WOS&limit=1&page=1",
            "Yoksis" => url + "?wsdl",
            "TrDizin" => url + "/api/public/yazar/orcid?orcid=0000-0001-8560-7482",
            "Crossref" => url + "/works/10.1038/nphys1170" +
                (string.IsNullOrWhiteSpace(configuration["Crossref:Mailto"]) ? string.Empty :
                    "?mailto=" + Uri.EscapeDataString(configuration["Crossref:Mailto"]!.Trim())),
            "Unpaywall" => url + "/v2/10.1038/nphys1170?email=" +
                Uri.EscapeDataString(configuration["Unpaywall:Email"]!.Trim()),
            "SemanticScholar" => url + "/paper/DOI:10.1038/nphys1170?fields=paperId",
            "AnalysisService" => url + "/health",
            "Gemini" => url + "/api/v1/internal/provider-status/gemini",
            _ => throw new InvalidOperationException("Unknown provider.")
        };
        Uri uri = new(url);
        if (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback) || !string.IsNullOrEmpty(uri.UserInfo))
            throw new UriFormatException("Provider URL must use HTTPS or loopback HTTP.");
        return uri.AbsoluteUri;
    }

    public static ProviderReportedHealthDto ParseOrcidHealth(JsonElement root, DateTime observedAt,
        ProviderStatusDto? provider = null)
    {
        bool overallValid = TryBoolean(root, "overallOk", out bool overall);
        bool tomcatValid = TryBoolean(root, "tomcatUp", out bool tomcat);
        bool databaseValid = TryBoolean(root, "dbConnectionOk", out bool database);
        bool readOnlyValid = TryBoolean(root, "readOnlyDbConnectionOk", out bool readOnlyDatabase);
        bool valid = overallValid && tomcatValid && databaseValid && readOnlyValid;
        ProviderReportedHealthDto result = new() { Source = "OfficialStatusApi", ObservedAt = observedAt,
            Status = valid ? overall ? "Healthy" : "Unhealthy" : "UnexpectedResponse",
            OverallOk = overallValid ? overall : null, TomcatUp = tomcatValid ? tomcat : null,
            DbConnectionOk = databaseValid ? database : null,
            ReadOnlyDbConnectionOk = readOnlyValid ? readOnlyDatabase : null };
        if (provider is not null && (!valid || !overall)) provider.Status = result.Status;
        return result;
    }

    private static bool TryBoolean(JsonElement root, string property, out bool value)
    {
        value = false;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(property, out JsonElement item) ||
            item.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        value = item.GetBoolean();
        return true;
    }

    private static bool IsValidTrDizinResponse(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty("orcid", out JsonElement orcid) && orcid.ValueKind == JsonValueKind.String &&
        orcid.GetString() == "0000-0001-8560-7482" &&
        root.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.Number &&
        id.TryGetInt64(out long authorId) && authorId > 0;

    private static bool IsValidUnpaywallResponse(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty("doi", out JsonElement doi) && doi.ValueKind == JsonValueKind.String &&
        string.Equals(doi.GetString(), "10.1038/nphys1170", StringComparison.OrdinalIgnoreCase) &&
        root.TryGetProperty("is_oa", out JsonElement isOpenAccess) &&
        isOpenAccess.ValueKind is JsonValueKind.True or JsonValueKind.False;

    private static bool IsValidCrossrefResponse(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty("status", out JsonElement status) && status.ValueKind == JsonValueKind.String &&
        status.GetString() == "ok" &&
        root.TryGetProperty("message", out JsonElement message) && message.ValueKind == JsonValueKind.Object &&
        message.TryGetProperty("DOI", out JsonElement doi) && doi.ValueKind == JsonValueKind.String &&
        string.Equals(doi.GetString(), "10.1038/nphys1170", StringComparison.OrdinalIgnoreCase);

    private static bool IsValidSemanticScholarResponse(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty("paperId", out JsonElement paperId) && paperId.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(paperId.GetString());

    private static bool IsValidEmail(string? value)
    {
        string email = value?.Trim() ?? string.Empty;
        return email.Length is > 3 and <= 254 && email.Contains('@') && !email.Any(char.IsWhiteSpace);
    }

    private static string ParseGeminiHealth(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("provider", out JsonElement provider) || provider.ValueKind != JsonValueKind.String ||
            provider.GetString() != "Gemini" ||
            !root.TryGetProperty("health", out JsonElement health) || health.ValueKind != JsonValueKind.String ||
            !root.TryGetProperty("quotas", out JsonElement quotas) || quotas.ValueKind != JsonValueKind.Array ||
            quotas.GetArrayLength() != 0)
            return "UnexpectedResponse";
        return health.GetString() switch
        {
            "Healthy" => "Healthy",
            "Disabled" => "Disabled",
            "NotConfigured" => "NotConfigured",
            "Unauthorized" => "Unauthorized",
            "RateLimited" => "RateLimited",
            "Unavailable" or "Timeout" => "Unavailable",
            "UnexpectedResponse" => "UnexpectedResponse",
            _ => "UnexpectedResponse"
        };
    }

    public static List<ProviderQuotaDto> ParseSearchApiQuotas(JsonElement root, DateTime? observedAt = null)
    {
        List<ProviderQuotaDto> quotas = [];
        if (root.ValueKind != JsonValueKind.Object) return quotas;
        DateTime? periodEnd = root.TryGetProperty("subscription", out JsonElement subscription) &&
            subscription.ValueKind == JsonValueKind.Object && subscription.TryGetProperty("period_end", out JsonElement period) &&
            period.ValueKind == JsonValueKind.String && DateTime.TryParse(period.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime parsed) ? parsed : null;
        if (root.TryGetProperty("account", out JsonElement account) && account.ValueKind == JsonValueKind.Object)
        {
            decimal? limit = Number(account, "monthly_allowance"), used = Number(account, "current_month_usage"),
                remaining = Number(account, "remaining_credits");
            if (limit.HasValue || used.HasValue || remaining.HasValue)
                quotas.Add(new() { Source = "AccountApi", Window = "month", Unit = "searches", Limit = limit,
                    Used = used, Remaining = remaining, Scope = "account", ValueKind = "ProviderReported",
                    SourceFields = "account.monthly_allowance,current_month_usage,remaining_credits",
                    ObservedAt = observedAt, SubscriptionPeriodEndsAt = periodEnd });
        }
        if (root.TryGetProperty("api_usage", out JsonElement usage) && usage.ValueKind == JsonValueKind.Object)
        {
            decimal? limit = Number(usage, "hourly_rate_limit"), used = Number(usage, "searches_this_hour");
            if (limit.HasValue || used.HasValue)
                quotas.Add(new() { Source = "AccountApi", Window = "hour", Unit = "searches", Limit = limit,
                    Used = used, Remaining = limit.HasValue && used.HasValue ? Math.Max(0, limit.Value - used.Value) : null,
                    Scope = "account", ValueKind = limit.HasValue && used.HasValue ? "DerivedFromProviderValues" : "ProviderReported",
                    SourceFields = "api_usage.hourly_rate_limit,searches_this_hour", ObservedAt = observedAt,
                    SubscriptionPeriodEndsAt = periodEnd });
        }
        return quotas;
    }

    public static List<ProviderQuotaDto> ParseOpenAlexQuotas(JsonElement root, DateTime? observedAt = null)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("rate_limit", out JsonElement rateLimit) ||
            rateLimit.ValueKind != JsonValueKind.Object) return [];
        decimal? limit = Number(rateLimit, "credits_limit"), used = Number(rateLimit, "credits_used"),
            remaining = Number(rateLimit, "credits_remaining");
        DateTime? resetsAt = rateLimit.TryGetProperty("resets_at", out JsonElement reset) &&
            reset.ValueKind == JsonValueKind.String && DateTime.TryParse(reset.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime parsed) ? parsed : null;
        if (!limit.HasValue && !used.HasValue && !remaining.HasValue) return [];
        return [new() { Source = "AccountApi", Window = "day", Unit = "credits", Limit = limit, Used = used,
            Remaining = remaining, Scope = "api-key", ValueKind = "ProviderReported",
            SourceFields = "rate_limit.credits_limit,credits_used,credits_remaining,resets_at",
            ObservedAt = observedAt, ResetsAt = resetsAt }];
    }

    private static decimal? Number(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetDecimal(out decimal number) && number >= 0 ? number : null;

    public static List<ProviderQuotaDto> ParseHeaderQuotas(HttpResponseMessage response, DateTime? observedAt = null)
    {
        List<ProviderQuotaDto> quotas = [];
        foreach (string suffix in new[] { "", "-Day", "-Second" })
        {
            decimal? limit = Header(response, "X-RateLimit-Limit" + suffix);
            decimal? remaining = Header(response, "X-RateLimit-Remaining" + suffix);
            if (limit.HasValue || remaining.HasValue)
                quotas.Add(new() { Source = "ResponseHeaders", Window = suffix == "" ? "unspecified" : suffix[1..].ToLowerInvariant(),
                    Unit = "unknown", Limit = limit, Remaining = remaining, ValueKind = "ProviderReported",
                    SourceFields = "X-RateLimit-Limit" + suffix + ",X-RateLimit-Remaining" + suffix,
                    ObservedAt = observedAt });
        }
        return quotas;
    }

    public static List<ProviderQuotaDto> ParseCrossrefQuotas(HttpResponseMessage response,
        DateTime? observedAt = null)
    {
        decimal? limit = Header(response, "X-Rate-Limit-Limit");
        if (!limit.HasValue || !response.Headers.TryGetValues("X-Rate-Limit-Interval", out var values) ||
            !TryParseCrossrefInterval(values.FirstOrDefault(), out string window))
            return [];
        return [new()
        {
            Source = "ResponseHeaders", Window = window, Unit = "requests", Limit = limit,
            Remaining = null, Scope = "request-pool", ValueKind = "ProviderReported",
            SourceFields = "X-Rate-Limit-Limit,X-Rate-Limit-Interval", ObservedAt = observedAt
        }];
    }

    private static bool TryParseCrossrefInterval(string? value, out string window)
    {
        window = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || value.Length < 2 ||
            !int.TryParse(value[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count != 1)
            return false;
        window = char.ToLowerInvariant(value[^1]) switch
        {
            's' => "second",
            'm' => "minute",
            'h' => "hour",
            'd' => "day",
            _ => string.Empty
        };
        return window.Length > 0;
    }

    private static decimal? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) && decimal.TryParse(values.FirstOrDefault(),
            NumberStyles.Number, CultureInfo.InvariantCulture, out decimal number) && number >= 0 ? number : null;

    public static DateTime? ParseOpenAlexResetAt(HttpResponseMessage response, DateTime observedAt)
    {
        decimal? seconds = Header(response, "X-RateLimit-Reset");
        if (!seconds.HasValue) return null;
        try { return observedAt.AddSeconds((double)seconds.Value); }
        catch (ArgumentOutOfRangeException) { return null; }
        catch (OverflowException) { return null; }
    }

    public static ProviderRemainingUsageDto BuildRemainingUsage(ProviderStatusDto provider, DateTime now)
    {
        List<ProviderRemainingUsageItemDto> items = provider.ProviderQuotas.Select(quota =>
            BuildRemainingUsageItem(provider.Provider, quota, now)).ToList();
        if (provider.Status is "NotConfigured" or "Unavailable" or "Timeout" or "Unauthorized" or
            "LocallyLimited" or "LocalBudgetUnavailable" or "LocalCoordinationUnavailable" or
            "LocalCoordinationPending")
        {
            foreach (ProviderRemainingUsageItemDto item in items)
            {
                item.Status = "Unavailable";
                item.Value = null;
                item.Reason = "The provider check did not produce a usable current observation.";
            }
            return new() { Status = "Unavailable", Reason = "A current provider remaining balance could not be obtained.", Items = items };
        }
        if (items.Any(item => item.Status == "ProviderReported"))
            return new() { Status = "ProviderReported", Reason = "At least one current usage window was reported by the provider; inspect each item.", Items = items };
        if (items.Any(item => item.Status == "Derived"))
            return new() { Status = "Derived", Reason = "At least one current usage window was calculated from provider-reported values; inspect each item.", Items = items };
        if (items.Any(item => item.Status == "Stale"))
            return new() { Status = "Stale", Reason = "The provider observation is no longer current.", Items = items };
        return new() { Items = items };
    }

    private static ProviderRemainingUsageItemDto BuildRemainingUsageItem(string providerName,
        ProviderQuotaDto quota, DateTime now)
    {
        ProviderRemainingUsageItemDto item = new()
        {
            Unit = quota.Unit, Window = quota.Window, Source = quota.Source, Scope = quota.Scope,
            ObservedAt = quota.ObservedAt, ExpiresAt = quota.ExpiresAt, ResetsAt = quota.ResetsAt
        };
        if (!quota.ObservedAt.HasValue || !quota.ExpiresAt.HasValue || quota.ObservedAt > now)
        {
            item.Reason = "The observation does not have a valid current time range.";
            return item;
        }
        bool stale = quota.ExpiresAt <= now || quota.ResetsAt.HasValue && quota.ResetsAt <= now;
        if (stale)
        {
            item.Status = "Stale";
            item.Reason = "The observation expired or its quota reset time elapsed.";
            return item;
        }
        bool recognizedAnonymousScope = providerName == "OpenAlex" && quota.Scope == "anonymous";
        if (quota.Scope is not ("account" or "api-key") && !recognizedAnonymousScope)
        {
            item.Reason = "The observation is not verified for an account or API key.";
            return item;
        }
        bool standardOpenAlexHeaders = quota.SourceFields is
            "X-RateLimit-Limit,X-RateLimit-Remaining" or
            "X-RateLimit-Limit,X-RateLimit-Remaining,X-RateLimit-Reset";
        bool documentedWosHeaders = quota.SourceFields is
            "X-RateLimit-Limit-Day,X-RateLimit-Remaining-Day" or
            "X-RateLimit-Limit-Second,X-RateLimit-Remaining-Second";
        bool recognizedSource = quota.Source == "AccountApi" ||
            (providerName == "OpenAlex" && quota.Source == "ResponseHeaders" &&
                quota.Scope is "api-key" or "anonymous" && standardOpenAlexHeaders) ||
            (providerName == "WebOfScience" && quota.Source == "ResponseHeaders" &&
                quota.Scope == "api-key" && documentedWosHeaders);
        if (!recognizedSource || quota.ValueKind is not ("ProviderReported" or "DerivedFromProviderValues"))
        {
            item.Reason = "The remaining value does not have recognized provider provenance.";
            return item;
        }
        if (string.IsNullOrWhiteSpace(quota.Unit) || quota.Unit == "unknown" ||
            string.IsNullOrWhiteSpace(quota.Window) || quota.Window == "unspecified")
        {
            item.Reason = "The provider did not establish the usage unit and window.";
            return item;
        }
        if (!quota.Remaining.HasValue)
        {
            item.Reason = "The provider observation did not include enough data for remaining usage.";
            return item;
        }
        item.Value = quota.Remaining;
        item.Status = quota.ValueKind == "DerivedFromProviderValues" ? "Derived" : "ProviderReported";
        item.Reason = item.Status == "Derived"
            ? "Calculated from provider-reported limit and usage values."
            : "Reported directly by the provider.";
        return item;
    }

    internal static void RefreshRemainingUsage(ProviderStatusResponse response, DateTime now)
    {
        foreach (ProviderStatusDto provider in response.Providers)
            provider.RemainingUsage = BuildRemainingUsage(provider, now);
    }

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
