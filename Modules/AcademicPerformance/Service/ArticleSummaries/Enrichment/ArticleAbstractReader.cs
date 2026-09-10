using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries.Enrichment;

public static partial class ArticleAbstractReader
{
    public const int MaximumAbstractCharacters = 24_000;

    public static string? FromPayload(string? payload, string provider)
    {
        if (string.IsNullOrWhiteSpace(payload) || payload.Length > 4 * 1024 * 1024)
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(payload, new JsonDocumentOptions
            {
                MaxDepth = 64
            });

            return provider.StartsWith("OpenAlex", StringComparison.OrdinalIgnoreCase)
                ? FromOpenAlex(document.RootElement)
                : provider.StartsWith("Crossref", StringComparison.OrdinalIgnoreCase)
                    ? FromCrossref(document.RootElement)
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? FromOpenAlex(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("results", out JsonElement results) &&
            results.ValueKind == JsonValueKind.Array)
        {
            root = results.GetArrayLength() == 1 ? results[0] : default;
        }

        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("abstract_inverted_index", out JsonElement index) ||
            index.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        Dictionary<int, string> positions = [];
        int maximumPosition = -1;

        foreach (JsonProperty token in index.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(token.Name) || token.Name.Length > MaximumAbstractCharacters ||
                token.Value.ValueKind != JsonValueKind.Array || token.Value.GetArrayLength() == 0)
            {
                return null;
            }

            foreach (JsonElement positionElement in token.Value.EnumerateArray())
            {
                if (positionElement.ValueKind != JsonValueKind.Number ||
                    !positionElement.TryGetInt32(out int position) || position < 0 ||
                    position >= MaximumAbstractCharacters || !positions.TryAdd(position, token.Name))
                {
                    return null;
                }

                maximumPosition = Math.Max(maximumPosition, position);
            }
        }

        if (maximumPosition < 0 || positions.Count != maximumPosition + 1)
        {
            return null;
        }

        StringBuilder result = new(Math.Min(MaximumAbstractCharacters, positions.Count * 8));
        for (int position = 0; position <= maximumPosition; position++)
        {
            if (!positions.TryGetValue(position, out string? token))
            {
                return null;
            }

            int required = token.Length + (result.Length == 0 ? 0 : 1);
            if (result.Length + required > MaximumAbstractCharacters)
            {
                return null;
            }

            if (result.Length > 0)
            {
                result.Append(' ');
            }

            result.Append(token);
        }

        return result.Length == 0 ? null : result.ToString();
    }

    private static string? FromCrossref(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("message", out JsonElement message) &&
            message.ValueKind == JsonValueKind.Object)
        {
            root = message;
        }

        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("abstract", out JsonElement abstractElement) ||
            abstractElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return CleanMarkup(abstractElement.GetString());
    }

    private static string? CleanMarkup(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            DangerousXmlRegex().IsMatch(value))
        {
            return null;
        }

        string cleaned = ScriptAndStyleRegex().Replace(value, " ");
        cleaned = CommentRegex().Replace(cleaned, " ");
        cleaned = BreakRegex().Replace(cleaned, "\n");
        cleaned = ParagraphEndRegex().Replace(cleaned, "\n\n");
        cleaned = TagRegex().Replace(cleaned, string.Empty);
        cleaned = WebUtility.HtmlDecode(cleaned);

        List<string> paragraphs = [];
        foreach (string paragraph in NewlineRegex().Split(cleaned))
        {
            string normalized = WhitespaceRegex().Replace(paragraph, " ").Trim();
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                paragraphs.Add(normalized);
            }
        }

        string result = string.Join("\n\n", paragraphs);
        return result.Length is > 0 and <= MaximumAbstractCharacters ? result : null;
    }

    [GeneratedRegex(@"<!\s*(?:DOCTYPE|ENTITY)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DangerousXmlRegex();

    [GeneratedRegex(@"<(?:script|style)\b[^>]*>.*?</(?:script|style)\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ScriptAndStyleRegex();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex CommentRegex();

    [GeneratedRegex(@"<\s*(?:\w+:)?br\s*/?\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BreakRegex();

    [GeneratedRegex(@"</\s*(?:\w+:)?(?:p|div|section|sec|title|abstract|li|h[1-6])\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ParagraphEndRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\r\n?|\n+")]
    private static partial Regex NewlineRegex();

    [GeneratedRegex(@"[\p{Z}\t\f\v ]+")]
    private static partial Regex WhitespaceRegex();
}
