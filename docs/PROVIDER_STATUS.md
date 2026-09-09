# Provider status and quota API

`GET /Services/AcademicPerformance/V1/ProviderStatus` returns separate observations for
ORCID, SearchApi, OpenAlex, Web of Science, YÖKSİS, and AnalysisService. HTTP 200 means
the collector produced a snapshot; inspect each provider result independently. The
endpoint sends no credentials, request URLs, raw upstream bodies, or exception details
to clients. The executable request is in
[`Requests/ProviderStatus.http`](../Requests/ProviderStatus.http).

The legacy `Status`, `CheckedAt`, `HttpStatusCode`, `LatencyMilliseconds`,
`ProviderQuotas`, and `LocalBudget` fields remain available. Additive fields make their
meaning explicit:

- `Transport` records the collector-to-provider HTTP result.
- `ReportedHealth` records an official provider health assertion and its component
  booleans. It is currently populated only by ORCID.
- `QuotaAvailability` and `QuotaSource` state whether usable numeric quota evidence was
  returned and where it came from.
- `RemainingUsage` is the consumer-ready view. Its summary and per-window items distinguish
  `ProviderReported`, `Derived`, `Unknown`, `Unavailable`, and `Stale` values. Each item
  includes the unit, window, source, scope, observation/expiry/reset times, and a neutral
  explanation.
- quota observations include source fields, scope, value provenance, observation/reset/
  expiry times, and SearchApi subscription period end where supplied.
- `LocalBudget` remains the collector's SQL pacing and daily limit. It is never presented
  as the provider account balance.

An empty provider quota list means unknown, never unlimited. Missing, negative, or
wrong-type numeric fields are ignored. A zero is retained as a known exhausted value.
`RemainingUsage.Items[].Value` is null unless the value has a recognized provider source,
account or API-key scope, known unit and window, and a current observation. A present zero
means the window is exhausted; null means no verified current value. Partial numeric quota
data without a remaining value stays unknown. Generic Web of Science or YÖKSİS headers and
keyless OpenAlex observations do not become account balances. `LocalBudget` is never used
to populate this view.

The summary describes whether at least one item is usable; consumers must inspect every
item because one provider can expose several windows with different states. `Derived` is
currently used for SearchApi's hourly limit minus hourly usage. SearchApi subscription end
is retained only in the raw quota observation and is not a reset time. `Current` means the
observation is within its stated lifetime; another client sharing the account may consume
usage after `ObservedAt`.

## Provider checks

| Provider | Request | Interpretation |
| --- | --- | --- |
| ORCID | Public `/v3.0/pubStatus`, or Member `/v3.0/apiStatus` for `api.orcid.org` | `overallOk` and all three documented component booleans are required. `overallOk:false` is `Unhealthy`; missing/wrong-type fields are `UnexpectedResponse`. This is official service health, not account quota or token validation. |
| SearchApi | Bearer-authenticated `/api/v1/me` | Numeric account and hourly fields are official account data. Hourly remaining is marked derived when calculated from limit minus usage. `subscription.period_end` is retained separately and is not treated as every quota reset. Empty/invalid account data is unknown and unexpected. |
| OpenAlex | Bearer-authenticated `/rate-limit` when a key is configured | Only published `rate_limit.credits_limit`, `credits_used`, `credits_remaining`, and `resets_at` fields are selected. On header observations, the documented `X-RateLimit-Reset` seconds are converted relative to `ObservedAt`; invalid or overflowing values are ignored. The response's top-level `api_key` is never exposed. Missing/malformed numeric quota data is unknown and unexpected. |
| OpenAlex without key | `/works?per_page=1&select=id` | Reachability fallback only. Observed headers are not claimed as authenticated account quota. |
| Web of Science | Starter document request | Transport/reachability check. Generic rate-limit headers, if present, remain observed headers with unknown scope/unit unless their contract is established. |
| YÖKSİS | `?wsdl` | WSDL reachability only; SOAP authorization, operations, and account quota remain unverified. |
| AnalysisService | `/health` | Healthy only when the JSON `status` value is exactly `Running`. This does not test an AI model or AI-provider quota. |

## Caching and coordination

