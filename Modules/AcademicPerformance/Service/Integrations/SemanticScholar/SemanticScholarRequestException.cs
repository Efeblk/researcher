using System.Net;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.SemanticScholar;

public sealed class SemanticScholarRequestException : HttpRequestException
{
    public string ErrorCode { get; }
    public int? ProviderHttpStatusCode { get; }
    public DateTime? RetryAt { get; }
    public bool Retryable { get; }

    private SemanticScholarRequestException(string errorCode, string message,
        int? providerHttpStatusCode, DateTime? retryAt, bool retryable, Exception? innerException = null)
        : base(message, innerException, providerHttpStatusCode is int statusCode ? (HttpStatusCode)statusCode : null)
    {
        ErrorCode = errorCode;
        ProviderHttpStatusCode = providerHttpStatusCode;
        RetryAt = retryAt;
        Retryable = retryable;
    }

    public static SemanticScholarRequestException FromResponse(HttpResponseMessage response)
    {
        bool localDeferral = response.Headers.Contains("X-Academic-Local-Deferral");
        bool disabled = response.Headers.Contains("X-Academic-Provider-Disabled");
        DateTime? retryAt = RetryAtFrom(response);
        if (localDeferral)
            return new("LocallyLimited", "Semantic Scholar collection was deferred by the local request limit.",
                null, retryAt, true);
        if (disabled)
            return new("Disabled", "Semantic Scholar collection is disabled.", null, null, false);

        int statusCode = (int)response.StatusCode;
        return response.StatusCode switch
        {
            HttpStatusCode.TooManyRequests => new("RateLimited", "Semantic Scholar is rate limiting requests.",
                statusCode, retryAt, true),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new("Unauthorized", "Semantic Scholar authentication or access was rejected.",
                    statusCode, null, false),
            HttpStatusCode.RequestTimeout => new("Timeout", "Semantic Scholar timed out.",
                statusCode, retryAt, true),
            _ when statusCode >= 500 => new("Unavailable", "Semantic Scholar is temporarily unavailable.",
                statusCode, retryAt, true),
            _ => new("InvalidProviderResponse", "Semantic Scholar returned an invalid response.",
                statusCode, null, false)
        };
    }

    public static SemanticScholarRequestException Transport(Exception innerException) =>
        new("TransportError", "Semantic Scholar could not be reached.", null, null, true, innerException);

    public static SemanticScholarRequestException Timeout(Exception innerException) =>
        new("Timeout", "Semantic Scholar timed out.", null, null, true, innerException);

    public static SemanticScholarRequestException InvalidProviderResponse(
        HttpStatusCode statusCode, Exception innerException) =>
        new("InvalidProviderResponse", "Semantic Scholar returned an invalid response.",
            (int)statusCode, null, false, innerException);

    private static DateTime? RetryAtFrom(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Date is { } date)
            return date.UtcDateTime;
        if (response.Headers.RetryAfter?.Delta is { } delta)
            return DateTime.UtcNow.Add(delta);
        return null;
    }
}
