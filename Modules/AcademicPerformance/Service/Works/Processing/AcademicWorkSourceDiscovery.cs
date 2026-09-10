using System.Text.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;

public static class AcademicWorkSourceDiscovery
{
    private const int MaximumPayloadSources = 32;
    public static IReadOnlyList<AcademicWorkSource> FromPayload(string? payload, string provider)
    {
        if (string.IsNullOrWhiteSpace(payload)) return [];
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return [];
            return provider.StartsWith("OpenAlex", StringComparison.OrdinalIgnoreCase) ? FromOpenAlex(document.RootElement) :
                provider.StartsWith("Crossref", StringComparison.OrdinalIgnoreCase) ? FromCrossref(document.RootElement) : [];
        }
        catch (JsonException) { return []; }
    }
    public static AcademicWorkSource Create(string url, string kind, string origin, bool? isOpenAccess = null) =>
        new() { Url = url.Trim(), Kind = kind, Origin = origin, IsOpenAccess = isOpenAccess };
    public static bool LooksLikePdf(string value) => Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
        (uri.AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) || uri.AbsolutePath.Contains("/download/article-file/", StringComparison.OrdinalIgnoreCase));
    public static IReadOnlyList<AcademicWorkSource> Distinct(IEnumerable<AcademicWorkSource> sources)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        return sources.Where(source => !string.IsNullOrWhiteSpace(source.Url) && seen.Add($"{source.Origin}\n{source.Url.Trim()}"))
            .Take(MaximumPayloadSources).ToList();
    }
    private static IReadOnlyList<AcademicWorkSource> FromOpenAlex(JsonElement root)
    {
        List<AcademicWorkSource> result = [];
        AddLocation(root, "primary_location", "OpenAlex.Primary", null, result);
        AddLocation(root, "best_oa_location", "OpenAlex.BestOpenAccess", true, result);
        if (root.TryGetProperty("locations", out JsonElement locations) && locations.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (JsonElement location in locations.EnumerateArray().Take(MaximumPayloadSources))
                AddLocation(location, null, $"OpenAlex.Location[{index++}]", Bool(location, "is_oa"), result);
        }
        if (root.TryGetProperty("open_access", out JsonElement oa) && oa.ValueKind == JsonValueKind.Object)
            AddUrl(oa, "oa_url", "OpenAlex.OpenAccess", "Unknown", true, result);
        return Distinct(result);
    }
    private static IReadOnlyList<AcademicWorkSource> FromCrossref(JsonElement root)
    {
        if (root.TryGetProperty("message", out JsonElement message) && message.ValueKind == JsonValueKind.Object) root = message;
        List<AcademicWorkSource> result = [];
        if (root.TryGetProperty("link", out JsonElement links) && links.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (JsonElement link in links.EnumerateArray().Take(MaximumPayloadSources))
            {
                string? contentType = Text(link, "content-type");
                string? url = Text(link, "URL") ?? Text(link, "url");
                if (contentType?.Contains("pdf", StringComparison.OrdinalIgnoreCase) == true && Valid(url))
                    result.Add(Create(url!, "Pdf", $"Crossref.Link[{index}]"));
                index++;
            }
        }
        if (root.TryGetProperty("resource", out JsonElement resource) && resource.ValueKind == JsonValueKind.Object &&
            resource.TryGetProperty("primary", out JsonElement primary) && primary.ValueKind == JsonValueKind.Object)
            AddUrl(primary, "URL", "Crossref.ResourcePrimary", "Landing", null, result);
        return Distinct(result);
    }
    private static void AddLocation(JsonElement parent, string? property, string origin, bool? open, List<AcademicWorkSource> result)
    {
        JsonElement location = parent;
        if (property is not null && (!parent.TryGetProperty(property, out location) || location.ValueKind != JsonValueKind.Object)) return;
        AddUrl(location, "pdf_url", origin + ".Pdf", "Pdf", open, result);
        AddUrl(location, "landing_page_url", origin + ".Landing", "Landing", open, result);
    }
    private static void AddUrl(JsonElement parent, string property, string origin, string kind, bool? open, List<AcademicWorkSource> result)
    { string? url = Text(parent, property); if (Valid(url)) result.Add(Create(url!, kind, origin, open)); }
    private static string? Text(JsonElement value, string property) => value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(property, out JsonElement item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
    private static bool? Bool(JsonElement value, string property) => value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(property, out JsonElement item) && item.ValueKind is JsonValueKind.True or JsonValueKind.False ? item.GetBoolean() : null;
    private static bool Valid(string? value) => value?.Length <= 2000 && Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https";
}
