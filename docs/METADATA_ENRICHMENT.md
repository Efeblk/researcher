# Article metadata enrichment

Article metadata enrichment recovers abstracts and trusted article-source candidates from
provider metadata before the article-summary workflow fetches full text. It does not add a
database table or column. OpenAlex and Crossref provider rows continue to retain their raw JSON,
and `AcademicWorkSynchronizer` reconstructs an abstract from that payload whenever normalized
works are created or refreshed. A refresh without an abstract does not erase a previously saved
normalized abstract.

Register the feature with the module's service collection:

```csharp
services.AddArticleMetadataEnrichment(configuration);
```

The public entry point is:

```csharp
Task<ArticleMetadataResult> EnrichAsync(
    string personelId,
    string? doi,
    CancellationToken cancellationToken = default);
```

`ArticleMetadataResult` contains the recovered `Abstract`, a bounded list of
`AcademicWorkSource` values, and a sanitized `Status`. Status is `InvalidDoi`, `NotFound`,
`Unavailable`, or `Enriched` with the providers that supplied evidence. Provider exception
messages, request URLs, API keys, and email addresses are not returned.
When `Unpaywall:Email` is absent or invalid, `;UnpaywallNotConfigured` is appended.

## Provider behavior

- OpenAlex lookups use `OpenAlex:ApiBaseUrl` and append `OpenAlex:ApiKey` when configured. Calls
  use the existing provider HTTP client and its durable pacing policy. The returned DOI must match
  the requested DOI.
- Crossref lookups use the existing `CrossrefClient`, including `Crossref:ApiBaseUrl`, optional
  `Crossref:Mailto`, response limits, and DOI identity validation.
- Unpaywall lookups run only when `Unpaywall:Email` is configured. Store the email in user secrets
  or another secure configuration provider; no personal email default is supplied. The default
  API root is `https://api.unpaywall.org`, and a trusted override can be set with
  `Unpaywall:ApiBaseUrl`. Both `url_for_pdf` and the landing URL are retained when present.

Provider base URLs must use HTTPS, except for loopback HTTP used in local testing. DOI input is
placed only in escaped path or query components and cannot change the configured provider host.
Responses, request duration, candidate counts, and JSON depth are bounded. Caller cancellation is
propagated.

## Configuration

All values are optional except the Unpaywall email when Unpaywall should be queried:

```json
{
  "ArticleMetadataEnrichment": {
    "PositiveCacheMinutes": 1440,
    "NegativeCacheMinutes": 60,
    "RequestTimeoutSeconds": 15,
    "MaximumResponseBytes": 4194304,
    "MaximumCandidates": 16
  },
  "Unpaywall": {
    "Email": "set-with-user-secrets"
  }
}
```

Successful metadata is cached by normalized DOI for the positive TTL. Definitive misses are
cached for the shorter negative TTL. Transport failures, timeouts, HTTP 429 responses, malformed
responses, and DOI mismatches are not cached, so a later request can retry them.

## Abstract parsing

`ArticleAbstractReader.FromPayload(payload, provider)` reconstructs OpenAlex
`abstract_inverted_index` tokens in their numeric positions. Repeated words at different
positions are retained. Nonnumeric or duplicate positions, gaps, malformed JSON, and content over
24,000 characters return no abstract.

Crossref JATS or HTML abstracts are reduced to text while paragraph boundaries are preserved.
Declarations that could define external entities are rejected and no XML resolver or network
fetch is used. Crossref abstracts over 24,000 characters also return no abstract.
