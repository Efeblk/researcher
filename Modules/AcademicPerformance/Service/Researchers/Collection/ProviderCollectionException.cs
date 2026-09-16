namespace AcademicCollectorDemo.Modules.AcademicPerformance.Researchers.Collection;

using System.Net;
using System.Text.Json;

public sealed class ProviderCollectionException(
    string code, string safeDescription, int retrievedCount, int? expectedCount,
    Exception innerException) : Exception(safeDescription, innerException)
{
    public string Code { get; } = code;
    public string SafeDescription { get; } = safeDescription;
    public int RetrievedCount { get; } = retrievedCount;
    public int? ExpectedCount { get; } = expectedCount;
    public string CauseCode { get; } = Classify(innerException).Code ?? code;
    public string CauseDescription { get; } = Classify(innerException).Description ?? safeDescription;

    public static (string? Code, string? Description) Classify(Exception exception)
    {
        for (Exception? cause = exception; cause is not null; cause = cause.InnerException)
        {
            if (cause is TaskCanceledException or TimeoutException) return ("Timeout", "Sağlayıcı isteği zaman aşımına uğradı.");
            if (cause is JsonException or InvalidDataException) return ("MalformedResponse", "Sağlayıcı geçerli ve eksiksiz bir yanıt döndürmedi.");
            if (cause is HttpRequestException http)
            {
                if (http.StatusCode == HttpStatusCode.TooManyRequests) return ("RateLimited", "Sağlayıcı hız veya kota sınırına ulaştı.");
                if (http.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) return ("AuthenticationOrConfiguration", "Sağlayıcı kimlik doğrulamasını veya yapılandırmayı kabul etmedi.");
                if (http.StatusCode == HttpStatusCode.NotFound) return ("NotFound", "Sağlayıcı kaydı bulunamadı.");
            }
        }
        return (null, null);
    }
}
