# Publication metrics V4

`publication-metrics-v4` is a deterministic, precomputed snapshot of saved publication data, separately saved provider bibliometrics, bounded OpenAlex research context, explicit descriptive eligibility, reference-population readiness, and cross-provider consistency. It describes collected works and provider-specific saved values; it must not be presented as an institution-complete publication record, a cross-provider score, or a reference-population benchmark. Metric computation performs no provider or model call. The read action reads only refresh state and the last immutable snapshot; it never computes, schedules, parses, or scans source rows.

Analysis-owned migrations add immutable `analysis.PublicationMetricSnapshots` rows and one durable `analysis.PublicationMetricsRefreshStates` row per scheduled researcher. They also add immutable typed `analysis.PublicationMetricProviderSnapshots` children, one for each provider in a snapshot. Collector-owned migration `202609110008` adds the regenerated one-to-one `core.AcademicWorkResearchContexts` rows and ranked `core.AcademicWorkTopics` children; Analysis reads those source rows without writing them.

Call the Analysis Service on port 5011 with:

```text
POST /api/v1/products/GetResearcherPublicationMetrics
{ "PersonelID": "..." }
```

`PersonelID` is trimmed and must contain 1-200 characters. A malformed request returns 400 and an unknown researcher returns 404. Before the first snapshot is ready, a known researcher returns 202 with `Data=null`; it is never represented as computed zeros. After the worker computes an empty researcher, the endpoint returns 200 with zero counts and null coverage proportions.

The response wrapper reports `RequestedRevision`, `ComputedRevision`, current/requested/snapshot catalog and computation-year metadata, `SnapshotId`, `Status`, `IsStale`, safe retry outcome fields, and nullable `Data`. Status is `Current`, `Stale`, `Pending`, or `Failed`. A failed refresh retains and returns the last good snapshot as stale. Repeated read actions return the same snapshot ID until a worker commits a new revision.

An explicit refresh only schedules work and returns 202:

```text
POST /api/v1/products/RefreshResearcherPublicationMetrics
{ "PersonelID": "..." }
```

There is no synchronous fallback. Pausing `PublicationMetrics:WorkerEnabled` stops computation while stored snapshots remain readable. Runnable read and refresh requests are in [PublicationMetrics.http](../ResearcherAnalysisService/Requests/PublicationMetrics.http).

## Scope and counts

An in-scope canonical work must have all three of these current records for the requested researcher: a canonical membership, a canonical observation, and the observation's linked `AcademicWork` owned by that researcher. Provider observations for other researchers on a shared DOI never affect the requested researcher's histograms or coverage.

`CanonicalWorkCount` counts each in-scope canonical work once, so two provider observations of the same canonical DOI contribute one. `ProviderObservationCount` counts those own observations separately. `UnmappedAcademicWorkCount` counts the researcher's current `AcademicWork` rows that do not have an own canonical observation on a canonical work with current membership. Every publication category is eligible; this version does not silently filter categories.

Snapshot `Data` includes `CatalogVersion`, `ComputedAt`, the valid year upper bound, and a `Definitions` entry for each returned measure. Each definition states its scope, formula, and denominator. The worker projects only fields and booleans needed by the calculation; provider payloads, abstract text, URLs, and other researchers' identifiers are not persisted in the result. Computation, immutable snapshot insertion, and revision acknowledgment run atomically in one short serializable transaction under the canonical write gate.

Collector canonical reconciliation writes a durable `core.CollectionChanges` signal even when all works were removed. Analysis consumes that signal with an idempotent `analysis.CollectionChangeReceipts` row and invalidates the requested researcher's metrics. Analysis-owned source/summary changes invalidate the same owner in their own transaction. A bounded default-on Analysis worker also discovers researchers without refresh state, including empty researchers. Catalog-version or UTC computation-year changes advance the desired target once and schedule a new snapshot. Failures use bounded exponential retry, store only a safe error, and never replace the last successful snapshot.

## Year and category resolution

For each own observation, the selected year is `PublicationYearObserved`; `PublicationDateObserved.Year` is used only when the observed year is null. A selected year is valid from 1 through the returned `ValidYearUpperBound`, which is the UTC computation year plus one.

For each canonical work:

- one distinct valid year resolves into that year bucket;
- multiple distinct valid years enter the conflict bucket and are excluded from the histogram;
- zero valid years enter the unknown bucket.

`InvalidYearObservationCount` counts selected year values outside the valid range. `InvalidYearCanonicalWorkCount` counts works having at least one such observation. These are separate quality indicators: a work may contain an invalid observation and still resolve from another valid observation. A null year and null date are unknown, not invalid. An invalid explicit year does not fall back to the date.

The category histogram returns every `AcademicWorkCategory`, including zero-count categories. One distinct non-`Unknown` category resolves, multiple distinct categories conflict, and no known category is unknown. Conflict works are excluded from category buckets; unknown works appear in the `Unknown` bucket.

## Coverage

Each coverage value returns `Numerator`, `EligibleDenominator`, `Missing`, and `Proportion`. The denominator is the in-scope canonical-work count. `Proportion` is null when that denominator is zero.

- `NormalizedCanonicalDoi` counts canonical identities with a normalized DOI.
- `SavedAbstract` counts works with a nonblank abstract on at least one own observation.
- `RecordedSourceUrl` counts works with a nonblank saved `Link`, `FullTextUrl`, or `AcademicWorkSource.Url` on at least one own observation.

For abstract and URL coverage, spaces, tabs, carriage returns, and line feeds alone are blank. `RecordedSourceUrl` reports stored candidate metadata. It does not assert that a URL is reachable, contains full text, or is permitted for later acquisition.

