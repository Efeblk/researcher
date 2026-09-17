using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Works.Processing;

internal static class CanonicalWorkIdentityResolver
{
    public static List<WorkIdentity> Resolve(List<AcademicWork> works)
    {
        if (works.Select(work => work.PersonelId).Distinct(StringComparer.Ordinal).Skip(1).Any())
            throw new ArgumentException("Canonical metadata identity resolution requires one PersonelID.", nameof(works));

        Dictionary<string, WorkIdentity> relationIdentities = ResolveProviderRelations(works);
        WorkMetadata?[] metadata = works.Select(CreateMetadata).ToArray();
        WorkIdentity?[] result = new WorkIdentity?[works.Count];
        for (int index = 0; index < works.Count; index++)
        {
            string? normalizedDoi = AcademicDoiNormalizer.NormalizeValid(works[index].Doi);
            if (normalizedDoi is not null)
                result[index] = relationIdentities.GetValueOrDefault(normalizedDoi) ??
                    CreateDoiIdentity(normalizedDoi);
        }

        IEnumerable<IGrouping<(string Title, int Year), int>> buckets = Enumerable.Range(0, works.Count)
            .Where(index => metadata[index] is not null)
            .GroupBy(index => (metadata[index]!.Title, metadata[index]!.Year));
        foreach (IGrouping<(string Title, int Year), int> bucket in buckets)
            ResolveBucket(works, metadata, result, bucket);

        for (int index = 0; index < works.Count; index++)
            result[index] ??= CreateSourceIdentity(works[index]);
        return result.Select(identity => identity!).ToList();
    }

    private static void ResolveBucket(List<AcademicWork> works, WorkMetadata?[] metadata,
        WorkIdentity?[] result, IGrouping<(string Title, int Year), int> bucket)
    {
        List<int> indexes = bucket.OrderBy(index => StableWorkOrder(works[index]), StringComparer.Ordinal).ToList();
        HashSet<int> unvisited = indexes.ToHashSet();
        while (unvisited.Count != 0)
        {
            int seed = indexes.First(unvisited.Contains);
            List<int> component = [];
            Queue<int> pending = new([seed]);
            unvisited.Remove(seed);
            while (pending.TryDequeue(out int current))
            {
                component.Add(current);
                foreach (int candidate in indexes.Where(unvisited.Contains).ToArray())
                {
                    if (!AuthorsCompatible(metadata[current]!, metadata[candidate]!))
                        continue;
                    unvisited.Remove(candidate);
                    pending.Enqueue(candidate);
                }
            }
            ResolveComponent(works, metadata, result, bucket.Key, component);
        }
    }

    private static void ResolveComponent(List<AcademicWork> works, WorkMetadata?[] metadata,
        WorkIdentity?[] result, (string Title, int Year) bucketKey, List<int> component)
    {
        List<int> withoutDoi = component.Where(index => result[index] is null).ToList();
        if (withoutDoi.Count == 0)
            return;
        WorkIdentity[] identities = component.Select(index => result[index])
            .Where(value => value is not null).Select(value => value!)
            .DistinctBy(value => value.DictionaryKey).ToArray();
        if (!IsClique(component, metadata) || identities.Length > 1)
            return;
        if (identities.Length == 1)
        {
            foreach (int index in withoutDoi)
                result[index] = identities[0];
            return;
        }
        if (withoutDoi.Count == 1)
            return;

        WorkMetadata representative = withoutDoi.Select(index => metadata[index]!)
            .OrderByDescending(value => value.AuthorInformation)
            .ThenBy(value => value.AuthorKey, StringComparer.Ordinal).First();
        string sourceScopedKey = Hash($"metadata|{works[withoutDoi[0]].PersonelId.Trim()}|" +
            $"{bucketKey.Title}|{bucketKey.Year}|{representative.AuthorKey}");
        WorkIdentity identity = CreateSourceIdentity(sourceScopedKey);
        foreach (int index in withoutDoi)
            result[index] = identity;
    }

    private static bool IsClique(List<int> indexes, WorkMetadata?[] metadata)
    {
        for (int left = 0; left < indexes.Count; left++)
            for (int right = left + 1; right < indexes.Count; right++)
                if (!AuthorsCompatible(metadata[indexes[left]]!, metadata[indexes[right]]!))
                    return false;
        return true;
    }

    private static WorkMetadata? CreateMetadata(AcademicWork work)
    {
        string? title = AcademicWorkTitleNormalizer.Normalize(work.Title);
        if (title is null || work.PublicationYear is null || string.IsNullOrWhiteSpace(work.Authors) ||
            work.Authors.Contains('…') || work.Authors.Contains("...", StringComparison.Ordinal))
            return null;
        string[][] authors = work.Authors.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Select(NormalizeTokens).Where(tokens => tokens.Length != 0)
            .OrderBy(CanonicalNameKey, StringComparer.Ordinal).ToArray();
        if (authors.Length == 0 || authors.Any(tokens => tokens.Length < 2 ||
                tokens.All(token => token.Length == 1) || IsPlaceholder(tokens)) ||
            authors.SelectMany(tokens => tokens).Any(token => token is "et" or "al"))
            return null;
        return new(title, work.PublicationYear.Value, authors);
    }

