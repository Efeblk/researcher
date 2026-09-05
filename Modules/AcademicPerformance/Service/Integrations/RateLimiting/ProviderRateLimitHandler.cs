using System.Net;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using Microsoft.Data.SqlClient;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.RateLimiting;

// Applied at HTTP level so profile, detail, and pagination calls all consume budget.
// SQL coordinates pacing, cooldowns, and UTC daily budgets across application hosts.
public sealed class ProviderRateLimitHandler(
    string connectionString, IReadOnlyList<ProviderRequestPolicy> policies) : DelegatingHandler
{
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

        HttpResponseMessage? response = null;
        try
        {
            await using SqlApplicationLock? gate = await SqlApplicationLock.TryAcquireAsync(
                connectionString, "AcademicCollector.Provider." + policy.Name, 30000, cancellationToken);
            if (gate is null)
                return Deferred(policy.Name, DateTime.UtcNow.AddSeconds(30));

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
                return Deferred(policy.Name, now.Date.AddDays(1));
            // Long cooldowns go back to the durable queue instead of occupying a worker.
            if (nextAllowed - now > TimeSpan.FromSeconds(10))
                return Deferred(policy.Name, nextAllowed);
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

            response = await base.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                bool retryable = response.StatusCode is HttpStatusCode.TooManyRequests or
                    HttpStatusCode.RequestTimeout || (int)response.StatusCode >= 500;
                DateTime? retryAt = null;
                if (retryable)
                {
                    retryAt = GetRetryAt(response, DateTime.UtcNow);
                    await using SqlCommand cooldown = gate.Connection.CreateCommand();
                    cooldown.CommandText = """
                        UPDATE ProviderRequestBudgets SET NextAllowedAt =
                            CASE WHEN NextAllowedAt > @retry THEN NextAllowedAt ELSE @retry END
                        WHERE Provider = @provider;
                        """;
                    cooldown.Parameters.AddWithValue("@provider", policy.Name);
                    cooldown.Parameters.AddWithValue("@retry", retryAt.Value);
                    await cooldown.ExecuteNonQueryAsync(cancellationToken);
                }
                ProviderCallScope.Record(policy.Name, retryable, retryAt);
            }
            return response;
        }
        catch
        {
            response?.Dispose();
            ProviderCallScope.Record(policy.Name, true, DateTime.UtcNow.AddMinutes(1));
            throw;
        }
    }

    public static DateTime GetRetryAt(HttpResponseMessage response, DateTime now)
    {
        DateTime retryAt = response.Headers.RetryAfter?.Date?.UtcDateTime
            ?? now.Add(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(1));
        return retryAt > now ? retryAt : now.AddSeconds(1);
    }

    private static HttpResponseMessage Deferred(string provider, DateTime retryAt)
    {
        ProviderCallScope.Record(provider, true, retryAt);
        HttpResponseMessage response = new(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("Provider request budget is temporarily unavailable.")
        };
        response.Headers.RetryAfter = new(new DateTimeOffset(retryAt, TimeSpan.Zero));
        return response;
    }
}
