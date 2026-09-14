using System.Text.Json;

namespace ResearcherAnalysisService.SourceData.Works;

internal sealed record MetricQuality(string Quality, string? Reason);

internal static class ResearchContextQuality
{
    public static Dictionary<string, MetricQuality> Read(string json)
    {
        try
        {
            Dictionary<string, MetricQuality> values =
                JsonSerializer.Deserialize<Dictionary<string, MetricQuality>>(json,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
            return new Dictionary<string, MetricQuality>(values, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
