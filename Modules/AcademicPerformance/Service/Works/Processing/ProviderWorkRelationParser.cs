using System.Text.Json;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;

internal enum ProviderWorkRelationKind
{
    VersionEquivalent,
    IsVersionOf,
    HasVersion
}

internal sealed record ProviderWorkRelation(
    string SourceDoi, string TargetDoi, ProviderWorkRelationKind Kind);

internal static class ProviderWorkRelationParser
{
    public static IReadOnlyList<ProviderWorkRelation> Parse(AcademicWork work)
    {
        string? workDoi = AcademicDoiNormalizer.NormalizeValid(work.Doi);
        if (workDoi is null || string.IsNullOrWhiteSpace(work.ProviderPayload))
            return [];
        try
        {
            using JsonDocument document = JsonDocument.Parse(work.ProviderPayload);
            return work.Provider switch
            {
                AcademicWorkProvider.Orcid => ParseOrcid(document.RootElement, workDoi),
                AcademicWorkProvider.Crossref => ParseCrossref(document.RootElement, workDoi),
                _ => []
            };
        }
        catch (JsonException)
        {
            return [];
        }
    }

    internal static string? FindOrcidSelfDoi(JsonElement work)
    {
        JsonElement externalIds = Property(Property(work, "external-ids"), "external-id");
        if (externalIds.ValueKind != JsonValueKind.Array)
            return null;
        List<(string Doi, string? Relationship)> dois = externalIds.EnumerateArray()
            .Where(item => string.Equals(Text(item, "external-id-type"), "doi",
                StringComparison.OrdinalIgnoreCase))
            .Select(item => (AcademicDoiNormalizer.NormalizeValid(
                    Text(Property(item, "external-id-normalized"), "value") ??
                    Text(item, "external-id-value")),
                NormalizeRelationship(Text(item, "external-id-relationship"))))
            .Where(item => item.Item1 is not null)
            .Select(item => (item.Item1!, item.Item2)).ToList();
        string[] explicitSelf = dois.Where(item => item.Relationship == "self")
            .Select(item => item.Doi).Distinct(StringComparer.Ordinal).ToArray();
        if (explicitSelf.Length == 1)
            return explicitSelf[0];
        if (explicitSelf.Length > 1)
            return null;
        string[] unqualified = dois.Where(item => item.Relationship is null)
            .Select(item => item.Doi).Distinct(StringComparer.Ordinal).ToArray();
        return dois.Count == 1 && unqualified.Length == 1 ? unqualified[0] : null;
    }

    private static IReadOnlyList<ProviderWorkRelation> ParseOrcid(JsonElement root, string workDoi)
    {
        if (!string.Equals(FindOrcidSelfDoi(root), workDoi, StringComparison.Ordinal))
            return [];
        JsonElement values = Property(Property(root, "external-ids"), "external-id");
        return values.EnumerateArray()
            .Where(item => string.Equals(Text(item, "external-id-type"), "doi",
                StringComparison.OrdinalIgnoreCase) &&
                NormalizeRelationship(Text(item, "external-id-relationship")) == "version-of")
            .Select(item => AcademicDoiNormalizer.NormalizeValid(
                Text(Property(item, "external-id-normalized"), "value") ??
                Text(item, "external-id-value")))
            .Where(doi => doi is not null && doi != workDoi)
            .Select(doi => new ProviderWorkRelation(
                workDoi, doi!, ProviderWorkRelationKind.VersionEquivalent))
            .Distinct().ToList();
    }

    private static IReadOnlyList<ProviderWorkRelation> ParseCrossref(JsonElement root, string workDoi)
    {
        JsonElement message = Property(root, "message");
        if (!string.Equals(AcademicDoiNormalizer.NormalizeValid(Text(message, "DOI")),
                workDoi, StringComparison.Ordinal))
            return [];
        JsonElement relations = Property(message, "relation");
        if (relations.ValueKind != JsonValueKind.Object)
            return [];
        List<ProviderWorkRelation> result = [];
        AddCrossref(relations, "is-version-of", ProviderWorkRelationKind.IsVersionOf, workDoi, result);
        AddCrossref(relations, "has-version", ProviderWorkRelationKind.HasVersion, workDoi, result);
        return result.Distinct().ToList();
    }

    private static void AddCrossref(JsonElement relations, string name, ProviderWorkRelationKind kind,
        string workDoi, List<ProviderWorkRelation> target)
    {
        JsonElement values = Property(relations, name);
        if (values.ValueKind != JsonValueKind.Array)
            return;
        foreach (JsonElement value in values.EnumerateArray())
        {
            string? doi = AcademicDoiNormalizer.NormalizeValid(Text(value, "id"));
            string? type = Text(value, "id-type");
            if (doi is not null && doi != workDoi &&
                type?.Equals("doi", StringComparison.OrdinalIgnoreCase) == true)
                target.Add(new(workDoi, doi, kind));
        }
    }

    private static string? NormalizeRelationship(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant().Replace('_', '-');
    private static string? Text(JsonElement value, string name) =>
        Property(value, name).ValueKind == JsonValueKind.String ? Property(value, name).GetString() : null;
    private static JsonElement Property(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out JsonElement property)
            ? property : default;
}
