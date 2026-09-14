using AcademicCollectorDemo.Modules.AcademicPerformance.Api.V1.Contracts;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Metrics;

public sealed record PublicationMetricObservation(
    int CanonicalWorkId,
    bool HasNormalizedCanonicalDoi,
    int? PublicationYearObserved,
    DateTime? PublicationDateObserved,
    AcademicWorkCategory CategoryObserved,
    bool HasSavedAbstract,
    bool HasRecordedSourceUrl,
    AcademicWorkProvider Provider = AcademicWorkProvider.Legacy,
    int? CitedByCount = null);

public static class PublicationMetricsCalculator
{
    public static ResearcherPublicationMetricsResponse Calculate(
        string personelId,
        IReadOnlyCollection<PublicationMetricObservation> observations,
        int unmappedAcademicWorkCount,
        DateTime computedAt)
    {
        int validYearUpperBound = checked(computedAt.Year + 1);
        IGrouping<int, PublicationMetricObservation>[] works = observations
            .GroupBy(observation => observation.CanonicalWorkId)
            .OrderBy(group => group.Key)
            .ToArray();

        Dictionary<int, int> years = [];
        int resolvedYears = 0;
        int conflictingYears = 0;
        int unknownYears = 0;
        int invalidYearObservations = 0;
        int invalidYearWorks = 0;

        Dictionary<AcademicWorkCategory, int> categories = Enum
            .GetValues<AcademicWorkCategory>()
            .ToDictionary(category => category, _ => 0);
        int resolvedCategories = 0;
        int conflictingCategories = 0;
        int unknownCategories = 0;

        foreach (IGrouping<int, PublicationMetricObservation> work in works)
        {
            int[] selectedYears = work
                .Select(SelectYearCandidate)
                .Where(year => year.HasValue)
                .Select(year => year!.Value)
                .ToArray();
            int workInvalidYearCount = selectedYears.Count(year =>
                year < 1 || year > validYearUpperBound);
            invalidYearObservations += workInvalidYearCount;
            if (workInvalidYearCount > 0)
                invalidYearWorks++;

            int[] validYears = selectedYears
                .Where(year => year >= 1 && year <= validYearUpperBound)
                .Distinct()
                .Order()
                .ToArray();
            if (validYears.Length == 1)
            {
                resolvedYears++;
                years[validYears[0]] = years.GetValueOrDefault(validYears[0]) + 1;
            }
            else if (validYears.Length > 1)
            {
                conflictingYears++;
            }
            else
            {
                unknownYears++;
            }

            AcademicWorkCategory[] knownCategories = work
                .Select(observation => observation.CategoryObserved)
                .Where(category => category != AcademicWorkCategory.Unknown)
                .Distinct()
                .Order()
                .ToArray();
            if (knownCategories.Length == 1)
            {
                resolvedCategories++;
                categories[knownCategories[0]]++;
            }
            else if (knownCategories.Length > 1)
            {
                conflictingCategories++;
            }
            else
            {
                unknownCategories++;
                categories[AcademicWorkCategory.Unknown]++;
            }
        }

        return new ResearcherPublicationMetricsResponse
        {
            PersonelId = personelId,
            Catalog = PublicationMetricCatalog.Catalog,
            CatalogVersion = PublicationMetricCatalog.Version,
            ResultLabel = PublicationMetricCatalog.ResultLabel,
            ComputedAt = computedAt,
            ValidYearUpperBound = validYearUpperBound,
            CanonicalWorkCount = works.Length,
            ProviderObservationCount = observations.Count,
            UnmappedAcademicWorkCount = unmappedAcademicWorkCount,
            Definitions = PublicationMetricCatalog.CreateDefinitions(),
            PublicationYears = new PublicationYearMetricsDto
            {
                Histogram = years.OrderBy(pair => pair.Key)
                    .Select(pair => new PublicationYearBucketDto
                    {
                        Year = pair.Key,
                        CanonicalWorkCount = pair.Value
                    }).ToList(),
                ResolvedCanonicalWorkCount = resolvedYears,
                ConflictCanonicalWorkCount = conflictingYears,
                UnknownCanonicalWorkCount = unknownYears,
                InvalidYearObservationCount = invalidYearObservations,
                InvalidYearCanonicalWorkCount = invalidYearWorks
            },
            Categories = new PublicationCategoryMetricsDto
            {
                Histogram = categories.OrderBy(pair => pair.Key)
                    .Select(pair => new PublicationCategoryBucketDto
                    {
                        Category = pair.Key.ToString(),
                        CanonicalWorkCount = pair.Value
                    }).ToList(),
                ResolvedCanonicalWorkCount = resolvedCategories,
                ConflictCanonicalWorkCount = conflictingCategories,
                UnknownCanonicalWorkCount = unknownCategories
            },
            Coverage = new PublicationCoverageMetricsDto
            {
                NormalizedCanonicalDoi = Coverage(works.Length,
                    works.Count(work => work.Any(observation =>
                        observation.HasNormalizedCanonicalDoi))),
                SavedAbstract = Coverage(works.Length,
                    works.Count(work => work.Any(observation => observation.HasSavedAbstract))),
                RecordedSourceUrl = Coverage(works.Length,
                    works.Count(work => work.Any(observation => observation.HasRecordedSourceUrl)))
            }
        };
    }

    private static int? SelectYearCandidate(PublicationMetricObservation observation) =>
        observation.PublicationYearObserved ?? observation.PublicationDateObserved?.Year;

    private static PublicationCoverageValueDto Coverage(int denominator, int numerator) => new()
    {
        Numerator = numerator,
        EligibleDenominator = denominator,
        Missing = denominator - numerator,
        Proportion = denominator == 0 ? null : (decimal)numerator / denominator
    };
}
