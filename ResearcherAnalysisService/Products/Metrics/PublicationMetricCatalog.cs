using ResearcherAnalysisService.Products.Api.Contracts;

namespace ResearcherAnalysisService.Products.Metrics;

public static class PublicationMetricCatalog
{
    public const string Catalog = "publication-metrics";
    public const string Version = "publication-metrics-v4";
    public const string ResultLabel = "Collected works, saved provider bibliometrics, OpenAlex research context, and observed cross-provider consistency";

    public static List<PublicationMetricDefinitionDto> CreateDefinitions() =>
    [
        Definition("canonical_work_count", "Unique collected canonical works",
            "Current canonical works with both a current membership and at least one current observation for the requested researcher.",
            "Count each in-scope canonical work once, including every publication category.",
            "Not applicable; this is the in-scope canonical-work population."),
        Definition("provider_observation_count", "Current provider observations",
            "Current observations owned by the requested researcher on in-scope canonical works.",
            "Count provider observations; observations merged into the same canonical work remain separate here.",
            "Not applicable; this is an observation count."),
        Definition("unmapped_academic_work_count", "Unmapped collected works",
            "Current AcademicWork rows owned by the requested researcher.",
            "Count AcademicWork rows that do not have an own canonical observation on a canonical work with current membership for the requested researcher.",
            "All current AcademicWork rows owned by the requested researcher."),
        Definition("publication_year_histogram", "Resolved publication year",
            "In-scope canonical works. Each own observation uses PublicationYearObserved, falling back to PublicationDateObserved.Year only when the observed year is null.",
            "Accept candidates from year 1 through ValidYearUpperBound. One distinct valid year resolves the canonical work; multiple distinct valid years enter the conflict bucket; no valid year enters unknown. Conflict and unknown works are excluded from year buckets.",
            "In-scope canonical works."),
        Definition("invalid_year_observation_count", "Invalid year observations",
            "Current observations owned by the requested researcher on in-scope canonical works.",
            "Count observations whose selected year candidate exists but is outside year 1 through ValidYearUpperBound. An invalid explicit year does not fall back to its date.",
            "Current provider observations for the requested researcher."),
        Definition("invalid_year_canonical_work_count", "Works with invalid year observations",
            "In-scope canonical works.",
            "Count canonical works having at least one invalid selected observation-year candidate. This quality count may overlap resolved, conflict, or unknown year status.",
            "In-scope canonical works."),
        Definition("publication_category_histogram", "Resolved publication category",
            "In-scope canonical works; every AcademicWorkCategory is represented in the returned histogram.",
            "One distinct non-Unknown observed category resolves the canonical work; multiple enter conflict; no non-Unknown category enters unknown. Conflict works are excluded from category buckets; unknown works are counted in the Unknown bucket.",
            "In-scope canonical works."),
        Definition("normalized_canonical_doi_coverage", "Normalized canonical DOI coverage",
            "In-scope canonical works.",
            "Count canonical works whose stored canonical identity has a nonblank normalized DOI.",
            "In-scope canonical works."),
        Definition("saved_abstract_coverage", "Saved abstract coverage",
            "In-scope canonical works and only the requested researcher's current observations.",
            "Count canonical works for which at least one own current AcademicWork has a saved Abstract containing a character other than space, tab, carriage return, or line feed.",
            "In-scope canonical works."),
        Definition("recorded_source_url_coverage", "Recorded source URL coverage",
            "In-scope canonical works and only the requested researcher's current observations.",
            "Count canonical works for which at least one own current AcademicWork has a Link, FullTextUrl, or AcademicWorkSource URL containing a character other than space, tab, carriage return, or line feed. This records candidate metadata only; it does not verify accessibility or full text.",
            "In-scope canonical works."),
        Definition("saved_provider_bibliometrics", "Saved provider bibliometrics",
            "The requested researcher's separately saved OpenAlex, Google Scholar, and Web of Science metric fields, with the scope of each field declared in FieldDefinitions.",
            "Return each provider's saved values in its own block with per-field origin. Preserve zero, return missing as null, and normalize invalid values to null with explicit Invalid quality. Never merge provider totals or newly derive h-index in the metrics worker.",
            "The population declared by each field's FieldDefinitions scope; canonical-work count is not a denominator for these fields."),
        Definition("openalex_research_context", "OpenAlex research context",
            "Only current OpenAlex observations owned by the requested researcher on that researcher's current canonical works. Other providers are unavailable in this context slice.",
            "De-duplicate by canonical work; resolve a primary topic or classification group only when own OpenAlex observations agree. Report conflicts instead of choosing an observation. Provider-supplied FWCI and citation-normalized percentile remain labelled ProviderReportedNormalization.",
            "All in-scope canonical works for coverage and overall availability; each resolved collected-own classification group for its descriptive mean FWCI."),
        Definition("cross_provider_consistency", "Observed cross-provider consistency",
            "Only the requested researcher's current observations that overlap on the same canonical work.",
            "Resolve each provider value independently, report missing and conflicts, and compare only available pairs. Never merge provider values or select a winning provider.",
            "Canonical works observed by both providers in the returned provider pair."),
        Definition("evaluated_output_eligibility", "Evaluated-output eligibility",
            "The versioned collected-publication descriptive policy and reference-population readiness state.",
            "All categories remain visible for descriptive reporting. Provider totals and internal normalized scores are excluded from evaluated outputs until a reviewed reference population and purpose-specific eligibility policy exist.",
            "Not applicable; this is a use policy, not a metric denominator.")
    ];

    private static PublicationMetricDefinitionDto Definition(
        string key, string label, string scope, string formula, string denominator) => new()
        {
            Key = key,
            Label = label,
            Scope = scope,
            Formula = formula,
            Denominator = denominator
        };
}
