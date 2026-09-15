using ResearcherAnalysisService.Products.Metrics;
using ResearcherAnalysisService.Products.Api.Contracts;
using ResearcherAnalysisService.SourceData.Works;

namespace ResearcherAnalysisService.Tests;

public sealed class PublicationMetricsCalculatorTests
{
    [Fact]
    public void Calculate_ConflictingUnknownAndInvalidYears_UsesDeclaredBuckets()
    {
        DateTime computedAt = new(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);
        PublicationMetricObservation[] observations =
        [
            Observation(1, null, new DateTime(2020, 4, 2), AcademicWorkCategory.Article),
            Observation(1, 2020, null, AcademicWorkCategory.Article),
            Observation(2, 2019, null, AcademicWorkCategory.Book),
            Observation(2, 2021, null, AcademicWorkCategory.BookChapter),
            Observation(3, null, null, AcademicWorkCategory.Unknown),
            Observation(4, 2028, new DateTime(2018, 1, 1), AcademicWorkCategory.Dataset),
            Observation(5, -1, null, AcademicWorkCategory.Report),
            Observation(5, 2017, null, AcademicWorkCategory.Report)
        ];

        var result = PublicationMetricsCalculator.Calculate(
            "person", observations, unmappedAcademicWorkCount: 2, computedAt);

        Assert.Equal(5, result.CanonicalWorkCount);
        Assert.Equal(8, result.ProviderObservationCount);
        Assert.Equal(2, result.UnmappedAcademicWorkCount);
        Assert.Equal(2027, result.ValidYearUpperBound);
        Assert.Equal([(2017, 1), (2020, 1)], result.PublicationYears.Histogram
            .Select(bucket => (bucket.Year, bucket.CanonicalWorkCount)));
        Assert.Equal(2, result.PublicationYears.ResolvedCanonicalWorkCount);
        Assert.Equal(1, result.PublicationYears.ConflictCanonicalWorkCount);
        Assert.Equal(2, result.PublicationYears.UnknownCanonicalWorkCount);
        Assert.Equal(2, result.PublicationYears.InvalidYearObservationCount);
        Assert.Equal(2, result.PublicationYears.InvalidYearCanonicalWorkCount);
        Assert.Equal(1, result.Categories.ConflictCanonicalWorkCount);
        Assert.Equal(1, result.Categories.UnknownCanonicalWorkCount);
        Assert.Equal(Enum.GetValues<AcademicWorkCategory>().Length,
            result.Categories.Histogram.Count);
    }

