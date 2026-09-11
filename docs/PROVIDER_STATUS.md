# Provider status

`GET /Services/AcademicPerformance/V1/ProviderStatus` returns one compact row for each configured integration:
ORCID, SearchApi, OpenAlex, Web of Science, YÖKSİS, TR Dizin, Crossref, Unpaywall,
Semantic Scholar, AnalysisService, and Gemini. Each row contains only `provider`, `health`, and
`quotas`; quota entries contain only `limit`, `remaining`, `unit`, `period`, and `resetsAt`.
Local SQL request counters and raw provider responses are not part of this public contract.

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

Deploy compatible collector and analysis-service versions together when enabling the Gemini row.
Keep `AnalysisService:ApiKey` aligned with `Service:ApiKey` in the analysis service, and keep the
Gemini key only in analysis-service secret configuration. Provider payloads, API keys, email query
values, and transport exception details are not copied into the compact response.
The executable collector request remains in [`Requests/ProviderStatus.http`](../Requests/ProviderStatus.http).
