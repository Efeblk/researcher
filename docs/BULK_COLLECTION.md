# Bulk collection worker

The bulk module accepts researcher rows, saves them in a SQL Server queue, and collects their data in the background. The web request returns a batch ID and progress instead of waiting for every provider call.

```text
JSON rows or configured SQL query
    -> validate and save batch/jobs
    -> background worker
    -> existing collection service
    -> provider HTTP limiter
    -> stored profiles, normalized works, publication summaries
```

## Input

Each row has a source identifier and any combination of the supported provider identifiers:

```json
{
  "BatchId": "a3700086-8ad7-4871-becf-0fbd2ce586e3",
  "Researchers": [
    {
      "SourceResearcherId": "employee-001",
      "WebOfScienceId": "A-1234-2020"
    }
  ]
}
```

`Orcid` and `GoogleScholarId` are optional. `WebOfScienceId` maps to the existing application's `WebOfScienceResearcherId`. ORCID collection also requests OpenAlex comparison data. This input does not include T.C. identity numbers or YÖKSİS bulk collection.

`SourceResearcherId` is an opaque identifier from the source system, not a name or national identity number. It lets callers match each result back to an input row. Provider identifiers still determine which local researcher is updated.

## API

Use the requests in [BulkCollection.http](../Requests/BulkCollection.http).

| POST endpoint under `/Services/AcademicPerformance/V1/Bulk/` | Purpose |
| --- | --- |
| `Submit` | Accept JSON rows and persist a batch. |
| `ImportSql` | Run the operator-configured source query and submit its rows. Body: `{ "BatchId": "..." }`. |
| `Status` | Return aggregate counts and a page of job results. Body: `{ "BatchId": "...", "Skip": 0, "Take": 100 }`. |

Generate one new `BatchId` for each new batch. Reusing a batch ID with identical JSON rows returns the existing batch; different input under that ID is rejected. Input ordering is part of this comparison. SQL imports should use a stable `ORDER BY` and a stable source snapshot if they need to be resubmitted.

The default maximum is 10,000 rows per batch and 500 job results per status page. Oversized SQL query results are rejected before any jobs are saved. Invalid provider identifiers and duplicate source IDs or identical normalized provider tuples within a batch become `Rejected` rows; valid rows continue. Different batches may intentionally collect the same researcher again, using the existing provider cache.

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
      "SourceResearcherId": "employee-001",
      "Status": "Pending",
      "Attempts": 0,
      "ResearcherId": null,
      "NextAttemptAt": "2026-09-06T00:00:00",
      "Message": null
    }
  ]
}
```

All queue timestamps are UTC. Status messages deliberately omit raw provider responses and credentials.

## Configure the SQL source

The importer supports SQL Server. Store a separate source connection string with a database account granted only the required `SELECT` permissions:

```powershell
dotnet user-secrets set "ConnectionStrings:BulkSource" "<read-only source connection string>"
```

The HTTP API never accepts SQL text. An operator configures `BulkSqlSource:Query`, for example:

```sql
SELECT EmployeeNumber, webofscienceID
FROM dbo.ResearcherExport
ORDER BY EmployeeNumber;
```

For that example, configure:

```json
{
  "BulkSqlSource": {
    "Enabled": true,
    "Query": "SELECT EmployeeNumber, webofscienceID FROM dbo.ResearcherExport ORDER BY EmployeeNumber",
    "SourceResearcherIdColumn": "EmployeeNumber",
    "WebOfScienceIdColumn": "webofscienceID"
  }
}
```

Column matching is case-insensitive. Missing ORCID or Google Scholar columns are allowed. At least one configured provider column must exist. If the source ID column is absent or null, the importer assigns `row-1`, `row-2`, and so on. Change the column settings when the production schema is known; no code change is required.

Importing is explicit: call `ImportSql` to create a batch. The worker polls the saved queue, not the source query. This avoids repeatedly importing the entire source table.

## Enable processing and configure provider speeds

Both the worker and SQL importer are disabled in committed defaults. Start with synthetic data, then enable the worker in deployment configuration:

```powershell
dotnet user-secrets set "BulkCollection:WorkerEnabled" "true"
```

Restart the host after changing worker or provider-limit settings. `Status.WorkerEnabled` shows the worker setting for the host answering the request.

Each provider has settings under `ProviderRequestLimits`: `Orcid`, `SearchApi`, `OpenAlex`, `WebOfScience`, and `Yoksis`.

| Setting | Meaning |
| --- | --- |
| `MinimumIntervalMilliseconds` | Minimum spacing between HTTP requests for that provider. |
| `DailyRequestLimit` | Application-side request cap per UTC day; `0` means no daily cap enforced by this application. |

The committed 1,000 ms interval is a starting configuration, **not a claim about any provider's subscription limits**. Set the interval and budget to your account's actual allowance before running bulk collections. Daily request counts cannot represent providers that charge different credit amounts per endpoint, and they do not include calls made by other applications outside this database.

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

A SQL session lock owns bulk processing. After a crash, another worker can acquire the lock and resume abandoned `Running` jobs, subject to the retry limit. Delivery is **at least once**: a crash after saving provider results but before recording job completion may repeat collection. Existing caches and synchronization reduce repeated calls and reconcile saved data; there is no exactly-once guarantee for external API requests.

The new migration adds `BulkCollectionBatches`, `BulkCollectionJobs`, and `ProviderRequestBudgets`. It does not change existing researcher or publication tables. Jobs remain available for auditing; automatic retention/deletion and a bulk management UI are not part of this version.

Production must apply the application's BYS authorization to these operational endpoints, as with the existing collection endpoints. The standalone host still uses its development permission service.

## Code locations

- `Service/Bulk/`: submission, job processing, queue models, and SQL import.
- `Background/BulkCollectionWorker.cs`: polls and runs queued work.
- `Service/Integrations/RateLimiting/`: shared HTTP pacing, budgets, and structured failure tracking.
- `Service/Api/V1/Contracts/Bulk*.cs`: public bulk API input and output.
- `Service/Api/V1/Endpoints/BulkCollectionEndpoint.cs`: HTTP entry points.
- `AcademicCollectorDemo.Tests/Integration/BulkCollectionTests.cs`: queue, recovery, SQL import, and API checks.
- `AcademicCollectorDemo.Tests/Integration/ProviderRateLimitTests.cs`: shared pacing, budgets, and cooldown checks with fake HTTP responses.
