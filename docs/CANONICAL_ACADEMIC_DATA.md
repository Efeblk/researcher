# Canonical academic data

The canonical layer links the existing provider-specific `AcademicWork` observations to a global work identity. `CanonicalWorks` owns publication identity. `PublicationSummaries` is the researcher-specific display projection: each rebuilt row has one `(PersonelID, CanonicalWorkId)` and derives its preferred metadata from that researcher's current observations. Display approvals remain attached to summary IDs.

## Identity and provenance

`core.CanonicalWorks` contains identity and lifecycle fields only. A syntactically valid DOI is normalized by removing known wrappers and lowercasing it, then used as the global identity. The database applies binary collation to the normalized DOI and enforces a filtered unique index.

An input without a valid DOI receives a hashed source-scoped identity based on the researcher, provider, and provider work ID. The `AcademicWork` ID is the fallback when the provider supplies no stable work ID. DOI-less records are never merged by title or year. A check constraint requires every canonical row to have exactly one of `NormalizedDoi` and `SourceScopedKey`.

`core.CanonicalWorkObservations` maps each current `AcademicWork` row to one canonical row and retains the provider-observed title, DOI, publication date/year, category, author string, publication, source metadata, version, license, and retraction flag. Author strings are provider observations; they are not asserted to be a complete or verified author list. URL records are metadata links and are not downloaded or immutable documents. The read API also follows the live `AcademicWorkSources` relationship so later source discovery is visible. Fields duplicated on the observation are the snapshot from the last canonical reconciliation; a rebuild refreshes them from `AcademicWork`.

`core.CanonicalResearcherWorks` records current researcher-to-work membership. Membership means that a current provider observation associated the work with the researcher; it is not a complete authorship assertion.

`HasRetractionObservation` means at least one current linked observation reports `IsRetracted=true`. It does not adjudicate publication status. Reconciliation recomputes it across all current observations while holding the canonical write gate, so a provider with a null flag cannot erase another provider's positive observation.

## Reconciliation

Every successful V1 `Collect` reconciles canonical links after normalized `AcademicWork` persistence and before `PublicationSummary` synchronization. Durable bulk jobs call that same collection service, so `Bulk/Submit` records are canonicalized when the default-enabled bulk worker processes them. YÖKSİS and Crossref refresh paths use the same sequence. The same collector transaction writes a durable `core.CollectionChanges` signal; Semantic Scholar writes another signal when it later adds a full-text source. Analysis Service consumes those records with idempotent `analysis.CollectionChangeReceipts` and schedules its own summary/metric work. Reconciliation removes stale observations and memberships only for the requested researcher, keeps other researchers' associations, and never deletes orphaned canonical identities.

All canonical writers take a transaction-owned SQL Server application lock named as the canonical write gate before changing normalized works. They then take the researcher lock and sorted identity locks. The gate serializes the short database persistence phase to avoid delete/rebuild lock inversion; provider network calls occur before the transaction and do not hold it. Unique database indexes remain the final identity guard. Application lock waits are bounded and every negative SQL result is treated as a failure.

The read-only `GetResearcher` and canonical list actions do not initiate collection or change stored data. During development, reset the disposable database and recollect after this identity-model change. The existing rebuild action remains available for saved-data repair; it performs no provider HTTP request and refreshes canonical membership and the summary projection in one transaction:

```text
POST /Services/AcademicPerformance/V1/RebuildCanonicalPublications
{ "PersonelID": "..." }
```

Summary IDs and approvals remain stable while `CanonicalWorkId` remains the same. A canonical identity change creates a new summary and requires a new display selection. DOI-less records from different providers keep their source-scoped canonical identities even when title and year match, so the displayed summary count may be higher than the old title/year grouping.

Normal individual and bulk collection do not require this repair action.

The paged read endpoint is:

```text
POST /Services/AcademicPerformance/V1/ListCanonicalPublications
{ "PersonelID": "...", "SearchText": "10.1234/example", "Skip": 0, "Take": 100 }
```

The response exposes only the requested researcher's observations. It may include the count of known researcher associations for the canonical work, but never returns other personnel IDs or provider payloads. Display metadata is selected deterministically from that researcher's current observations, preferring ORCID and then richer observations. `Take` is capped at 500 and ordering is stable.

Successful manual or automatic article summaries attach immutable source snapshots and model-checked claim evidence to the canonical work by logical ID in Analysis Service tables. Before saving, Analysis rechecks the work identity, deterministic source input, current association, and execution token against its read-only collector source view, so a refresh cannot attach stale input to a different publication or queue generation. There is no cross-service foreign key. Evidence remains historical if a provider observation is later replaced or removed, while product reads still require a current association. Collector only commits the change signal; the Analysis worker performs article acquisition and analysis after that transaction. See [Article summaries](ANALYSIS_PIPELINE.md).