    [Fact]
    public void Calculate_EmptyPopulation_ReturnsNullCoverageProportions()
    {
        var result = PublicationMetricsCalculator.Calculate(
            "empty", [], 0, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(0, result.CanonicalWorkCount);
        Assert.Empty(result.PublicationYears.Histogram);
        Assert.All(new[]
        {
            result.Coverage.NormalizedCanonicalDoi,
            result.Coverage.SavedAbstract,
            result.Coverage.RecordedSourceUrl
        }, coverage =>
        {
            Assert.Equal(0, coverage.EligibleDenominator);
            Assert.Equal(0, coverage.Missing);
            Assert.Null(coverage.Proportion);
        });
    }

    [Fact]
    public void MapProviderMetrics_ZeroMissingAndInvalidValues_PreservesQualityAndOrigins()
    {
        DateTime collectedAt = new(2025, 6, 1, 10, 30, 0, DateTimeKind.Unspecified);
        PublicationProviderMetricSource source = new(
            OpenAlexCitationCount: 0,
            OpenAlexHIndex: -1,
            OpenAlexI10Index: null,
            OpenAlexDocumentsCount: 12,
            OpenAlexTwoYearMeanCitedness: -0.5m,
            OpenAlexMetricsUpdatedAt: collectedAt,
            ScholarCitationCount: 0,
            ScholarHIndex: 4,
            ScholarI10Index: null,
            ScholarDocumentsCount: 0,
            ScholarCitationCountRecent: 7,
            ScholarHIndexRecent: 2,
            ScholarI10IndexRecent: 1,
            ScholarMetricsSinceYear: 2027,
            ScholarMetricsUpdatedAt: null,
            WosCitationCount: -3,
            WosHIndex: 0,
            WosDocumentsCount: 5,
            WosMetricsUpdatedAt: collectedAt.AddDays(-1));

        var result = PublicationProviderMetricsMapper.Map(source, computationYear: 2026);

        Assert.Equal(0, result.OpenAlex.CitationCount.Value);
        Assert.Equal("Available", result.OpenAlex.CitationCount.Quality);
        Assert.Null(result.OpenAlex.HIndex.Value);
        Assert.Equal("Invalid", result.OpenAlex.HIndex.Quality);
        Assert.Null(result.OpenAlex.I10Index.Value);
        Assert.Equal("Unknown", result.OpenAlex.I10Index.Quality);
        Assert.Null(result.OpenAlex.TwoYearMeanCitedness.Value);
        Assert.Equal(DateTimeKind.Utc, result.OpenAlex.SourceUpdatedAt!.Value.Kind);
        Assert.Equal(0, result.GoogleScholar.DocumentCount.Value);
        Assert.Null(result.GoogleScholar.MetricsSinceYear.Value);
        Assert.Equal("Invalid", result.GoogleScholar.MetricsSinceYear.Quality);
        Assert.Null(result.WebOfScience.CitationCount.Value);
        Assert.Contains(result.WebOfScience.FieldDefinitions,
            definition => definition.Field == "HIndex" &&
                definition.Origin.Contains("collected", StringComparison.OrdinalIgnoreCase));

        List<PublicationMetricProviderSnapshot> rows =
            PublicationProviderMetricsMapper.CreateSnapshotRows(result);
        Assert.Equal(3, rows.Count);
        PublicationMetricProviderSnapshot openAlex = rows.Single(row => row.Provider == "OpenAlex");
        Assert.Equal(0, openAlex.CitationCount);
        Assert.Null(openAlex.HIndex);
        Assert.True(openAlex.HasInvalidValues);
        Assert.Contains("fieldDefinitions", openAlex.FieldMetadataJson);
    }

    [Fact]
    public void CalculateContext_DeduplicatesCanonicalWorksAndReportsValueAndGroupConflicts()
    {
        PublicationContextMetricObservation[] observations =
        [
            Context(1, 11, "T1", "S1", "F1", 2024, "article", "journal", 1m, 0m),
            Context(1, 12, "T1", "S1", "F1", 2024, "article", "journal", 1m, 0m),
            Context(1, 13, "T1", "S1", "F1", null, "article", "journal", 7m, 0m),
            Context(2, 21, "T1", "S1", "F1", 2023, "article", "conference", 2m, null),
            Context(2, 22, "T1", "S1", "F1", 2024, "article", "conference", 2m, null),
            Context(3, 31, "T2", "S2", "F2", 2024, "book", null, 2m, 0.5m),
            Context(3, 32, "T3", "S3", "F3", 2024, "book", null, 3m, 0.6m),
            Context(4, 41, "T4", "S4", "F4", 2024, "article", "journal", 2m, 0.7m),
            Context(4, 42, "T4", "S4", "F4", 2024, "article", "journal", 3m, 0.7m)
        ];

        PublicationContextualMetricsDto result = PublicationContextualMetricsCalculator.Calculate(
            5, observations, 2027);

        Assert.Equal(5, result.EligibleCanonicalWorkCount);
        Assert.Equal(4, result.OpenAlexContextCoverage.Numerator);
        Assert.Equal(1, result.MissingContextCanonicalWorkCount);
        Assert.Equal(1, result.PrimaryTopicConflictCanonicalWorkCount);
        Assert.Equal(2, result.PrimaryGroupConflictCanonicalWorkCount);
        Assert.Equal(2, result.PrimaryTopics.Single(topic => topic.TopicId == "T1").CanonicalWorkCount);
        Assert.Equal(1, result.ProviderReportedNormalization.Fwci.AvailableValueCount);
        Assert.Equal(3, result.ProviderReportedNormalization.Fwci.ConflictValueCount);
        Assert.Null(result.InternalNormalizedScore);
        Assert.Equal("ReferencePopulationUnavailable", result.InternalNormalizedScoreStatus);
        Assert.Contains(result.PrimarySubfieldGroups, group => group.ClassificationId == "S1" &&
            group.ArticlePrimarySourceType == "journal" && group.CanonicalWorkCount == 1 &&
            group.MeanFwci.ConflictValueCount == 1 && group.MeanFwci.MeanValue == null);
        Assert.Equal(3, result.PrimaryTopicCoverage.Numerator);
        Assert.Equal(2, result.PrimarySubfieldGroupCoverage.Numerator);
    }

    private static PublicationMetricObservation Observation(
        int canonicalWorkId,
        int? year,
        DateTime? date,
        AcademicWorkCategory category) => new(
            canonicalWorkId,
            HasNormalizedCanonicalDoi: true,
            year,
            date,
            category,
            HasSavedAbstract: false,
            HasRecordedSourceUrl: false,
            Provider: AcademicWorkProvider.Orcid);

    private static PublicationContextMetricObservation Context(
        int canonicalWorkId,
        int academicWorkId,
        string topicId,
        string subfieldId,
        string fieldId,
        int? year,
        string rawType,
        string? sourceType,
        decimal? fwci,
        decimal? percentile) => new(
            canonicalWorkId, academicWorkId, "Available", "Available",
            topicId, "Topic " + topicId, subfieldId, "Subfield " + subfieldId,
            fieldId, "Field " + fieldId, year, rawType, sourceType,
            fwci, fwci.HasValue ? "Available" : "Unknown",
            percentile, percentile.HasValue ? "Available" : "Unknown");
}