V4 explicitly includes every category for descriptive collected-record reporting and excludes provider totals from evaluated outputs. Its cross-provider study reports overlap coverage and observed year, category, and saved work-citation consistency without merging values or selecting a provider as truth. A reference-population manifest can be stored and mechanically validated, but internal normalization remains unavailable until a compatible reviewed population, eligibility policy, and execution formula are active. Citation-context labels remain a separate provider surface.

## Saved provider bibliometrics

`Data.ProviderMetrics` has separate `OpenAlex`, `GoogleScholar`, and `WebOfScience` blocks. Values are never added together, and the metrics worker does not derive a new h-index from the collected canonical-work set. Every field includes `Value`, `Quality`, and an optional `QualityReason`; zero is a valid available value, a missing saved value is `Unknown` with `Value=null`, and a negative saved value is `Invalid` and normalized to `Value=null`. Google Scholar `MetricsSinceYear` is also invalid outside year 1 through the UTC computation year.

Each provider block includes field-specific `FieldDefinitions` because the saved values do not all have the same provenance:

- OpenAlex author totals and summary statistics are values saved from its author record.
- Google Scholar citation, h-index, i10-index, and recent-period values are profile-reported; `DocumentCount` is the number of collected works with distinct CitationId values.
- Web of Science values were derived during collection from the de-duplicated WOS/WOK query result set. Citation count uses WOK, then WOS, then the first available saved count for each collected work. Its document count and h-index describe that collected query set and are not official whole-profile totals.

`SourceUpdatedAt` is the local UTC collection timestamp for the saved provider values. It is distinct from the snapshot's `ComputedAt` and is not a provider revision timestamp. The same normalized values are stored in typed `analysis.PublicationMetricProviderSnapshots` columns for SQL reporting; field origin, scope, period, and quality remain in `FieldMetadataJson`. Parent and child rows are inserted atomically and remain immutable with their parent snapshot history.

Normal researcher collection persists provider profiles and then runs canonical reconciliation in the same transaction and write gate. That reconciliation schedules a new metric revision even when the collected canonical works did not change, so provider-only changes refresh automatically. Direct SQL maintenance is outside that collection path and must call the explicit refresh action.

OpenAlex author totals collected before migration `202609110007` may contain a legacy zero where the upstream field was absent. Recollect that profile to recover missing-versus-zero provenance; this migration does not infer or rewrite historical raw data.
Downgrading migration `202609110007` restores the old nonnullable OpenAlex columns by converting null totals to zero, which is a deliberately lossy developer/test rollback.

## OpenAlex research context and normalization

V4 uses the collector-owned `core.AcademicWorkResearchContexts` and `core.AcademicWorkTopics` rows for the requested researcher's own current OpenAlex observations. Collector canonical reconciliation performs the SQL-only provider-payload normalization inside its transaction and write gate. The Analysis publication-metrics worker reads the normalized context through private read-only source models; it never updates collector rows. The stored read action never parses, schedules, or writes. Other providers have no research-context adapter in this version.

The normalized fields follow the official OpenAlex documentation for [work attributes](https://help.openalex.org/data/works/attributes/), [citation indicators](https://help.openalex.org/data/works/citations/), and [topics](https://help.openalex.org/data/topics/).

The normalized context records provider/source work identity, parser version, payload fingerprint, local `SourceSyncedAt`, optional provider `updated_date`, source publication year, raw OpenAlex type, primary source type, FWCI, citation-normalized percentile, nullable top-one/top-ten flags, and explicit quality metadata. A saved payload identity that conflicts with its owning `AcademicWork` is rejected as a context conflict. Parsing is bounded to 4 MB and bounded JSON depth, topic count, and SQL field lengths; missing, malformed, unsupported, or invalid fields are recorded without failing the entire researcher refresh.

Topics use their OpenAlex IDs as keys. Names are labels only. Array order is retained as `OriginalRank`; a topic is primary only when `primary_topic` agrees with the raw first `topics` entry and their supplied hierarchy IDs do not conflict. Secondary topics never inflate primary-topic totals. Assignment score is retained when it is a finite representable number. OpenAlex does not document it as a probability or truth score, so this service treats it only as a provider ranking value.

`ContextualMetrics` uses all own in-scope canonical works as its eligibility denominator and returns separate coverage for a parsed OpenAlex context, resolved primary topic, resolved primary-subfield group, and resolved primary-field group. Missing and invalid contexts are explicit. The primary topic distribution and the subfield/field groups count each canonical work once. A group is keyed by classification ID, source publication year, raw OpenAlex type, and, for articles, primary source type such as journal or conference. Missing or invalid year/type/classification excludes the work from that group coverage. If multiple own OpenAlex observations disagree on primary identity, group identity, FWCI, or percentile, the relevant conflict count increases and no observation wins silently. Coarser field coverage may still resolve when finer subfield values are missing; conflict counters therefore describe their stated level and may overlap coverage gaps.

FWCI and citation-normalized percentile are labelled `ProviderReportedNormalization`. Percentile values are stored as fractions from 0 through 1 and are not multiplied by 100. Top-one/top-ten flags remain nullable and are never inferred from a missing percentile. Overall availability reports available, missing, invalid, and conflicting canonical-work counts with the eligible denominator. Each resolved descriptive group exposes `MeanFwci`, its available-value sum/count, and the group denominator; the mean is null when no nonconflicting provider value is available. These are descriptions of collected own works. The service does not calculate a world-relative score from the local cohort, and `InternalNormalizedScore` remains null with `ReferencePopulationUnavailable` until an evaluated reference population exists.
