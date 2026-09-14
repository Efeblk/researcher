using ResearcherAnalysisService.Products.Api.Contracts;

namespace ResearcherAnalysisService.Products.Metrics;

public static class PublicationContextualMetricsCalculator
{
    private const string Scope =
        "Collected canonical works for the requested researcher; research context is available only from that researcher's current OpenAlex observations.";

    public static PublicationContextualMetricsDto Calculate(
        int eligibleCanonicalWorkCount,
        IReadOnlyCollection<PublicationContextMetricObservation> observations,
        int validYearUpperBound)
    {
        IGrouping<int, PublicationContextMetricObservation>[] works = observations
            .GroupBy(observation => observation.CanonicalWorkId).OrderBy(group => group.Key).ToArray();
        int covered = works.Count(work => work.Any(IsAvailable));
        int missing = eligibleCanonicalWorkCount - works.Count(work =>
            work.Any(observation => observation.ParseQuality != "Missing"));
        int invalid = works.Count(work => work.Any(observation =>
            observation.ParseQuality is not ("Available" or "Missing")));
        int topicConflicts = 0;
        HashSet<int> topicResolvedWorks = [];
        Dictionary<string, TopicAccumulator> topics = new(StringComparer.Ordinal);
        Dictionary<GroupKey, GroupAccumulator> subfields = [];
        Dictionary<GroupKey, GroupAccumulator> fields = [];
        HashSet<int> groupConflictWorks = [];

        foreach (IGrouping<int, PublicationContextMetricObservation> work in works)
        {
            PublicationContextMetricObservation[] available = work.Where(IsAvailable).ToArray();
            bool declaredConflict = available.Any(value => value.PrimaryTopicQuality == "Conflict");
            string[] topicIds = available.Where(value => value.PrimaryTopicQuality == "Available" &&
                    value.PrimaryTopicId is not null)
                .Select(value => value.PrimaryTopicId!).Distinct(StringComparer.Ordinal).ToArray();
            if (declaredConflict || topicIds.Length > 1)
            {
                topicConflicts++;
                groupConflictWorks.Add(work.Key);
            }
            else if (topicIds.Length == 1)
            {
                topicResolvedWorks.Add(work.Key);
                string topicId = topicIds[0];
                PublicationContextMetricObservation[] matching = available
                    .Where(value => value.PrimaryTopicId == topicId).ToArray();
                TopicAccumulator topic = topics.GetValueOrDefault(topicId) ?? new(topicId);
                topic.Count++;
                topic.AddLabels(matching.Select(value => value.PrimaryTopicName));
                topics[topicId] = topic;

                AddGroup(work.Key, matching, validYearUpperBound, subfields,
                    value => (value.SubfieldId, value.SubfieldName), groupConflictWorks);
                AddGroup(work.Key, matching, validYearUpperBound, fields,
                    value => (value.FieldId, value.FieldName), groupConflictWorks);
            }
        }

        return new()
        {
            Scope = Scope,
            EligibleCanonicalWorkCount = eligibleCanonicalWorkCount,
            OpenAlexContextCoverage = Coverage(eligibleCanonicalWorkCount, covered),
            PrimaryTopicCoverage = Coverage(eligibleCanonicalWorkCount, topicResolvedWorks.Count),
            PrimarySubfieldGroupCoverage = Coverage(eligibleCanonicalWorkCount,
                subfields.Values.SelectMany(group => group.Works).Distinct().Count()),
            PrimaryFieldGroupCoverage = Coverage(eligibleCanonicalWorkCount,
                fields.Values.SelectMany(group => group.Works).Distinct().Count()),
            MissingContextCanonicalWorkCount = Math.Max(missing, 0),
            InvalidContextCanonicalWorkCount = invalid,
            PrimaryTopicConflictCanonicalWorkCount = topicConflicts,
            PrimaryGroupConflictCanonicalWorkCount = groupConflictWorks.Count,
            PrimaryTopics = topics.Values.OrderBy(value => value.TopicId, StringComparer.Ordinal)
                .Select(value => new PublicationPrimaryTopicBucketDto
                {
                    TopicId = value.TopicId,
                    TopicName = value.Label,
                    CanonicalWorkCount = value.Count
                }).ToList(),
            PrimarySubfieldGroups = MapGroups(subfields),
            PrimaryFieldGroups = MapGroups(fields),
            ProviderReportedNormalization = new()
            {
                Scope = "OpenAlex-supplied normalization values on the requested researcher's current collected OpenAlex works; no local reference population is applied.",
                Fwci = Summarize(eligibleCanonicalWorkCount, works, value => value.Fwci,
                    value => value.FwciQuality),
                CitationNormalizedPercentile = Summarize(eligibleCanonicalWorkCount, works,
                    value => value.CitationNormalizedPercentile,
                    value => value.CitationNormalizedPercentileQuality)
            }
        };
    }

    private static void AddGroup(
        int canonicalWorkId,
        PublicationContextMetricObservation[] observations,
        int validYearUpperBound,
        Dictionary<GroupKey, GroupAccumulator> groups,
        Func<PublicationContextMetricObservation, (string? Id, string? Name)> classification,
        HashSet<int> conflicts)
    {
        GroupCandidate[] candidates = observations.Select(value =>
        {
            (string? id, string? name) = classification(value);
            string rawType = Normalize(value.RawType);
            string? sourceType = rawType.Equals("article", StringComparison.OrdinalIgnoreCase)
                ? NormalizeNullable(value.PrimarySourceType) : null;
            bool valid = id is not null && value.SourcePublicationYear is >= 1 &&
                value.SourcePublicationYear <= validYearUpperBound && rawType.Length > 0;
            return new GroupCandidate(valid ? new GroupKey(id!, value.SourcePublicationYear!.Value,
                rawType, sourceType) : null, name, value);
        }).Where(candidate => candidate.Key is not null).ToArray();
        GroupKey[] keys = candidates.Select(candidate => candidate.Key!).Distinct().ToArray();
        if (keys.Length > 1)
        {
            conflicts.Add(canonicalWorkId);
            return;
        }
        if (keys.Length == 0)
            return;
        GroupKey key = keys[0];
        GroupAccumulator group = groups.GetValueOrDefault(key) ?? new(key);
        group.Works.Add(canonicalWorkId);
        group.Observations.AddRange(observations);
        group.AddLabels(candidates.Where(candidate => candidate.Key == key)
            .Select(candidate => candidate.Label));
        groups[key] = group;
    }

