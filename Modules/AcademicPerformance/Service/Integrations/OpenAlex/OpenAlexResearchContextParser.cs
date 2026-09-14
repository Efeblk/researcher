using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.OpenAlex;

public static class OpenAlexResearchContextParser
{
    public const string ParserVersion = "openalex-research-context-v1";
    private const int MaximumPayloadBytes = 4 * 1024 * 1024;
    private const int MaximumTopics = 10;
    private const decimal MaximumStoredFwci = 999999999999.999999m;

    public static AcademicWorkResearchContext Parse(AcademicWork work)
    {
        string payload = work.ProviderPayload ?? string.Empty;
        var result = new AcademicWorkResearchContext
        {
            AcademicWorkId = work.Id,
            Provider = "OpenAlex",
            SourceWorkId = Text(work.ProviderWorkId, 500),
            ParserVersion = ParserVersion,
            SourceSyncedAt = work.SyncedAt,
            ParseQuality = "Available",
            PrimaryTopicQuality = "Unknown"
        };
        if (payload.Length > MaximumPayloadBytes || Encoding.UTF8.GetByteCount(payload) > MaximumPayloadBytes)
        {
            result.PayloadFingerprint = "oversize-payload";
            return Invalid(result, "PayloadTooLarge", "Saved OpenAlex payload exceeds the 4 MB parser limit.");
        }
        result.PayloadFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(payload))
            return Invalid(result, "Missing", "No saved OpenAlex payload is available.");

