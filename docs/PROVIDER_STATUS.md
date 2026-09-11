# Provider status

`GET /Services/AcademicPerformance/V1/ProviderStatus` returns one compact row for each configured integration:
ORCID, SearchApi, OpenAlex, Web of Science, YÖKSİS, TR Dizin, Crossref, Unpaywall,
Semantic Scholar, AnalysisService, and Gemini. Each row contains only `provider`, `health`, and
`quotas`; quota entries contain only `limit`, `remaining`, `unit`, `period`, and `resetsAt`.
The Gemini row can additionally contain `spending`. Local request-budget counters and raw provider
responses are not part of this public contract.

```json
{
  "providers": [
    {
      "provider": "SearchApi",
      "health": "Reachable",
      "quotas": [
        {
          "limit": 100,
          "remaining": 70,
          "unit": "searches",
          "period": "monthly",
          "resetsAt": null
        }
      ]
    }
  ]
}
```

`health` uses ORCID's official reported health when it is current. For other providers,
`Reachable` means the transport succeeded and the expected response shape was validated. Failures
are represented as `Unavailable`, `Unauthorized`, `RateLimited`, `Disabled`, or `Unknown`. HTTP 200
means the collector produced a snapshot; it does not mean every provider is healthy.

The checks use small read-only requests and validate provider-specific response shapes. ORCID uses
its official status endpoint and coordinates the result in SQL Server so a deployment checks it at
most once every five minutes. TR Dizin looks up one fixed public ORCID. Crossref and Semantic
Scholar retrieve metadata for a fixed DOI, and Unpaywall does the same only when a valid
`Unpaywall:Email` is configured. Disabled providers and integrations missing required credentials do
not receive an upstream request.

Only quota values reported by a provider account endpoint or documented response headers are
returned. Crossref's documented
[`X-Rate-Limit-Limit` and `X-Rate-Limit-Interval` headers](https://www.crossref.org/documentation/retrieve-metadata/rest-api/access-and-authentication/)
describe the current request pool, so the response includes its limit and period while leaving `remaining` null. Semantic Scholar
does not expose a quota in this check. Application request limits and local counters are never used
to fill unknown provider quota values.

SearchApi's hourly remaining value is derived from the limit and usage returned by its account API.
OpenAlex's documented daily credit headers and Web of Science's per-key daily and per-second headers
are retained with their provider scope. A zero is a reported exhausted balance; null is unknown. An
empty quota list does not mean unlimited. Stale or failed observations expose no numeric values, and
subscription end dates or local midnight are not presented as provider reset times.

AnalysisService and Gemini are separate rows. `/health` checks only that the analysis host is
running. The collector obtains Gemini health from the analysis service's protected
`GET /api/v1/internal/provider-status/gemini` endpoint. That endpoint uses the analysis service's
existing `Gemini:ApiKey`, `Ai:ArticleProvider`, and `Ai:ArticleModel` settings to request
[model metadata](https://ai.google.dev/api/models) from `GET /v1beta/models/{model}`; it does not
generate content and returns no quota. Gemini's [rate limits](https://ai.google.dev/gemini-api/docs/rate-limits)
depend on project, model, and tier, so model metadata is not treated as a remaining balance.
The collector authenticates this internal call with its existing `AnalysisService:ApiKey` in the
`X-Analysis-Key` header. No Gemini credential is added to the collector.

Gemini `spending` is a SQL-backed Paid Tier Standard estimate:

```json
{
  "available": true,
  "currency": "USD",
  "kind": "paidStandardEstimate",
  "since": "2026-09-11T10:00:00Z",
  "requestCount": 3,
  "unknownCount": 1,
  "estimatedTotalUsd": null,
  "last3": [
    { "at": "2026-09-11T12:00:00Z", "model": "gemini-3.8-flash", "estimatedUsd": 0.001065 },
    { "at": "2026-09-11T11:00:00Z", "model": "gemini-3.8-flash", "estimatedUsd": null },
    { "at": "2026-09-11T10:00:00Z", "model": "gemini-3.8-flash", "estimatedUsd": 0.000825 }
  ]
}
```

The estimate follows Google's [Gemini 3.8 Flash pricing](https://ai.google.dev/gemini-api/docs/pricing):
through December 31, 2026, uncached input is $0.75/M tokens, cached input is $0.075/M,
and output including thinking is $3.75/M; from January 1, 2027 those prices are $1.50/M,
$0.15/M, and $7.50/M. Pricing support begins with the model's September 3, 2026 release and uses
`((prompt-cached)*input + cached*cachedPrice + (candidate+thought)*output)/1,000,000`.
Missing or inconsistent usage, unsupported models or dates, tool-use prompt tokens, and durable
Pending rows count as unknown. When any attempt is unknown, `estimatedTotalUsd` is null; no partial
known subtotal is exposed. This is an estimate for Paid Tier Standard pricing, not an invoice and
not a claim that a request used the paid rather than free tier. Google's
[rate-limit documentation](https://ai.google.dev/gemini-api/docs/rate-limits) explains that active
limits depend on the project and tier, so spending estimates are not presented as quota.

The analysis service reads this aggregate only from `analysis.GeminiUsageAttempts`. If SQL cannot be read,
`available` is false and the total is null. The collector caches the complete ProviderStatus snapshot
for 60 seconds, including spending, and returns at most the last three attempts ordered newest first.

Deploy compatible collector and analysis-service versions together when enabling the Gemini row.
Keep `AnalysisService:ApiKey` aligned with `Service:ApiKey` in the analysis service, and keep the
Gemini key only in analysis-service secret configuration. Configure
`ConnectionStrings:UsageDatabase` in analysis-service secrets to the same database as the collector's
`AcademicDatabase`; there is no default. Deploy and start the collector first so its migrations create
the table, then deploy or restart the analysis service. Tracking begins only after that migration and
the instrumented analysis service are running; historical Gemini calls are not backfilled. Provider
payloads, API keys, email query values, and transport exception details are not copied into the compact response.
The executable collector request remains in [`Requests/ProviderStatus.http`](../Requests/ProviderStatus.http).
