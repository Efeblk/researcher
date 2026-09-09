# Bulk collection worker

The bulk module accepts researcher rows, saves them in a SQL Server queue, and collects their data in the background. The web request returns a batch ID and progress instead of waiting for every provider call.

```text
JSON rows from an external caller, or an optional configured SQL query
    -> validate and save batch/jobs
    -> background worker
    -> existing collection service
    -> provider HTTP limiter
    -> stored profiles, normalized works, publication summaries
```

## Input

Each row has a unique personnel identity and any combination of the supported provider identifiers:

```json
{
  "BatchId": "a3700086-8ad7-4871-becf-0fbd2ce586e3",
  "Researchers": [
    {
      "PersonelID": "employee-001",
      "ResearcherID": "A-1234-2020"
    }
  ]
}
```

`ORCID`, `ResearcherID` (Web of Science), `ScopusID`, and `ScholarID` match the personnel export column names. Provider IDs are optional, but at least one supported provider ID is required. ORCID collection also requests OpenAlex data and includes its works in the shared publication list. This input does not include T.C. identity numbers or YÖKSİS bulk collection.

Bulk cleanup and the single V1 `Collect` API use the same normalizer. It accepts canonical identifiers and narrowly recognized export forms: ORCID URLs on `orcid.org`, four space-separated ORCID groups, Web of Science author-record URLs, and Google Scholar profile URLs on `scholar.google.com`, `scholar.google.com.tr`, or `scholar.google.co.za`. Safe surrounding punctuation and Scholar tracking query parameters are removed. Final IDs must still pass the strict application parser. Cleanup never pads, truncates, guesses, moves a value between provider fields, or mines arbitrary text for an ID.

`NULL`, `0`, `.`, `-`, spreadsheet errors, malformed IDs, foreign URLs, ambiguous URLs with multiple provider candidates, and optional provider values over 4,096 characters are not used for collection. Other valid fields continue. The queue stores both the untouched original row and a separate canonical worker input, so cleanup never changes the imported source values. A row with no usable supported provider is `Rejected`. Field-specific `Warnings` remain available through `Status` after worker updates without echoing the original values. Google Scholar identifier case is preserved.

Scopus collection is unsupported. `ScopusID` is retained in the original queued row and produces a warning, but it creates no provider call.

`PersonelID` is the institution's unique personnel identity. It is stored on the researcher and used together with provider identifiers to find the same person. A request is rejected when supplied identifiers belong to different stored researchers.

## API

In production, the external caller queries its own source database and posts the generated JSON to `Submit` programmatically. This service does not query that source database.

For a local manual verification:

1. Run the read-only [BulkSubmitPayload.sql](../Requests/BulkSubmitPayload.sql) query against the caller-owned source database in SSMS.
2. Copy its single JSON result into the `Submit` request body in [BulkCollection.http](../Requests/BulkCollection.http).
3. Call `Status` with the same `BatchId` returned in that payload.

The query reads the five export columns from `dbo.PersonelTest`, orders rows by `PersonelID`, and casts `PersonelID` to a JSON string even when the source column is an integer. It preserves raw whitespace and emits SQL `NULL` values as JSON `null`. `JSON_QUERY` keeps `Researchers` as an array instead of an escaped JSON string. The checked-in query uses `TOP (10)` for a safe first run; remove it to submit all 2,980 rows. The API accepts at most 10,000 rows per batch. For the full batch, make sure SSMS displays and copies the complete `nvarchar(max)` cell; a truncated result is not a valid request payload.

Generate the payload once and keep that exact payload and `BatchId` for a retry. Running the SQL again calls `NEWID()` and creates a different batch. Reusing a batch ID with identical JSON rows returns the existing batch; different input under that ID is rejected. Input ordering is part of this comparison.

The service does not need a connection string, account, or user-secret for the caller's source database when using `Submit`. Source access and extraction remain entirely the caller's responsibility. The service still needs its own application database configuration and any provider credentials required for collection.

