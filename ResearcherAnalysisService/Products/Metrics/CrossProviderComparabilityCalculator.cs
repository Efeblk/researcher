using ResearcherAnalysisService.Products.Api.Contracts;
using ResearcherAnalysisService.SourceData.Works;

namespace ResearcherAnalysisService.Products.Metrics;

public static class CrossProviderComparabilityCalculator
{
    public static CrossProviderComparabilityDto Calculate(
        IReadOnlyCollection<PublicationMetricObservation> observations,
        int validYearUpperBound)
    {
        var works = observations.Where(value => value.HasNormalizedCanonicalDoi)
            .GroupBy(value => value.CanonicalWorkId)
            .ToDictionary(group => group.Key, group => group.GroupBy(value => value.Provider)
                .ToDictionary(provider => provider.Key, provider => provider.ToArray()));
        AcademicWorkProvider[] providers = observations.Select(value => value.Provider)
            .Distinct().Order().ToArray();
        List<CrossProviderPairDto> pairs = [];
        for (int first = 0; first < providers.Length; first++)
        for (int second = first + 1; second < providers.Length; second++)
        {
            AcademicWorkProvider left = providers[first];
            AcademicWorkProvider right = providers[second];
            var overlap = works.Values.Where(work => work.ContainsKey(left) && work.ContainsKey(right))
                .Select(work => (Left: Resolve(work[left], validYearUpperBound),
                    Right: Resolve(work[right], validYearUpperBound))).ToArray();
            pairs.Add(new()
            {
                FirstProvider = left.ToString(),
                SecondProvider = right.ToString(),
                CanonicalWorkOverlapDenominator = overlap.Length,
                PublicationYear = Compare(overlap.Select(value => (value.Left.Year, value.Right.Year))),
                Category = Compare(overlap.Select(value => (value.Left.Category, value.Right.Category))),
                CitationCount = CompareCitations(overlap.Select(value =>
                    (value.Left.Citations, value.Right.Citations)))
            });
        }
        return new()
        {
            Scope = "Current observations owned by the requested researcher and joined only through a shared canonical work. Agreement describes saved-data consistency, not truth, coverage equivalence, or provider interchangeability.",
            ProviderPairs = pairs
        };
    }

    private static ResolvedObservation Resolve(
        IReadOnlyCollection<PublicationMetricObservation> observations,
        int validYearUpperBound) => new(
        ResolveValue<int>(observations.Select(value => value.PublicationYearObserved ??
            value.PublicationDateObserved?.Year).Where(value => value is >= 1)
            .Where(value => value <= validYearUpperBound)),
        ResolveValue<AcademicWorkCategory>(observations.Select(value =>
                (AcademicWorkCategory?)value.CategoryObserved)
            .Where(value => value != AcademicWorkCategory.Unknown)),
        ResolveValue<int>(observations.Select(value => value.CitedByCount)
            .Where(value => value >= 0)));

    private static ResolvedValue<T> ResolveValue<T>(IEnumerable<T?> values) where T : struct
    {
        T[] distinct = values.Where(value => value.HasValue).Select(value => value!.Value)
            .Distinct().ToArray();
        return distinct.Length switch
        {
            0 => new(null, false),
            1 => new(distinct[0], false),
            _ => new(null, true)
        };
    }

    private static CrossProviderFieldComparisonDto Compare<T>(
        IEnumerable<(ResolvedValue<T> Left, ResolvedValue<T> Right)> values) where T : struct
    {
        var rows = values.ToArray();
        int conflict = rows.Count(row => row.Left.IsConflict || row.Right.IsConflict);
        var comparable = rows.Where(row => !row.Left.IsConflict && !row.Right.IsConflict &&
            row.Left.Value.HasValue && row.Right.Value.HasValue).ToArray();
        int missing = rows.Length - conflict - comparable.Length;
        int agreement = comparable.Count(row => EqualityComparer<T>.Default.Equals(
            row.Left.Value!.Value, row.Right.Value!.Value));
        return new()
        {
            AvailablePairCount = comparable.Length,
            MissingPairCount = missing,
            ConflictPairCount = conflict,
            ExactAgreementCount = agreement,
            ExactAgreementProportion = comparable.Length == 0 ? null :
                (decimal)agreement / comparable.Length
        };
    }

    private static CrossProviderCitationComparisonDto CompareCitations(
        IEnumerable<(ResolvedValue<int> Left, ResolvedValue<int> Right)> values)
    {
        var rows = values.ToArray();
        CrossProviderFieldComparisonDto common = Compare(rows);
        long[] differences = rows.Where(row => !row.Left.IsConflict && !row.Right.IsConflict &&
                row.Left.Value.HasValue && row.Right.Value.HasValue)
            .Select(row => Math.Abs((long)row.Left.Value!.Value - row.Right.Value!.Value)).ToArray();
        return new()
        {
            AvailablePairCount = common.AvailablePairCount,
            MissingPairCount = common.MissingPairCount,
            ConflictPairCount = common.ConflictPairCount,
            ExactAgreementCount = common.ExactAgreementCount,
            ExactAgreementProportion = common.ExactAgreementProportion,
            AbsoluteDifferenceSum = differences.Sum(),
            MeanAbsoluteDifference = differences.Length == 0 ? null :
                (decimal)differences.Sum() / differences.Length
        };
    }

    private sealed record ResolvedObservation(
        ResolvedValue<int> Year,
        ResolvedValue<AcademicWorkCategory> Category,
        ResolvedValue<int> Citations);

    private sealed record ResolvedValue<T>(T? Value, bool IsConflict) where T : struct;
}
