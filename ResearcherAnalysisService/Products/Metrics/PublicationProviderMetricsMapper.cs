using ResearcherAnalysisService.Products.Api.Contracts;
using System.Text.Json;

namespace ResearcherAnalysisService.Products.Metrics;

public static class PublicationProviderMetricsMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string OpenAlexScope =
        "Saved OpenAlex author-profile metrics, kept separate from other providers.";
    private const string GoogleScholarScope =
        "Saved Google Scholar profile metrics and collected-work count, kept separate from other providers.";
    private const string WebOfScienceScope =
        "Saved values derived from the collected de-duplicated Web of Science query works; not an official whole-profile total.";

    public static PublicationProviderMetricsDto Map(
        PublicationProviderMetricSource source,
        int computationYear) => new()
    {
        OpenAlex = new()
        {
            Scope = OpenAlexScope,
            SourceUpdatedAt = AsUtc(source.OpenAlexMetricsUpdatedAt),
            CitationCount = Count(source.OpenAlexCitationCount),
            HIndex = Count(source.OpenAlexHIndex),
            I10Index = Count(source.OpenAlexI10Index),
            DocumentCount = Count(source.OpenAlexDocumentsCount),
            TwoYearMeanCitedness = Decimal(source.OpenAlexTwoYearMeanCitedness),
            FieldDefinitions =
            [
                Field("CitationCount", "OpenAlex author profile", "All-works cited-by count saved from the OpenAlex author record."),
                Field("HIndex", "OpenAlex author profile", "H-index saved from the OpenAlex summary statistics."),
                Field("I10Index", "OpenAlex author profile", "i10-index saved from the OpenAlex summary statistics."),
                Field("DocumentCount", "OpenAlex author profile", "Works count saved from the OpenAlex author record."),
                Field("TwoYearMeanCitedness", "OpenAlex author profile", "Two-year mean citedness saved from the OpenAlex summary statistics.", "Two-year provider period")
            ]
        },
        GoogleScholar = new()
        {
            Scope = GoogleScholarScope,
            SourceUpdatedAt = AsUtc(source.ScholarMetricsUpdatedAt),
            CitationCount = Count(source.ScholarCitationCount),
            HIndex = Count(source.ScholarHIndex),
            I10Index = Count(source.ScholarI10Index),
            DocumentCount = Count(source.ScholarDocumentsCount),
            CitationCountRecent = Count(source.ScholarCitationCountRecent),
            HIndexRecent = Count(source.ScholarHIndexRecent),
            I10IndexRecent = Count(source.ScholarI10IndexRecent),
            MetricsSinceYear = Year(source.ScholarMetricsSinceYear, computationYear),
            FieldDefinitions =
            [
                Field("CitationCount", "Google Scholar profile", "Provider-reported all-time citation count."),
                Field("HIndex", "Google Scholar profile", "Provider-reported all-time h-index."),
                Field("I10Index", "Google Scholar profile", "Provider-reported all-time i10-index."),
                Field("DocumentCount", "Collected Google Scholar works", "Count of collected works with unique CitationId values; not a provider profile total."),
                Field("CitationCountRecent", "Google Scholar profile", "Provider-reported citation count for the profile's recent period.", "Starts at MetricsSinceYear when reported"),
                Field("HIndexRecent", "Google Scholar profile", "Provider-reported h-index for the profile's recent period.", "Starts at MetricsSinceYear when reported"),
                Field("I10IndexRecent", "Google Scholar profile", "Provider-reported i10-index for the profile's recent period.", "Starts at MetricsSinceYear when reported"),
                Field("MetricsSinceYear", "Google Scholar profile", "Provider-reported start year for recent-period metrics.")
            ]
        },
        WebOfScience = new()
        {
            Scope = WebOfScienceScope,
            SourceUpdatedAt = AsUtc(source.WosMetricsUpdatedAt),
            CitationCount = Count(source.WosCitationCount),
            HIndex = Count(source.WosHIndex),
            DocumentCount = Count(source.WosDocumentsCount),
            FieldDefinitions =
            [
                Field("CitationCount", "Collected Web of Science query works", "Sum of saved citation counts using WOK, then WOS, then first available count per de-duplicated collected work."),
                Field("HIndex", "Collected Web of Science query works", "H-index derived from saved citation counts for the de-duplicated collected result set."),
                Field("DocumentCount", "Collected Web of Science query works", "Count of de-duplicated works returned by the saved WOS/WOK queries.")
            ]
        }
    };

    public static List<PublicationMetricProviderSnapshot> CreateSnapshotRows(
        PublicationProviderMetricsDto metrics) =>
    [
        new()
        {
            Provider = metrics.OpenAlex.Provider,
            SourceUpdatedAt = metrics.OpenAlex.SourceUpdatedAt,
            CitationCount = metrics.OpenAlex.CitationCount.Value,
            HIndex = metrics.OpenAlex.HIndex.Value,
            DocumentCount = metrics.OpenAlex.DocumentCount.Value,
            I10Index = metrics.OpenAlex.I10Index.Value,
            TwoYearMeanCitedness = metrics.OpenAlex.TwoYearMeanCitedness.Value,
            HasInvalidValues = HasInvalid([
                metrics.OpenAlex.CitationCount, metrics.OpenAlex.HIndex,
                metrics.OpenAlex.DocumentCount, metrics.OpenAlex.I10Index
            ]) || metrics.OpenAlex.TwoYearMeanCitedness.Quality == "Invalid",
            FieldMetadataJson = JsonSerializer.Serialize(new
            {
                metrics.OpenAlex.Scope,
                metrics.OpenAlex.FieldDefinitions,
                Quality = new
                {
                    CitationCount = Quality(metrics.OpenAlex.CitationCount),
                    HIndex = Quality(metrics.OpenAlex.HIndex),
                    DocumentCount = Quality(metrics.OpenAlex.DocumentCount),
                    I10Index = Quality(metrics.OpenAlex.I10Index),
                    TwoYearMeanCitedness = Quality(metrics.OpenAlex.TwoYearMeanCitedness)
                }
            }, JsonOptions)
        },
        new()
        {
            Provider = metrics.GoogleScholar.Provider,
            SourceUpdatedAt = metrics.GoogleScholar.SourceUpdatedAt,
            CitationCount = metrics.GoogleScholar.CitationCount.Value,
            HIndex = metrics.GoogleScholar.HIndex.Value,
            DocumentCount = metrics.GoogleScholar.DocumentCount.Value,
            I10Index = metrics.GoogleScholar.I10Index.Value,
            CitationCountRecent = metrics.GoogleScholar.CitationCountRecent.Value,
            HIndexRecent = metrics.GoogleScholar.HIndexRecent.Value,
            I10IndexRecent = metrics.GoogleScholar.I10IndexRecent.Value,
            MetricsSinceYear = metrics.GoogleScholar.MetricsSinceYear.Value,
            HasInvalidValues = HasInvalid([
                metrics.GoogleScholar.CitationCount, metrics.GoogleScholar.HIndex,
                metrics.GoogleScholar.DocumentCount, metrics.GoogleScholar.I10Index,
                metrics.GoogleScholar.CitationCountRecent, metrics.GoogleScholar.HIndexRecent,
                metrics.GoogleScholar.I10IndexRecent, metrics.GoogleScholar.MetricsSinceYear
            ]),
            FieldMetadataJson = JsonSerializer.Serialize(new
            {
                metrics.GoogleScholar.Scope,
                metrics.GoogleScholar.FieldDefinitions,
                Quality = new
                {
                    CitationCount = Quality(metrics.GoogleScholar.CitationCount),
                    HIndex = Quality(metrics.GoogleScholar.HIndex),
                    DocumentCount = Quality(metrics.GoogleScholar.DocumentCount),
                    I10Index = Quality(metrics.GoogleScholar.I10Index),
                    CitationCountRecent = Quality(metrics.GoogleScholar.CitationCountRecent),
                    HIndexRecent = Quality(metrics.GoogleScholar.HIndexRecent),
                    I10IndexRecent = Quality(metrics.GoogleScholar.I10IndexRecent),
                    MetricsSinceYear = Quality(metrics.GoogleScholar.MetricsSinceYear)
                }
            }, JsonOptions)
        },
        new()
        {
            Provider = metrics.WebOfScience.Provider,
            SourceUpdatedAt = metrics.WebOfScience.SourceUpdatedAt,
            CitationCount = metrics.WebOfScience.CitationCount.Value,
            HIndex = metrics.WebOfScience.HIndex.Value,
            DocumentCount = metrics.WebOfScience.DocumentCount.Value,
            HasInvalidValues = HasInvalid([
                metrics.WebOfScience.CitationCount, metrics.WebOfScience.HIndex,
                metrics.WebOfScience.DocumentCount
            ]),
            FieldMetadataJson = JsonSerializer.Serialize(new
            {
                metrics.WebOfScience.Scope,
                metrics.WebOfScience.FieldDefinitions,
                Quality = new
                {
                    CitationCount = Quality(metrics.WebOfScience.CitationCount),
                    HIndex = Quality(metrics.WebOfScience.HIndex),
                    DocumentCount = Quality(metrics.WebOfScience.DocumentCount)
                }
            }, JsonOptions)
        }
    ];

    private static ProviderCountMetricDto Count(int? value) => value switch
    {
        null => new()
        {
            Quality = "Unknown",
            QualityReason = "No saved value is available."
        },
        < 0 => new()
        {
            Quality = "Invalid",
            QualityReason = "The saved value is negative; it was normalized to null."
        },
        _ => new() { Value = value, Quality = "Available" }
    };

    private static ProviderDecimalMetricDto Decimal(decimal? value) => value switch
    {
        null => new()
        {
            Quality = "Unknown",
            QualityReason = "No saved value is available."
        },
        < 0 => new()
        {
            Quality = "Invalid",
            QualityReason = "The saved value is negative; it was normalized to null."
        },
        _ => new() { Value = value, Quality = "Available" }
    };

    private static ProviderCountMetricDto Year(int? value, int computationYear) => value switch
    {
        null => new()
        {
            Quality = "Unknown",
            QualityReason = "No saved recent-period start year is available."
        },
        < 1 => new()
        {
            Quality = "Invalid",
            QualityReason = "The saved recent-period start year is below 1; it was normalized to null."
        },
        _ when value > computationYear => new()
        {
            Quality = "Invalid",
            QualityReason = "The saved recent-period start year is after the computation year; it was normalized to null."
        },
        _ => new() { Value = value, Quality = "Available" }
    };

    private static DateTime? AsUtc(DateTime? value) => value.HasValue
        ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
        : null;

    private static ProviderMetricFieldDefinitionDto Field(
        string field, string origin, string scope, string? period = null) => new()
        {
            Field = field,
            Origin = origin,
            Scope = scope,
            Period = period
        };

    private static bool HasInvalid(IEnumerable<ProviderCountMetricDto> values) =>
        values.Any(value => value.Quality == "Invalid");

    private static object Quality(ProviderCountMetricDto value) => new
    {
        value.Quality,
        value.QualityReason
    };

    private static object Quality(ProviderDecimalMetricDto value) => new
    {
        value.Quality,
        value.QualityReason
    };
}
