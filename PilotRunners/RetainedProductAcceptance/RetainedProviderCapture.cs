using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ServiceAcceptancePilot;

public sealed class RetainedProviderCapture(string directory)
{
    private const int MaximumBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public Task WriteRequestAsync(int ordinal, DateTime startedAt, HttpRequestMessage request, byte[] body,
        CancellationToken cancellationToken) => WriteNewAsync($"attempt-{ordinal:D2}-request.json", new()
    {
        ["ordinal"] = ordinal,
        ["startedAtUtc"] = startedAt,
        ["method"] = request.Method.Method,
        ["target"] = request.RequestUri is null ? null :
            $"{request.RequestUri.Scheme}://{request.RequestUri.Host}{request.RequestUri.AbsolutePath}",
        ["bodyBytes"] = body.Length,
        ["bodySha256"] = Hash(body),
        ["body"] = ParseJson(body)
    }, cancellationToken);

    public Task WriteResponseAsync(int ordinal, HttpResponseMessage response, byte[] body,
        CancellationToken cancellationToken) => WriteNewAsync($"attempt-{ordinal:D2}-response.json", new()
    {
        ["ordinal"] = ordinal,
        ["capturedAtUtc"] = DateTimeOffset.UtcNow,
        ["httpStatus"] = (int)response.StatusCode,
        ["bodyBytes"] = body.Length,
        ["bodySha256"] = Hash(body),
        ["body"] = ParseJson(body)
    }, cancellationToken);

    private async Task WriteNewAsync(string name, JsonObject value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, name);
        string temporary = target + ".pending-" + Guid.NewGuid().ToString("N");
        byte[] bytes = Encoding.UTF8.GetBytes(value.ToJsonString(JsonOptions) + Environment.NewLine);
        if (bytes.Length > MaximumBytes)
            throw new InvalidOperationException("The sanitized provider capture exceeds the 4 MB artifact limit.");
        await using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await stream.WriteAsync(bytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        File.Move(temporary, target, false);
    }

    private static JsonNode ParseJson(byte[] bytes)
    {
        if (bytes.Length > MaximumBytes) throw new InvalidOperationException("Provider payload exceeded 4 MB.");
        try { return JsonNode.Parse(bytes) ?? JsonValue.Create(string.Empty)!; }
        catch (JsonException) { return JsonValue.Create(Encoding.UTF8.GetString(bytes))!; }
    }

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
