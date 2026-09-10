using System.Net;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using Microsoft.Data.SqlClient;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.RateLimiting;

// Applied at HTTP level so profile, detail, and pagination calls all consume budget.
// SQL coordinates pacing, cooldowns, and UTC daily budgets across application hosts.
public sealed class ProviderRateLimitHandler(
    string connectionString, IReadOnlyList<ProviderRequestPolicy> policies,
    ILogger<ProviderRateLimitHandler>? logger = null) : DelegatingHandler
{
    public static readonly HttpRequestOptionsKey<long> ResponseBufferLimit =
        new("AcademicCollector.ProviderResponseBufferLimit");
    public static readonly HttpRequestOptionsKey<bool> ExpectedNotFound =
        new("AcademicCollector.ExpectedProviderNotFound");

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, ProviderCallScope.Cancellation);
        cancellationToken = linked.Token;
        ProviderRequestPolicy? policy = policies.FirstOrDefault(policy =>
            string.Equals(policy.Host, request.RequestUri?.Host, StringComparison.OrdinalIgnoreCase));
        if (policy is null)
            throw new InvalidOperationException("No request policy is configured for this provider host.");
        if (!policy.Enabled)
            return Disabled(policy.Name);

        HttpResponseMessage? response = null;
        DateTime? responseRetryAt = null;
        SqlApplicationLock? gate = null;
        bool dispatchStarted = false;
        try
        {
            gate = await SqlApplicationLock.TryAcquireAsync(
                connectionString, "AcademicCollector.Provider." + policy.Name, 30000, cancellationToken);
            if (gate is null)
            {
                LogDeferral(policy.Name, 30);
                return Deferred(policy.Name, DateTime.UtcNow.AddSeconds(30));
            }

            DateTime now = DateTime.UtcNow;
            await using SqlCommand budget = gate.Connection.CreateCommand();
            budget.CommandText = """
                IF NOT EXISTS (SELECT 1 FROM ProviderRequestBudgets WHERE Provider = @provider)
                    INSERT INTO ProviderRequestBudgets VALUES (@provider, @now, CAST(@now AS date), 0);
                SELECT NextAllowedAt, BudgetDate, RequestsToday
                FROM ProviderRequestBudgets WHERE Provider = @provider;
                """;
            budget.Parameters.AddWithValue("@provider", policy.Name);
            budget.Parameters.AddWithValue("@now", now);
            DateTime nextAllowed;
            DateTime budgetDate;
            int requestsToday;
            await using (SqlDataReader reader = await budget.ExecuteReaderAsync(cancellationToken))
            {
                await reader.ReadAsync(cancellationToken);
                nextAllowed = DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc);
                budgetDate = reader.GetDateTime(1);
                requestsToday = reader.GetInt32(2);
            }

            if (policy.DailyRequestLimit > 0 && budgetDate.Date == now.Date &&
                requestsToday >= policy.DailyRequestLimit)
            {
                LogDeferral(policy.Name, (int)Math.Ceiling((now.Date.AddDays(1) - now).TotalSeconds));
                return Deferred(policy.Name, now.Date.AddDays(1));
            }
            // Long cooldowns go back to the durable queue instead of occupying a worker.
            if (nextAllowed - now > TimeSpan.FromSeconds(10))
            {
                LogDeferral(policy.Name, (int)Math.Ceiling((nextAllowed - now).TotalSeconds));
                return Deferred(policy.Name, nextAllowed);
            }
            if (nextAllowed > now)
                await Task.Delay(nextAllowed - now, cancellationToken);

            now = DateTime.UtcNow;
            await using SqlCommand reserve = gate.Connection.CreateCommand();
            reserve.CommandText = """
                UPDATE ProviderRequestBudgets SET NextAllowedAt = @next,
                    RequestsToday = CASE WHEN BudgetDate = CAST(@now AS date)
                        THEN RequestsToday + 1 ELSE 1 END,
                    BudgetDate = CAST(@now AS date) WHERE Provider = @provider;
                """;
            reserve.Parameters.AddWithValue("@provider", policy.Name);
            reserve.Parameters.AddWithValue("@now", now);
            reserve.Parameters.AddWithValue("@next", now.AddMilliseconds(policy.MinimumIntervalMilliseconds));
            await reserve.ExecuteNonQueryAsync(cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            int requestOrdinal = ProviderCallScope.NextRequestOrdinal();
            if (requestOrdinal > 0)
                logger?.LogInformation("Bulk provider request {RequestOrdinal} to {Provider} started.",
                    requestOrdinal, policy.Name);
            dispatchStarted = true;
            response = await base.SendAsync(request, cancellationToken);
            bool retryableResponse = response.StatusCode is HttpStatusCode.TooManyRequests or
                HttpStatusCode.RequestTimeout || (int)response.StatusCode >= 500;
            if (retryableResponse)
            {
                responseRetryAt = GetRetryAt(response, DateTime.UtcNow);
                await PersistCooldownAsync(gate, policy.Name, responseRetryAt.Value, cancellationToken);
            }
            // HttpClient normally buffers content after delegating handlers return. Buffer here so a
            // provider that sends headers and then stalls or drops the body also receives a cooldown.
            if (request.Options.TryGetValue(ResponseBufferLimit, out long bufferLimit))
                await response.Content.LoadIntoBufferAsync(bufferLimit, cancellationToken);
            else
                await response.Content.LoadIntoBufferAsync(cancellationToken);
            if (requestOrdinal > 0)
                logger?.LogInformation("Bulk provider request {RequestOrdinal} to {Provider} returned HTTP {StatusCode}.",
                    requestOrdinal, policy.Name, (int)response.StatusCode);
            bool expectedNotFound = response.StatusCode == HttpStatusCode.NotFound &&
                request.Options.TryGetValue(ExpectedNotFound, out bool expected) && expected;
            if (!response.IsSuccessStatusCode && !expectedNotFound)
            {
                ProviderCallScope.Record(policy.Name, retryableResponse, responseRetryAt);
            }
            await PersistCooldownAsync(gate, policy.Name,
                DateTime.UtcNow.AddMilliseconds(policy.MinimumIntervalMilliseconds), cancellationToken);
            return response;
        }
        catch (Exception exception)
        {
            response?.Dispose();
            if (dispatchStarted)
            {
                DateTime retryAt = DateTime.UtcNow.AddMinutes(1);
                if (responseRetryAt is { } headerRetryAt && headerRetryAt > retryAt)
                    retryAt = headerRetryAt;
                ProviderCallScope.Record(policy.Name, true, retryAt);
                if (gate is not null)
                {
                    using CancellationTokenSource cleanup = new(TimeSpan.FromSeconds(5));
                    try
                    {
                        await PersistCooldownAsync(gate, policy.Name, retryAt, cleanup.Token);
                    }
                    catch (Exception cooldownException)
                    {
                        logger?.LogWarning(
                            "Could not persist the {Provider} transport-failure cooldown after {ErrorType}; cleanup failed with {CleanupErrorType}.",
                            policy.Name, exception.GetType().Name, cooldownException.GetType().Name);
                    }
                }
            }
            throw;
        }
        finally
        {
            if (gate is not null)
                await gate.DisposeAsync();
        }
    }

    private static async Task PersistCooldownAsync(SqlApplicationLock gate, string provider,
        DateTime retryAt, CancellationToken cancellationToken)
    {
        await using SqlCommand cooldown = gate.Connection.CreateCommand();
        cooldown.CommandText = """
            UPDATE ProviderRequestBudgets SET NextAllowedAt =
                CASE WHEN NextAllowedAt > @retry THEN NextAllowedAt ELSE @retry END
            WHERE Provider = @provider;
            """;
        cooldown.Parameters.AddWithValue("@provider", provider);
        cooldown.Parameters.AddWithValue("@retry", retryAt);
        await cooldown.ExecuteNonQueryAsync(cancellationToken);
    }

    public static DateTime GetRetryAt(HttpResponseMessage response, DateTime now)
    {
        DateTime retryAt = response.Headers.RetryAfter?.Date?.UtcDateTime
            ?? now.Add(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(1));
        return retryAt > now ? retryAt : now.AddSeconds(1);
    }

    private static HttpResponseMessage Deferred(string provider, DateTime retryAt)
    {
        ProviderCallScope.Record(provider, true, retryAt, true);
        HttpResponseMessage response = new(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("Provider request budget is temporarily unavailable.")
        };
        response.Headers.Add("X-Academic-Local-Deferral", "true");
        response.Headers.RetryAfter = new(new DateTimeOffset(retryAt, TimeSpan.Zero));
        return response;
    }

    private static HttpResponseMessage Disabled(string provider)
    {
        ProviderCallScope.Record(provider, false, isDisabled: true);
        HttpResponseMessage response = new(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("Provider requests are disabled by local configuration.")
        };
        response.Headers.Add("X-Academic-Provider-Disabled", "true");
        return response;
    }

    private void LogDeferral(string provider, int retrySeconds)
    {
        if (ProviderCallScope.IsActive)
            logger?.LogInformation("Bulk provider request to {Provider} deferred for {RetrySeconds} seconds.",
                provider, Math.Max(1, retrySeconds));
    }
}