| POST endpoint under `/Services/AcademicPerformance/V1/Bulk/` | Purpose |
| --- | --- |
| `Submit` | Accept JSON rows and persist a batch. |
| `ImportSql` | Run the operator-configured source query and submit its rows. Body: `{ "BatchId": "..." }`. |
| `Status` | Return aggregate counts and a page of job results. Body: `{ "BatchId": "...", "Skip": 0, "Take": 100 }`. |

The default maximum is 10,000 rows per batch and 500 job results per status page. Oversized SQL query results are rejected before any jobs are saved. A row is `Rejected` when cleanup leaves no usable provider identifier. Duplicate `PersonelID` values are also rejected. If any normalized ORCID, Scholar ID, or Web of Science ID is shared by different personnel in one batch, every involved row is rejected for manual review. Other valid rows continue. Different batches may intentionally collect the same researcher again, using the existing provider cache.

Example initial status (job IDs and timestamps vary):

```json
{
  "BatchId": "a3700086-8ad7-4871-becf-0fbd2ce586e3",
  "WorkerEnabled": false,
  "IsComplete": false,
  "Counts": { "Pending": 1 },
  "Jobs": [
    {
      "Id": 1,
      "PersonelID": "employee-001",
      "Status": "Pending",
      "Attempts": 0,
      "NextAttemptAt": "2026-09-06T00:00:00",
      "Message": null,
      "Warnings": []
    }
  ]
}
```

All queue timestamps are UTC. Status messages deliberately omit raw provider responses and credentials.

## Optional: configure server-side SQL import

`ImportSql` is an alternative for deployments where this service is intentionally allowed to connect to the source SQL Server. It is not required for the primary external-caller `Submit` flow. Store a separate source connection string with a database account granted only the required `SELECT` permissions:

```powershell
dotnet user-secrets set "ConnectionStrings:BulkSource" "<read-only source connection string>"
```

The HTTP API never accepts SQL text. An operator configures `BulkSqlSource:Query`, for example:

```sql
SELECT PersonelID, ORCID, ResearcherID, ScopusID, ScholarID
FROM <operator-owned personnel table>
ORDER BY PersonelID;
```

The committed host profile is ready for the supplied personnel-export schema while remaining disabled until its query and connection are configured:

```json
{
  "BulkSqlSource": {
    "Enabled": false,
    "Query": "",
    "PersonelIdColumn": "PersonelID",
    "OrcidColumn": "ORCID",
    "WebOfScienceIdColumn": "ResearcherID",
    "GoogleScholarIdColumn": "ScholarID",
    "ScopusIdColumn": "ScopusID"
  }
}
```

Here `ResearcherID` means the Web of Science identifier. `ScopusID` is preserved in the audit envelope and stored as metadata, but it is not collected. Set `Query` to a simple operator-owned `SELECT` with a stable `ORDER BY`; SQL is never accepted from the HTTP request. Column matching is case-insensitive. The configured `PersonelID` column is required and every row must contain a nonblank value. Missing ORCID or ScholarID columns are allowed. At least one configured supported-provider column must exist.

Importing is explicit: call `ImportSql` to create a batch. The worker polls the saved queue, not the source query. This avoids repeatedly importing the entire source table.

## Enable processing and configure provider speeds

Both the worker and SQL importer are disabled in committed defaults. To perform actual local collection, set `BulkCollection.WorkerEnabled` to `true` in `academicsettings.json`, then restart the host:

```json
"BulkCollection": {
  "WorkerEnabled": true
}
```

An existing `BulkCollection:WorkerEnabled` user-secret or another later configuration source can override the file value; update or remove that override if `Status.WorkerEnabled` remains `false`. Restart the host after changing worker or provider-limit settings. `Status.WorkerEnabled` shows the worker setting for the host answering the request. Enabling the worker does not add any source-database configuration requirement to `Submit`.

Each provider has settings under `ProviderRequestLimits`: `Orcid`, `SearchApi`, `OpenAlex`, `WebOfScience`, and `Yoksis`.

