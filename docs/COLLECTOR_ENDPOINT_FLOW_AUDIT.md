# Collector endpoint flow audit

Audit date: 2026-09-15. Baseline: `4d7acee`.

## Acceptance result

- Single collection passed through the real collector HTTP host using an isolated, randomly named LocalDB database. The synthetic researcher had a cached Web of Science profile; the request normalized the researcher ID, saved the response, rebuilt canonical data, emitted collection changes, and returned the saved publication through the canonical list endpoint.
- A two-researcher bulk request passed end to end through real HTTP submit and status endpoints, the hosted bulk worker, normalization, persistence, canonical publication queries, and collection-change emission. Provider data was synthetic/cached and paid enrichment providers were disabled.
- Provider status passed at the HTTP boundary with nine synthetic upstream providers. Healthy quota mappings were preserved. Separate 429 and 401 responses mapped to `RateLimited` and `Unauthorized`, did not prevent other provider results, and did not expose internal `message`, `reason`, local-budget, or checked-at fields.
- General collector routes exercised successfully included `/`, `/AcademicPerformance`, the Serenity core script, researcher lookup, publication lists, canonical list/rebuild, publication-summary list, publication approval, and Semantic Scholar collection/citation reads.
- Invalid single-collection identifiers returned a structured error. Empty bulk submit/import identifiers, empty researcher batches, and unknown batch status requests now return HTTP 400 with actionable, bounded validation messages.

## Defect fixed

Bulk request validation previously threw ordinary `ArgumentException` instances. At the HTTP boundary Serenity treated these as unexpected failures and returned a generic error, so callers could not distinguish bad input or an unknown batch from a server fault.

Bulk input guards now throw `BulkRequestException`. The bulk endpoint converts only that typed, expected exception to Serenity `ValidationError`; unrelated exceptions remain private unexpected failures. Regression coverage verifies HTTP 400 and the stable messages for empty submit/import `BatchId` values, an empty researcher list, and an unknown batch.

Sanitized response shapes observed after the fix:

```json
{"Error":{"Message":"Supply a stable, non-empty BatchId for safe resubmission."}}
{"Error":{"Message":"Supply between 1 and 10000 researchers."}}
{"Error":{"Message":"Batch not found."}}
```

The configured maximum batch size supplies the numeric value in the second response.

## Verification

- Targeted baseline endpoint run: 6 passed, 0 failed. This included the real-host single and two-researcher bulk flows, provider status HTTP/mixed outcomes, and two Semantic Scholar endpoint tests selected by the filter.
- New targeted provider-status and bulk-error regressions: 3 passed, 0 failed after implementation.
- Full collector suite: `dotnet test AcademicCollectorDemo.Tests/AcademicCollectorDemo.Tests.csproj --no-restore` — 242 passed, 0 failed, 0 skipped after the final change.
- `npm run typecheck` passed.
- `npm test` — 9 passed, 0 failed, 0 skipped.

## Limits

No live paid provider was called, no private application database was read, and no schema was changed. External provider success/error behavior used deterministic synthetic HTTP responses or cached synthetic records. The single-collection flow therefore verifies the collector HTTP/application/persistence path but does not constitute a live upstream Web of Science smoke test. Analysis Service behavior was outside this collector-only audit.