    private static bool AuthorsCompatible(WorkMetadata left, WorkMetadata right)
    {
        if (left.Authors.Length != right.Authors.Length)
            return false;
        int[] matchedLeftByRight = Enumerable.Repeat(-1, right.Authors.Length).ToArray();
        for (int leftIndex = 0; leftIndex < left.Authors.Length; leftIndex++)
        {
            bool[] visitedRight = new bool[right.Authors.Length];
            if (!TryMatchAuthor(left.Authors, right.Authors, leftIndex, matchedLeftByRight, visitedRight))
                return false;
        }
        return true;
    }

    private static bool TryMatchAuthor(string[][] left, string[][] right, int leftIndex,
        int[] matchedLeftByRight, bool[] visitedRight)
    {
        for (int rightIndex = 0; rightIndex < right.Length; rightIndex++)
        {
            if (visitedRight[rightIndex] || !NamesCompatible(left[leftIndex], right[rightIndex]))
                continue;
            visitedRight[rightIndex] = true;
            if (matchedLeftByRight[rightIndex] == -1 || TryMatchAuthor(
                left, right, matchedLeftByRight[rightIndex], matchedLeftByRight, visitedRight))
            {
                matchedLeftByRight[rightIndex] = leftIndex;
                return true;
            }
        }
        return false;
    }

    private static bool NamesCompatible(string[] left, string[] right)
    {
        if (left.Length != right.Length || !left.Any(leftToken => leftToken.Length > 1 &&
            right.Contains(leftToken, StringComparer.Ordinal)))
            return false;
        int[] matchedLeftByRight = Enumerable.Repeat(-1, right.Length).ToArray();
        for (int leftIndex = 0; leftIndex < left.Length; leftIndex++)
        {
            bool[] visitedRight = new bool[right.Length];
            if (!TryMatchToken(left, right, leftIndex, matchedLeftByRight, visitedRight))
                return false;
        }
        return true;
    }

    private static bool TryMatchToken(string[] left, string[] right, int leftIndex,
        int[] matchedLeftByRight, bool[] visitedRight)
    {
        for (int rightIndex = 0; rightIndex < right.Length; rightIndex++)
        {
            string leftToken = left[leftIndex];
            string rightToken = right[rightIndex];
            if (visitedRight[rightIndex] || !(leftToken == rightToken ||
                leftToken[0] == rightToken[0] && (leftToken.Length == 1 || rightToken.Length == 1)))
                continue;
            visitedRight[rightIndex] = true;
            if (matchedLeftByRight[rightIndex] == -1 || TryMatchToken(
                left, right, matchedLeftByRight[rightIndex], matchedLeftByRight, visitedRight))
            {
                matchedLeftByRight[rightIndex] = leftIndex;
                return true;
            }
        }
        return false;
    }

    private static bool IsPlaceholder(string[] tokens)
    {
        HashSet<string> values = tokens.ToHashSet(StringComparer.Ordinal);
        return values.SetEquals(["unknown", "author"]) || values.SetEquals(["anonymous", "author"]);
    }

    private static string CanonicalNameKey(string[] tokens) =>
        string.Join(' ', tokens.OrderBy(token => token, StringComparer.Ordinal));