| Setting | Meaning |
| --- | --- |
| `MinimumIntervalMilliseconds` | Minimum spacing between HTTP requests for that provider. |
| `DailyRequestLimit` | Application-side request cap per UTC day; `0` means no daily cap enforced by this application. |
| `Enabled` | Whether this deployment may call the provider. Disabled providers make no HTTP request and do not trigger automatic retries on their own. |

Pacing, daily caps, and provider cooldowns are shared by application instances that use the same
SQL database. Transport failures and response-body failures establish a one-minute shared cooldown;
a longer provider `Retry-After` value remains authoritative even when reading the body later fails.
The next request is spaced from completion of the prior response, which is deliberately conservative.
Local cap and cooldown deferrals return to the durable queue without exhausting the job's retry
attempts. Coordination cannot account for other applications that share the same provider key or IP.

SearchApi collection is disabled by default because the available account quota is exhausted. Set
`ProviderRequestLimits:SearchApi:Enabled` to `true` only after verifying the active plan and setting
limits that fit its quota.

Defaults now follow the [repository provider reports](API_OZET_RAPORU.md), with spacing below the published ceilings. Official sources were rechecked on 6 September 2026.

| Provider | Default spacing | Application daily cap | Basis |
| --- | --- | --- | --- |
| ORCID | 100 ms (up to 10/s) | 25,000 | Public/anonymous ceiling is 12/s. Anonymous access allows 25,000 reads/day; registered public access allows 100,000. The default uses the lower allowance. |
| OpenAlex | 20 ms (up to 50/s) | 1,000 | Published ceiling is 100/s. Our current client uses filter queries; 1,000 such calls cost the documented $0.10 keyless daily budget. A free key allows a larger budget. |
| Web of Science | 250 ms (up to 4/s) | 5,000 | The project report confirms Free Institutional Member: 5/s and 5,000/day. |
| SearchApi | 2,000 ms (up to 1,800/hour) | No application daily cap | Conservative spacing below the documented Developer example's 2,000/hour. The purchased plan is still unconfirmed; this is not a claim about the account entitlement. |
| YÖKSİS | 1,000 ms (up to 1/s) | No application daily cap | Provider limits remain unverified; this is a placeholder. |