        try
        {
            using JsonDocument document = JsonDocument.Parse(payload, new JsonDocumentOptions
            {
                MaxDepth = 32,
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Invalid(result, "Malformed", "Saved OpenAlex payload is not a JSON object.");

            string? rawPayloadWorkId = GetString(root, "id");
            string? payloadWorkId = Text(rawPayloadWorkId, 500);
            if (rawPayloadWorkId?.Trim().Length > 500)
                return Invalid(result, "Invalid", "Saved OpenAlex payload identity exceeds its storage limit.");
            if (payloadWorkId is not null && result.SourceWorkId is not null &&
                !NormalizeWorkId(payloadWorkId).Equals(
                    NormalizeWorkId(result.SourceWorkId), StringComparison.OrdinalIgnoreCase))
            {
                return Invalid(result, "Conflict",
                    "Saved OpenAlex payload identity does not match the owning AcademicWork provider identity.");
            }
            result.SourceWorkId ??= payloadWorkId;
            result.ProviderUpdatedAt = Date(root, "updated_date");
            result.SourcePublicationYear = Integer(root, "publication_year");
            string? rawType = GetString(root, "type");
            string? sourceType = GetString(GetObject(GetObject(root, "primary_location"), "source"), "type");
            result.RawType = Text(rawType, 100);
            result.PrimarySourceType = Text(sourceType, 100);
            if (rawType?.Trim().Length > 100 || sourceType?.Trim().Length > 100)
                return Invalid(result, "Invalid", "A saved OpenAlex type exceeds its storage limit.");

            (result.Fwci, string fwciQuality, string? fwciReason) = NonnegativeDecimal(root, "fwci");
            bool hasPercentile = root.TryGetProperty("citation_normalized_percentile",
                out JsonElement percentileValue);
            JsonElement percentile = hasPercentile && percentileValue.ValueKind == JsonValueKind.Object
                ? percentileValue : default;
            bool invalidPercentileObject = hasPercentile && percentileValue.ValueKind is not
                (JsonValueKind.Object or JsonValueKind.Null);
            (result.CitationNormalizedPercentile, string percentileQuality, string? percentileReason) =
                invalidPercentileObject
                    ? (null, "Invalid", "The saved percentile value is not an object.")
                    : Fraction(percentile, "value");
            (result.IsInTopOnePercent, string topOneQuality, string? topOneReason) =
                invalidPercentileObject
                    ? (null, "Invalid", "The saved percentile flags are not in an object.")
                    : Boolean(percentile, "is_in_top_1_percent");
            (result.IsInTopTenPercent, string topTenQuality, string? topTenReason) =
                invalidPercentileObject
                    ? (null, "Invalid", "The saved percentile flags are not in an object.")
                    : Boolean(percentile, "is_in_top_10_percent");

            List<AcademicWorkTopic> topics = ParseTopics(root);
            result.Topics = topics;
            JsonElement primaryTopic = GetObject(root, "primary_topic");
            string? primaryId = TopicId(primaryTopic);
            string? firstTopicId = RawFirstTopicId(root);
            if (primaryId is null)
            {
                result.PrimaryTopicQuality = "Unknown";
                result.PrimaryTopicQualityReason = "No saved primary topic is available.";
            }
            else if (firstTopicId is null || !primaryId.Equals(firstTopicId, StringComparison.Ordinal) ||
                PrimaryHierarchyConflicts(root, primaryTopic))
            {
                result.PrimaryTopicQuality = "Conflict";
                result.PrimaryTopicQualityReason =
                    "Saved primary_topic does not match the first ranked topics entry.";
            }
            else
            {
                AcademicWorkTopic? primary = topics.SingleOrDefault(topic => topic.OriginalRank == 1 &&
                    topic.TopicId == primaryId);
                if (primary is null)
                {
                    result.PrimaryTopicQuality = "Conflict";
                    result.PrimaryTopicQualityReason =
                        "The first ranked topic could not be stored as a valid topic identity.";
                }
                else
                {
                    primary.IsPrimary = true;
                    result.PrimaryTopicQuality = "Available";
                }
            }

            result.ValueQualityJson = JsonSerializer.Serialize(new
            {
                Fwci = Quality(fwciQuality, fwciReason),
                CitationNormalizedPercentile = Quality(percentileQuality, percentileReason),
                IsInTopOnePercent = Quality(topOneQuality, topOneReason),
                IsInTopTenPercent = Quality(topTenQuality, topTenReason)
            });
            return result;
        }
        catch (JsonException)
        {
            return Invalid(result, "Malformed", "Saved OpenAlex payload could not be parsed as bounded JSON.");
        }
    }

    private static List<AcademicWorkTopic> ParseTopics(JsonElement root)
    {
        if (!root.TryGetProperty("topics", out JsonElement values) ||
            values.ValueKind != JsonValueKind.Array)
            return [];
        List<AcademicWorkTopic> result = [];
        HashSet<string> ids = new(StringComparer.Ordinal);
        int rank = 0;
        foreach (JsonElement value in values.EnumerateArray().Take(MaximumTopics))
        {
            rank++;
            string? id = TopicId(value);
            if (id is null || !ids.Add(id))
                continue;
            (double? score, string scoreQuality, string? scoreReason) = Score(value, "score");
            JsonElement subfield = GetObject(value, "subfield");
            JsonElement field = GetObject(value, "field");
            JsonElement domain = GetObject(value, "domain");
            result.Add(new()
            {
                TopicId = id,
                TopicName = Text(GetString(value, "display_name"), 1000),
                SubfieldId = Identifier(subfield, "id", 200),
                SubfieldName = Text(GetString(subfield, "display_name"), 1000),
                FieldId = Identifier(field, "id", 200),
                FieldName = Text(GetString(field, "display_name"), 1000),
                DomainId = Identifier(domain, "id", 200),
                DomainName = Text(GetString(domain, "display_name"), 1000),
                OriginalRank = rank,
                AssignmentScore = score,
                ScoreQuality = scoreQuality,
                ScoreQualityReason = scoreReason
            });
        }
        return result;
    }

    private static AcademicWorkResearchContext Invalid(
        AcademicWorkResearchContext result, string quality, string reason)
    {
        result.ParseQuality = quality;
        result.ParseQualityReason = reason;
        result.PrimaryTopicQuality = "Unknown";
        result.PrimaryTopicQualityReason = "No parsed primary topic is available.";
        result.ValueQualityJson = JsonSerializer.Serialize(new
        {
            Fwci = Quality("Unknown", reason),
            CitationNormalizedPercentile = Quality("Unknown", reason),
            IsInTopOnePercent = Quality("Unknown", reason),
            IsInTopTenPercent = Quality("Unknown", reason)
        });
        return result;
    }

    private static (decimal?, string, string?) NonnegativeDecimal(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
            return (null, "Unknown", "No saved value is available.");
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out decimal number))
            return (null, "Invalid", "The saved value is not a representable number.");
        return number < 0
            ? (null, "Invalid", "The saved value is negative; it was normalized to null.")
            : number > MaximumStoredFwci
                ? (null, "Invalid", "The saved value exceeds the normalized SQL precision; it was normalized to null.")
            : (number, "Available", null);
    }

    private static (decimal?, string, string?) Fraction(JsonElement root, string name)
    {
        (decimal? value, string quality, string? reason) = NonnegativeDecimal(root, name);
        return value > 1
            ? (null, "Invalid", "The saved percentile fraction is outside 0 through 1; it was normalized to null.")
            : (value, quality, reason);
    }

    private static (bool?, string, string?) Boolean(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
            return (null, "Unknown", "No saved value is available.");
        return value.ValueKind switch
        {
            JsonValueKind.True => (true, "Available", null),
            JsonValueKind.False => (false, "Available", null),
            _ => (null, "Invalid", "The saved value is not a boolean; it was normalized to null.")
        };
    }

    private static (double?, string, string?) Score(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
            return (null, "Unknown", "No saved topic ranking score is available.");
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double number) ||
            double.IsNaN(number) || double.IsInfinity(number))
            return (null, "Invalid", "The saved topic ranking score is not a finite representable number.");
        return (number, "Available", null);
    }

    private static DateTime? Date(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String &&
        DateTime.TryParse(value.GetString(), null,
            System.Globalization.DateTimeStyles.AssumeUniversal |
            System.Globalization.DateTimeStyles.AdjustToUniversal, out DateTime parsed)
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
            : null;

    private static int? Integer(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out int parsed)
            ? parsed : null;

    private static JsonElement GetObject(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.Object ? value : default;

    private static string? GetString(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? RawFirstTopicId(JsonElement root)
    {
        if (!root.TryGetProperty("topics", out JsonElement topics) ||
            topics.ValueKind != JsonValueKind.Array || topics.GetArrayLength() == 0)
            return null;
        return TopicId(topics[0]);
    }

    private static bool PrimaryHierarchyConflicts(JsonElement root, JsonElement primaryTopic)
    {
        if (!root.TryGetProperty("topics", out JsonElement topics) ||
            topics.ValueKind != JsonValueKind.Array || topics.GetArrayLength() == 0)
            return false;
        JsonElement first = topics[0];
        foreach (string level in new[] { "subfield", "field", "domain" })
        {
            string? primaryId = Identifier(GetObject(primaryTopic, level), "id", 200);
            string? firstId = Identifier(GetObject(first, level), "id", 200);
            if (primaryId is not null && firstId is not null && primaryId != firstId)
                return true;
        }
        return false;
    }

    private static string? TopicId(JsonElement topic) => Identifier(topic, "id", 200);

    private static string? Identifier(JsonElement root, string name, int maximumLength)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(name, out JsonElement value))
            return null;
        string? text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt64(out long number) =>
                number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => null
        };
        return Text(text, maximumLength);
    }

    private static string? Text(string? value, int maximumLength)
    {
        string? trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > maximumLength ? null : trimmed;
    }

    private static string NormalizeWorkId(string value) =>
        value.Trim().TrimEnd('/').Split('/').Last();

    private static object Quality(string quality, string? reason) => new { Quality = quality, Reason = reason };
}