    private static string[] NormalizeTokens(string value) => NormalizeWords(value)?
        .Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];

    private static string? NormalizeWords(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        StringBuilder normalized = new();
        bool pendingSpace = false;
        foreach (char character in value.Normalize(NormalizationForm.FormKD))
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetterOrDigit(character))
            {
                if (pendingSpace && normalized.Length != 0)
                    normalized.Append(' ');
                normalized.Append(char.ToLowerInvariant(character));
                pendingSpace = false;
            }
            else if (category is UnicodeCategory.MathSymbol or UnicodeCategory.CurrencySymbol or
                UnicodeCategory.ModifierSymbol or UnicodeCategory.OtherSymbol)
            {
                if (pendingSpace && normalized.Length != 0)
                    normalized.Append(' ');
                normalized.Append(character);
                pendingSpace = false;
            }
            else
                pendingSpace = true;
        }
        return normalized.Length == 0 ? null : normalized.ToString();
    }

    private static string StableWorkOrder(AcademicWork work) => string.Join('|',
        work.Provider.ToString(), work.ProviderWorkId?.Trim().ToLowerInvariant(),
        work.Id.ToString(CultureInfo.InvariantCulture));

    private static WorkIdentity CreateDoiIdentity(string normalizedDoi) =>
        new("doi:" + normalizedDoi, normalizedDoi, null, Hash("doi:" + normalizedDoi), [normalizedDoi], []);

    private static Dictionary<string, WorkIdentity> ResolveProviderRelations(List<AcademicWork> works)
    {
        List<ProviderWorkRelation> relations = works.SelectMany(ProviderWorkRelationParser.Parse)
            .Distinct().ToList();
        Dictionary<string, HashSet<string>> adjacency = new(StringComparer.Ordinal);
        foreach (ProviderWorkRelation relation in relations)
        {
            AddNeighbor(relation.SourceDoi, relation.TargetDoi);
            AddNeighbor(relation.TargetDoi, relation.SourceDoi);
        }

        Dictionary<string, WorkIdentity> result = new(StringComparer.Ordinal);
        HashSet<string> unvisited = adjacency.Keys.ToHashSet(StringComparer.Ordinal);
        while (unvisited.Count != 0)
        {
            string seed = unvisited.Order(StringComparer.Ordinal).First();
            HashSet<string> nodes = [];
            Queue<string> pending = new([seed]);
            unvisited.Remove(seed);
            while (pending.TryDequeue(out string? current))
            {
                nodes.Add(current);
                foreach (string neighbor in adjacency[current])
                    if (unvisited.Remove(neighbor)) pending.Enqueue(neighbor);
            }
            WorkIdentity? identity = ResolveRelationComponent(nodes,
                relations.Where(value => nodes.Contains(value.SourceDoi)).ToList());
            if (identity is not null)
                foreach (string node in nodes) result[node] = identity;
        }
        return result;

        void AddNeighbor(string left, string right)
        {
            if (!adjacency.TryGetValue(left, out HashSet<string>? values))
                adjacency[left] = values = new(StringComparer.Ordinal);
            values.Add(right);
        }
    }

    private static WorkIdentity? ResolveRelationComponent(
        HashSet<string> nodes, List<ProviderWorkRelation> relations)
    {
        Dictionary<string, HashSet<string>> parents = new(StringComparer.Ordinal);
        foreach (ProviderWorkRelation relation in relations.Where(value =>
            value.Kind != ProviderWorkRelationKind.VersionEquivalent))
        {
            string child = relation.Kind == ProviderWorkRelationKind.IsVersionOf
                ? relation.SourceDoi : relation.TargetDoi;
            string parent = relation.Kind == ProviderWorkRelationKind.IsVersionOf
                ? relation.TargetDoi : relation.SourceDoi;
            if (!parents.TryGetValue(child, out HashSet<string>? values))
                parents[child] = values = new(StringComparer.Ordinal);
            values.Add(parent);
            if (values.Count > 1) return null;
        }
        string? directedRoot = null;
        if (parents.Count != 0)
        {
            HashSet<string> directedNodes = parents.Keys
                .Concat(parents.Values.SelectMany(value => value)).ToHashSet(StringComparer.Ordinal);
            foreach (string node in directedNodes)
            {
                HashSet<string> path = [];
                string current = node;
                while (parents.TryGetValue(current, out HashSet<string>? values))
                {
                    if (!path.Add(current)) return null;
                    current = values.Single();
                }
                if (directedRoot is null) directedRoot = current;
                else if (directedRoot != current) return null;
            }
        }
        if (directedRoot is not null)
            return CreateDoiIdentity(directedRoot) with { DoiAliases = nodes, Relations = relations };

        string key = Hash("doi-versions|" + string.Join('|', nodes.Order(StringComparer.Ordinal)));
        return CreateSourceIdentity(key) with { DoiAliases = nodes, Relations = relations };
    }

    private static WorkIdentity CreateSourceIdentity(AcademicWork work)
    {
        string providerIdentity = string.IsNullOrWhiteSpace(work.ProviderWorkId)
            ? "academic-work:" + work.Id
            : "provider-work:" + work.ProviderWorkId.Trim().ToLowerInvariant();
        return CreateSourceIdentity(Hash($"{work.PersonelId}|{work.Provider}|{providerIdentity}"));
    }

    private static WorkIdentity CreateSourceIdentity(string sourceScopedKey) =>
        new("source:" + sourceScopedKey, null, sourceScopedKey, Hash("source:" + sourceScopedKey), [], []);

    internal static bool RelationsAreUnambiguous(IReadOnlyCollection<ProviderWorkRelation> relations)
    {
        if (relations.Count == 0) return true;
        HashSet<string> nodes = relations.SelectMany(value => new[] { value.SourceDoi, value.TargetDoi })
            .ToHashSet(StringComparer.Ordinal);
        return ResolveRelationComponent(nodes, relations.Distinct().ToList()) is not null;
    }

    private static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record WorkMetadata(string Title, int Year, string[][] Authors)
    {
        public string AuthorKey { get; } = string.Join(',', Authors.Select(CanonicalNameKey)
            .OrderBy(value => value, StringComparer.Ordinal));
        public int AuthorInformation { get; } = Authors.Sum(tokens => tokens.Sum(token => token.Length));
    }
}

internal sealed record WorkIdentity(
    string DictionaryKey,
    string? NormalizedDoi,
    string? SourceScopedKey,
    string LockKey,
    IReadOnlyCollection<string> DoiAliases,
    IReadOnlyCollection<ProviderWorkRelation> Relations);
