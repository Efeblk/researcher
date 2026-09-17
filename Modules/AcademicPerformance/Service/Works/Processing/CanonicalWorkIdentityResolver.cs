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

        WorkMetadata?[] metadata = works.Select(CreateMetadata).ToArray();
        WorkIdentity?[] result = new WorkIdentity?[works.Count];
        for (int index = 0; index < works.Count; index++)
        {
            string? normalizedDoi = AcademicDoiNormalizer.NormalizeValid(works[index].Doi);
            if (normalizedDoi is not null)
                result[index] = CreateDoiIdentity(normalizedDoi);
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
        string[] dois = component.Select(index => result[index]?.NormalizedDoi)
            .Where(value => value is not null).Select(value => value!)
            .Distinct(StringComparer.Ordinal).ToArray();
        if (!IsClique(component, metadata) || dois.Length > 1)
            return;
        if (dois.Length == 1)
        {
            WorkIdentity doiIdentity = CreateDoiIdentity(dois[0]);
            foreach (int index in withoutDoi)
                result[index] = doiIdentity;
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
        new("doi:" + normalizedDoi, normalizedDoi, null, Hash("doi:" + normalizedDoi));

    private static WorkIdentity CreateSourceIdentity(AcademicWork work)
    {
        string providerIdentity = string.IsNullOrWhiteSpace(work.ProviderWorkId)
            ? "academic-work:" + work.Id
            : "provider-work:" + work.ProviderWorkId.Trim().ToLowerInvariant();
        return CreateSourceIdentity(Hash($"{work.PersonelId}|{work.Provider}|{providerIdentity}"));
    }

    private static WorkIdentity CreateSourceIdentity(string sourceScopedKey) =>
        new("source:" + sourceScopedKey, null, sourceScopedKey, Hash("source:" + sourceScopedKey));

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
    string LockKey);