    private static List<PublicationResearchGroupDto> MapGroups(
        Dictionary<GroupKey, GroupAccumulator> groups) => groups.Values
        .OrderBy(value => value.Key.ClassificationId, StringComparer.Ordinal)
        .ThenBy(value => value.Key.PublicationYear)
        .ThenBy(value => value.Key.RawType, StringComparer.Ordinal)
        .ThenBy(value => value.Key.ArticlePrimarySourceType, StringComparer.Ordinal)
        .Select(value => new PublicationResearchGroupDto
        {
            ClassificationId = value.Key.ClassificationId,
            ClassificationName = value.Label,
            PublicationYear = value.Key.PublicationYear,
            RawType = value.Key.RawType,
            ArticlePrimarySourceType = value.Key.ArticlePrimarySourceType,
            Label = $"Collected own OpenAlex works: {value.Label ?? value.Key.ClassificationId}; " +
                $"year {value.Key.PublicationYear}; type {value.Key.RawType}" +
                (value.Key.ArticlePrimarySourceType is null ? string.Empty :
                    $"; primary source type {value.Key.ArticlePrimarySourceType}"),
            CanonicalWorkCount = value.Works.Count,
            MeanFwci = Summarize(value.Works.Count,
                value.Observations.GroupBy(observation => observation.CanonicalWorkId),
                observation => observation.Fwci, observation => observation.FwciQuality)
        }).ToList();

    private static ProviderNormalizedValueSummaryDto Summarize(
        int denominator,
        IEnumerable<IEnumerable<PublicationContextMetricObservation>> works,
        Func<PublicationContextMetricObservation, decimal?> value,
        Func<PublicationContextMetricObservation, string> quality)
    {
        List<decimal> available = [];
        int missing = 0;
        int invalid = 0;
        int conflict = 0;
        foreach (IEnumerable<PublicationContextMetricObservation> work in works)
        {
            PublicationContextMetricObservation[] contexts = work.Where(IsAvailable).ToArray();
            decimal[] values = contexts.Where(item => quality(item) == "Available" && value(item).HasValue)
                .Select(item => value(item)!.Value).Distinct().ToArray();
            if (values.Length > 1)
                conflict++;
            else if (values.Length == 1)
                available.Add(values[0]);
            else if (work.Any(item => item.ParseQuality is not ("Available" or "Missing") ||
                quality(item) == "Invalid"))
                invalid++;
            else
                missing++;
        }
        missing += Math.Max(denominator - (available.Count + missing + invalid + conflict), 0);
        decimal? sum = available.Count == 0 ? null : available.Sum();
        return new()
        {
            EligibleDenominator = denominator,
            AvailableValueCount = available.Count,
            MissingValueCount = missing,
            InvalidValueCount = invalid,
            ConflictValueCount = conflict,
            AvailableValueSum = sum,
            MeanValue = sum.HasValue ? sum.Value / available.Count : null
        };
    }

    private static bool IsAvailable(PublicationContextMetricObservation value) =>
        value.ParseQuality == "Available";

    private static PublicationCoverageValueDto Coverage(int denominator, int numerator) => new()
    {
        Numerator = numerator,
        EligibleDenominator = denominator,
        Missing = denominator - numerator,
        Proportion = denominator == 0 ? null : (decimal)numerator / denominator
    };

    private static string Normalize(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;
    private static string? NormalizeNullable(string? value) => string.IsNullOrWhiteSpace(value)
        ? null : value.Trim().ToLowerInvariant();

    private sealed record GroupKey(
        string ClassificationId,
        int PublicationYear,
        string RawType,
        string? ArticlePrimarySourceType);
    private sealed record GroupCandidate(
        GroupKey? Key,
        string? Label,
        PublicationContextMetricObservation Observation);

    private sealed class TopicAccumulator(string topicId)
    {
        private readonly HashSet<string> _labels = new(StringComparer.Ordinal);
        public string TopicId { get; } = topicId;
        public int Count { get; set; }
        public string? Label => _labels.Count == 1 ? _labels.Single() : null;
        public void AddLabels(IEnumerable<string?> labels)
        {
            foreach (string label in labels.Where(label => !string.IsNullOrWhiteSpace(label))
                .Select(label => label!.Trim()))
                _labels.Add(label);
        }
    }

    private sealed class GroupAccumulator(GroupKey key)
    {
        private readonly HashSet<string> _labels = new(StringComparer.Ordinal);
        public GroupKey Key { get; } = key;
        public HashSet<int> Works { get; } = [];
        public List<PublicationContextMetricObservation> Observations { get; } = [];
        public string? Label => _labels.Count == 1 ? _labels.Single() : null;
        public void AddLabels(IEnumerable<string?> labels)
        {
            foreach (string label in labels.Where(label => !string.IsNullOrWhiteSpace(label))
                .Select(label => label!.Trim()))
                _labels.Add(label);
        }
    }
}
