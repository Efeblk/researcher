using AcademicCollectorDemo.Modules.AcademicPerformance.Metrics;
using AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models;

namespace AcademicCollectorDemo.Tests.Unit;

public sealed class CrossProviderComparabilityCalculatorTests
{
    [Fact]
    public void Calculate_OverlapMissingAndConflict_UsesExplicitDenominatorsWithoutMerging()
    {
        PublicationMetricObservation[] observations =
        [
            Value(1, AcademicWorkProvider.OpenAlex, 2024, AcademicWorkCategory.Article, 5),
            Value(1, AcademicWorkProvider.WebOfScience, 2024, AcademicWorkCategory.Article, 7),
            Value(2, AcademicWorkProvider.OpenAlex, 2023, AcademicWorkCategory.Article, null),
            Value(2, AcademicWorkProvider.WebOfScience, 2022, AcademicWorkCategory.Review, 3),
            Value(2, AcademicWorkProvider.WebOfScience, 2024, AcademicWorkCategory.Review, 3),
            Value(3, AcademicWorkProvider.GoogleScholar, 2020, AcademicWorkCategory.Article, 1)
        ];

        var result = CrossProviderComparabilityCalculator.Calculate(observations, 2027);

        var overlap = result.ProviderPairs.Single(value =>
            value.FirstProvider == "WebOfScience" && value.SecondProvider == "OpenAlex");
        Assert.Equal(2, overlap.CanonicalWorkOverlapDenominator);
        Assert.Equal(1, overlap.PublicationYear.AvailablePairCount);
        Assert.Equal(1, overlap.PublicationYear.ConflictPairCount);
        Assert.Equal(1, overlap.PublicationYear.ExactAgreementCount);
        Assert.Equal(1, overlap.CitationCount.AvailablePairCount);
        Assert.Equal(2, overlap.CitationCount.AbsoluteDifferenceSum);

        var noOverlap = result.ProviderPairs.Single(value =>
            value.FirstProvider == "GoogleScholar" && value.SecondProvider == "OpenAlex");
        Assert.Equal(0, noOverlap.CanonicalWorkOverlapDenominator);
        Assert.Null(noOverlap.Category.ExactAgreementProportion);
    }

    private static PublicationMetricObservation Value(int work, AcademicWorkProvider provider,
        int? year, AcademicWorkCategory category, int? citedBy) => new(
        work, true, year, null, category, false, false, provider, citedBy);
}