The complete response has a 60-second process cache. ORCID has a separate SQL-backed
deployment cache because ORCID asks clients not to check status more than once every
five minutes. Its cache key is derived from the configured status origin and case-
preserved path, so Public, Member, and custom test endpoints do not collide.
The service recomputes `RemainingUsage` against the current UTC time on every process-cache
read. A value becomes `Stale` with a null `Value` as soon as its observation expires or its
reset time elapses, even if the aggregate response remains cached.

The service obtains a SQL application lock and writes a five-minute `LocalCoordinationPending`
reservation using SQL Server UTC before sending the ORCID request. A cancelled or crashed
caller therefore cannot cause another instance to retry immediately. A completed,
sanitized observation replaces the reservation and expires five minutes after completion.
Corrupt or incomplete cached payloads fail closed until their database expiry. When SQL
coordination is unavailable, the result is `LocalCoordinationUnavailable`; it is not
reported as an ORCID outage. Cached ORCID results do not contain `LocalBudget`; the current
local counter is attached separately on every aggregate refresh.

The migration creates `ProviderStatusObservations`; deploy it before serving this
endpoint. ORCID SQL coordination plus its HTTP request has a 30-second local deadline,
and each provider HTTP request has a 15-second deadline. The API has no force-refresh
parameter.

`LocalBudget` fields retain their existing meanings: `DailyRequestLimit` is this
collector's configured cap, `RequestsToday` is its SQL-reserved attempts,
`RemainingToday` is their nonnegative difference, `MinimumIntervalMilliseconds` is its
pacing rule, `NextAllowedAt` is its next local opportunity, and `ResetsAt` is the next
UTC midnight. `Status` is `Available`, `Waiting`, `Exhausted`, or `Unavailable`.

The ORCID public and member endpoints were each observed once on 9 September 2026 with
HTTP 200 and boolean `tomcatUp`, `dbConnectionOk`, `readOnlyDbConnectionOk`, and
`overallOk` fields. Automated tests use synthetic responses only and make no live paid
provider calls.

OpenAlex's current documentation identifies `/rate-limit` and Bearer authentication. The
field mapping uses the published historical schema, but no authenticated OpenAlex account
was available for a live schema check in this change. Schema drift therefore produces an
explicit unknown/unexpected result rather than invented values.

An abbreviated synthetic result shows the separation (nullable fields may be omitted):

```json
{
  "Provider": "Orcid",
  "Status": "Healthy",
  "Transport": { "Status": "Healthy", "HttpStatusCode": 200 },
  "ReportedHealth": {
    "Status": "Healthy",
    "Source": "OfficialStatusApi",
    "OverallOk": true,
    "TomcatUp": true,
    "DbConnectionOk": true,
    "ReadOnlyDbConnectionOk": true
  },
  "QuotaAvailability": "Unknown",
  "RemainingUsage": {
    "Status": "Unknown",
    "Reason": "No verified current provider remaining balance is available.",
    "Items": []
  },
  "CacheScope": "SqlDeployment",
  "ProviderQuotas": [],
  "LocalBudget": { "Status": "Available", "RequestsToday": 12 }
}
```

A SearchApi result with synthetic values illustrates a reported exhausted monthly window
alongside a derived hourly window:

```json
{
  "Provider": "SearchApi",
  "RemainingUsage": {
    "Status": "ProviderReported",
    "Items": [
      { "Status": "ProviderReported", "Value": 0, "Unit": "searches", "Window": "month", "Source": "AccountApi", "Scope": "account" },
      { "Status": "Derived", "Value": 6, "Unit": "searches", "Window": "hour", "Source": "AccountApi", "Scope": "account" }
    ]
  }
}
```

## Sources

- [ORCID: How do I check the server status?](https://info.orcid.org/ufaqs/how-do-i-check-the-server-status/)
- [OpenAlex authentication](https://help.openalex.org/api/authentication/)
- [Published OpenAlex rate-limit response schema](https://raw.githubusercontent.com/ourresearch/openalex-docs/main/how-to-use-the-api/rate-limits-and-authentication.md)
- [SearchApi Account API](https://www.searchapi.io/docs/account-api)

The detailed Turkish source assessment and unresolved provider-contract questions are in
[`PROVIDER_OFFICIAL_DATA_RESEARCH.tr.md`](PROVIDER_OFFICIAL_DATA_RESEARCH.tr.md).