Sources: [ORCID limits](https://info.orcid.org/ufaqs/what-are-the-api-limits/), [OpenAlex authentication](https://help.openalex.org/api/authentication/), [OpenAlex costs](https://help.openalex.org/access/example-costs/), [WoS plans](https://developer.clarivate.com/apis/wos-starter), [SearchApi limits](https://www.searchapi.io/pricing).

Every spacing and cap remains adjustable in `academicsettings.json` or deployment overrides. These are maximum launch rates, not guaranteed throughput. Provider response time, SQL coordination, and sequential collection can make processing slower.

Raise ORCID's cap to 100,000 only when using registered public credentials. For OpenAlex's free-key budget, the current filter-only client can use a 10,000 daily request cap. Revisit this approximation if endpoint costs or query types change: request counts are not a general monetary-budget limiter. SearchApi enforces an hourly allowance of 20% of plan credits; smooth spacing does not track the remaining monthly or trial balance. Confirm that plan before bulk use. Requests from other applications are not included in our database counters.

Every HTTP request passes through the limiter, including pagination and detail requests. Interactive requests share the same budget. Different providers have separate pacing and cooldown state. Limits are coordinated through the application SQL database, so hosts using the same database share these budgets. Requests already represented by the existing fresh provider cache make no HTTP call.

The limiter honors `Retry-After` dates and durations on temporary provider failures and records a shared cooldown. Long waits and exhausted daily budgets return the job to the queue. Retryable failures use exponential backoff, with a default of three total attempts. Provider `Retry-After` takes precedence when it requires a longer wait. HTTP 4xx failures other than 408/429 are not automatically retried.

The HTTP handler approach follows [Microsoft's client-side rate-limiting guidance](https://learn.microsoft.com/en-us/dotnet/core/extensions/http-ratelimiter). Distributed ownership uses [SQL Server application locks](https://learn.microsoft.com/en-us/sql/relational-databases/system-stored-procedures/sp-getapplock-transact-sql).

## Progress and recovery

| Status | Meaning |
| --- | --- |
| `Pending` | Saved and waiting for a worker. |
| `Running` | A worker is collecting this researcher. |
| `RetryWaiting` | A temporary failure or cooldown scheduled another attempt. |
| `Succeeded` | Collection completed without reported errors. |
| `Partial` | Some data was saved, but collection remains incomplete. |
| `Failed` | Collection did not complete successfully. |
| `Rejected` | Invalid or duplicate input. |

The first version processes one researcher at a time across bulk workers, reusing the existing multi-provider collection workflow. It does not yet run independent parallel provider queues. This limits concurrent writes to the same researcher and is a straightforward starting point; throughput is also constrained by each researcher's collection time.

Each status row includes UTC `StartedAt` and `CompletedAt` timestamps. A researcher with many provider pages can remain `Running` for several minutes; bulk-only logs report the job attempt and elapsed time plus safe provider names, request ordinals, HTTP status codes, and cooldown durations without logging identifiers or request URLs. The request ordinal counts every provider HTTP request across the whole job attempt, so it is not a provider page number. Web of Science logs page progress separately for each WOS and WOK database request: the request log includes the requested page and configured maximum, while the parsed-response log also includes the provider-reported total and computed page count. `Pending` and `RetryWaiting` jobs resume when a worker is available. Terminal `Failed` jobs do not resume automatically.

A SQL session lock owns bulk processing. After a crash, another worker can acquire the lock and resume abandoned `Running` jobs, subject to the retry limit. Delivery is **at least once**: a crash after saving provider results but before recording job completion may repeat collection. Existing caches and synchronization reduce repeated calls and reconcile saved data; there is no exactly-once guarantee for external API requests.

The initial migrations now make the required institution `PersonelID` the sole `Researchers` primary key; there is no internal integer `Researchers.Id`. The nine direct researcher relationships (the four provider profiles, YÖKSİS records, academic works, publication summaries, publication approvals, and saved analyses) reference that same `PersonelID`. Provider profile, work, summary, approval, and analysis row IDs remain unchanged. Single-provider and YÖKSİS collection requests also require `PersonelID` before any provider call. The remaining personnel-export columns are `ORCID`, Web of Science `ResearcherID`, `ScopusID`, and `ScholarID`.

The migrations also create `BulkCollectionBatches`, `BulkCollectionJobs`, and `ProviderRequestBudgets`. Because these initial migrations were edited during the test phase, use a fresh application database when adopting this schema; do not delete an existing database or source personnel table as part of import. Jobs remain available for auditing; automatic retention/deletion and a bulk management UI are not part of this version.

Migration `202609090002` widens author metadata in `OrcidWorks`, `AcademicWorks`, and `PublicationSummaries` to lossless Unicode text. Apply migrations normally at application startup; this migration alters the application database in place and does not require deleting or resetting it.

If another bounded provider field exceeds its schema, single `Collect` returns `FailureCode: "PersistenceDataTooLong"`; the bulk job becomes terminal `Failed` and is not automatically retried.

Production must apply the application's BYS authorization to these operational endpoints, as with the existing collection endpoints. The standalone host still uses its development permission service.

## Code locations

- `Service/Bulk/`: submission, job processing, queue models, and SQL import.
- `Background/BulkCollectionWorker.cs`: polls and runs queued work.
- `Service/Integrations/RateLimiting/`: shared HTTP pacing, budgets, and structured failure tracking.
- `Service/Api/V1/Contracts/Bulk*.cs`: public bulk API input and output.
- `Service/Api/V1/Endpoints/BulkCollectionEndpoint.cs`: HTTP entry points.
- `AcademicCollectorDemo.Tests/Integration/BulkCollectionTests.cs`: queue, recovery, SQL import, and API checks.
- `AcademicCollectorDemo.Tests/Integration/ProviderRateLimitTests.cs`: shared pacing, budgets, and cooldown checks with fake HTTP responses.
