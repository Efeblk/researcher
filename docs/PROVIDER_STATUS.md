# Provider status API

`GET /Services/AcademicPerformance/V1/ProviderStatus` checks the configured ORCID,
SearchApi (Google Scholar), OpenAlex, Web of Science, YOKSIS and researcher analysis service.
The endpoint returns HTTP 200 with individual results even when a dependency fails.
The collector must be running; it is not a replacement for host liveness monitoring.

Open [Requests/ProviderStatus.http](../Requests/ProviderStatus.http), edit the GET URL
if the collector is not running at `http://localhost:5001`, and send the request.
The file includes concise Turkish usage notes. Provider credentials are read from the
server configuration; no request body is needed.

## Behavior

Checks run concurrently, with a 15-second deadline per service. SQL budget reads have
an additional 5-second deadline. A process-wide cache retains the complete snapshot
for 60 seconds, including local budget values; `CheckedAt`, per-provider `CheckedAt`
and `ExpiresAt` make its age explicit. Concurrent callers share the refresh. There is
no forced-refresh option. Client cancellation propagates and cancelled refreshes are not cached.
Each application instance has its own cache; SQL pacing/budget coordination remains shared.

Provider probes use the collector's existing rate-limited HTTP client. A probe can
consume one request from the configured local daily budget and may consume provider
credits. Exhausted local budgets and long cooldowns prevent outgoing checks. Such a
result is `LocallyLimited`, not evidence of an upstream outage. All calls, including
checks, count toward the SQL budget. No researcher data is saved by this endpoint.

| Provider | Check | Provider quota source |
| --- | --- | --- |
| Orcid | `/search/?q=orcid&rows=0`, using the optional existing token | Recognized response headers when present |
| SearchApi | Account API `/api/v1/me` with existing API key | Monthly allowance, usage, remaining credits and hourly usage/limit |
| OpenAlex | `/works?per_page=1&select=id`, optional key sent as Bearer | Daily budget response headers, expressed as credits |
| WebOfScience | One document for `PY=1900` in WOS with existing key | Recognized response headers, preserving separate day/second windows |
| Yoksis | Existing service URL plus `?wsdl`, using configured Basic credentials | Recognized response headers when present |
| AnalysisService | Configured base URL plus `/health` | Unknown; no quota endpoint is assumed |

YOKSIS `Reachable` verifies a WSDL document only. It does not establish that SOAP
operations or account permissions work. AnalysisService checks the analysis host;
it does not call its underlying OpenAI/Ollama model or verify that model's quota.
No API keys, credential values, request URLs, upstream bodies or exception details
are included in the response. Missing required credentials produce `NotConfigured`.
URLs must use HTTPS or loopback HTTP. Redirects are disabled.

## Response fields

Each `Providers` item includes `Provider`, `Status`, `CheckKind`, `CheckedAt`,
`HttpStatusCode`, `LatencyMilliseconds`, `RetryAt`, `LocalBudget` and `ProviderQuotas`.
Statuses include `Healthy`, `Reachable`, `NotConfigured`, `Unauthorized`,
`RateLimited`, `LocallyLimited`, `Unavailable`, `LocalBudgetUnavailable`, `Timeout`
and `UnexpectedResponse`. A successful HTTP status with an unexpected document
shape is not reported as healthy.

`LocalBudget` describes **this collector's** shared SQL counters, not the subscription:

- `DailyRequestLimit`: configured cap; null means no local daily cap.
- `RequestsToday`: attempts reserved today, reset at UTC midnight, including failed calls.
- `RemainingToday`: cap minus attempts, floored at zero; null when uncapped or unavailable.
- `MinimumIntervalMilliseconds`: configured pacing.
- `NextAllowedAt`: future pacing/cooldown time, or at least the UTC reset when exhausted.
- `ResetsAt`: next UTC midnight.
- `Status`: `Available`, `Waiting`, `Exhausted` or `Unavailable`.

`ProviderQuotas` contains only reported numeric values. An empty list means **unknown**,
not unlimited. Entries identify `Source`, `Window`, `Unit`, `Limit`, `Used` and
`Remaining`; missing or malformed values remain null. Generic rate-limit headers
have an `unspecified` window; OpenAlex's documented generic headers describe a day.
The response deliberately separates a responsive service from its exhausted quota.

Example item (synthetic; null fields may be omitted by the host serializer):

```json
{
  "Provider": "SearchApi",
  "Status": "Healthy",
  "CheckKind": "AccountUsage",
  "HttpStatusCode": 200,
  "LocalBudget": {
    "Status": "Available",
    "MinimumIntervalMilliseconds": 1000,
    "DailyRequestLimit": 100,
    "RequestsToday": 12,
    "RemainingToday": 88,
    "ResetsAt": "2026-09-09T00:00:00Z"
  },
  "ProviderQuotas": [
    {"Source":"AccountApi","Window":"month","Unit":"searches","Limit":100,"Used":30,"Remaining":70}
  ]
}
```

No migration or new secret setting is required. Existing `ProviderRequestLimits`,
provider connection settings and `AnalysisService:BaseUrl` are reused. The endpoint
uses the same authorization conventions as the existing V1 endpoints.

## References

- [SearchApi Account API](https://www.searchapi.io/docs/account-api)
- [OpenAlex authentication and rate limits](https://help.openalex.org/api/authentication/)
- [ORCID search API](https://info.orcid.org/documentation/api-tutorials/api-tutorial-searching-the-orcid-registry/)
- [Web of Science Starter API](https://developer.clarivate.com/apis/wos-starter)

## Validation

`dotnet test AcademicCollectorDemo.Tests/AcademicCollectorDemo.Tests.csproj`,
`npm run typecheck`, and `npm test`. Tests use synthetic HTTP responses and the
isolated SQL Server fixture; no live provider account or paid API is called.
