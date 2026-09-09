# Provider status and quota API

`GET /Services/AcademicPerformance/V1/ProviderStatus` returns a compact snapshot for ORCID,
SearchApi, OpenAlex, Web of Science, YÖKSİS, and AnalysisService. HTTP 200 means the
collector produced a snapshot; each provider has its own health result.

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

`health` is ORCID's current official reported health when available. For other providers,
`Reachable` means a current successful transport with the expected response shape. Failures
are reported as `Unavailable`, `Unauthorized`, `RateLimited`, or `Unknown`.

`quotas` contains only provider-reported account or API-key windows with verified scope,
unit, and period. An empty list means the quota is unknown. It never means unlimited.
`limit` can be present when a current provider response reports a limit but omits the
remaining balance. Stale and failed observations do not expose numeric values. The
collector's local SQL pacing budget is not part of this response.

SearchApi's hourly `remaining` value is calculated from its official limit minus official
usage. A zero means the observed window was exhausted; null means the value is unknown.
Cache age is not returned, and current observations are not guaranteed to be a spendable
real-time balance because another client may have consumed quota since the check.

Periods use names such as `hourly`, `daily`, and `monthly`. `resetsAt` is populated only
when the provider supplies a quota reset. A subscription end date and local midnight are
not treated as provider quota resets.

The endpoint sends no credentials, raw upstream fields, messages, reasons, local budget,
or cache timestamps. Its executable request is in
[`Requests/ProviderStatus.http`](../Requests/ProviderStatus.http).
