using System.Text.Json;
using System.Text.Json.Nodes;

namespace FullTextResumePilot;

public static class PilotReportComparer
{
    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions CanonicalOptions = new(JsonSerializerDefaults.Web);

    public static bool MatchesResponse<T>(string responseBody, JsonNode? storedReport)
    {
        if (storedReport is null) return false;
        try
        {
            JsonNode? body = JsonNode.Parse(responseBody);
            JsonNode? responseReport = body?["Report"] ?? body?["report"];
            return Equivalent<T>(responseReport, storedReport);
        }
        catch (JsonException) { return false; }
    }

    public static bool Equivalent<T>(JsonNode? left, JsonNode? right)
    {
        if (left is null || right is null) return false;
        try
        {
            T? leftValue = left.Deserialize<T>(ReadOptions);
            T? rightValue = right.Deserialize<T>(ReadOptions);
            if (leftValue is null || rightValue is null) return false;
            return JsonNode.DeepEquals(JsonSerializer.SerializeToNode(leftValue, CanonicalOptions),
                JsonSerializer.SerializeToNode(rightValue, CanonicalOptions));
        }
        catch (JsonException) { return false; }
    }
}
