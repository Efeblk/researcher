# Researcher analysis service

`ResearcherAnalysisService` is a separate .NET 10 HTTP application in `AcademicCollectorDemo.sln`.
It accepts a caller-supplied researcher snapshot and returns a structured research
profile and abstract writing review. It runs without SQL Server, Serenity, or Node.js.

The collector creates snapshots from saved SQL Server records and stores successful
reports with their exact input snapshots. The AI service itself remains stateless.
There is no page report button, analysis queue, embedding index, web crawling,
full-text retrieval, or PDF upload in this version.

## Generate/save and retrieve by PersonelID

Use the requests in [AcademicPerformance.http](../Requests/AcademicPerformance.http):

| Collector endpoint | Behavior |
| --- | --- |
| `POST /Services/AcademicPerformance/V1/AnalyzeResearcher` | Load saved data, create a snapshot, call the AI service, and save a new report. |
| `POST /Services/AcademicPerformance/V1/GetResearcherAnalysis` | Return the latest saved report without calling the AI service or providers. |

Generation accepts `{ "PersonelID": "00123-A", "snapshotAt": "2026-09-08T00:00:00Z" }`.
Retrieval accepts `{ "PersonelID": "00123-A" }`.
The concise requests are in [ResearcherAnalysis.http](../Requests/ResearcherAnalysis.http).
`snapshotAt` is optional (defaults to capture time); supplied dates must be non-default
and no more than five minutes in the future. It labels the current saved-data snapshot,
not a historical query: the collector does not reconstruct records as of that date.
Both return `{ "Id": 123, "SavedAt": "...", "Report": { ... } }`.
Generation requires both applications and the configured model to be running.
Retrieval needs only the collector and SQL Server, and still works after a restart.
Collect fresh provider data separately with the existing `Collect` endpoint.

Each successful generation inserts a new `ResearcherAnalyses` row; older reports are
preserved. The row contains the exact snapshot and complete report, including model,
prompt version, generation time and coverage. Latest means highest saved report ID.
Snapshot time is the supplied date or the saved-data capture time; provider metric collection
timestamps remain separate. Failed generation never replaces a saved report.

Missing researchers/reports return 404; invalid IDs return 400; no usable publications
returns 422. Upstream failure returns 502 (with `AnalysisServiceStatus`), unavailable
service 503, and timeout 504. No automatic retries generate duplicate reports.

Collector settings are under `AnalysisService` in `academicsettings.json`:
`BaseUrl` defaults to `http://localhost:5011/`, `Language` to `en`,
`MaximumPublications` to 10, `MaximumTextBytes` to 2500, and `TimeoutSeconds` to 240.
If the AI service requires `Service:ApiKey`, set the matching collector
`AnalysisService:ApiKey` through user secrets. Use HTTPS for a remote service.

Selection uses deduplicated publication summaries, newest year first then ID. The
total count includes all saved summaries, while findings and activity describe only
the selected sample. Titles and whole abstracts/keywords must fit the UTF-8 byte
budget; oversized fields are omitted, never silently truncated. An abstract is attached
only through an unambiguous DOI or exact title/year match to a saved normalized work.
Coverage reports the selected count and available abstract count. The AI service may
still reject input exceeding its own model context limit; tune the collector limits
alongside that context. Turkish model output still needs quality review.

Migration `202609080001` creates the report table on collector startup. Contracts
shared by the two applications live in `ResearcherAnalysis.Contracts/`; the collector
references that library, not the AI web application.

## Run in Visual Studio

1. Open `AcademicCollectorDemo.sln`.
2. Right-click `ResearcherAnalysisService` and select **Set as Startup Project**.
3. Select the **http** launch profile and run. The browser opens `http://localhost:5011/health`.
4. To run the collector instead, select `AcademicCollectorDemo` as the startup project.
   Both applications can also be started using Visual Studio's multiple startup projects settings.

The analysis port is in `ResearcherAnalysisService/Properties/launchSettings.json` and
`ResearcherAnalysisService/appsettings.json`. Change both if 5011 is occupied.

From the repository root:

```powershell
dotnet run --project ResearcherAnalysisService --launch-profile http
```

Health only confirms that the HTTP service is running. Analysis uses a local Ollama
model by default; no paid AI API key or subscription is required.

## Local setup for an M2 Mac with 8 GB RAM

1. Install and open [Ollama](https://ollama.com/download/mac).
2. Download the configured small model once:

   ```sh
   ollama pull qwen3:1.7b
   ```

3. Run the analysis project and use the example request. Ollama must remain running;
   if its desktop app is not running, start the local server with `ollama serve`.

The checked-in settings are:

```json
{
  "Ai": {
    "Provider": "Ollama",
    "Model": "qwen3:1.7b",
    "OllamaBaseUrl": "http://localhost:11434/",
    "OllamaContextTokens": 8192,
    "TimeoutSeconds": 180,
    "MaxOutputTokens": 2000
  }
}
```

The [Qwen3 1.7B download](https://ollama.com/library/qwen3:1.7b) is approximately
1.4 GB; runtime memory also includes the context and model runtime. This is a starting
configuration for limited-memory hardware, not a measured quality or speed guarantee.
Begin with a few short abstracts and close memory-heavy applications. The adapter
requests `think: false` to disable Qwen3's extra reasoning output. It does not download
models or fall back to a paid/cloud provider. Only a loopback Ollama URL is accepted.
For strictly local operation, disable Ollama cloud features with `OLLAMA_NO_CLOUD=1`
in the Ollama server environment and restart Ollama.

The local adapter sends the report JSON schema with the request and uses the same
quotation/reference validation as the cloud adapter. It applies a conservative UTF-8
byte-based input allowance, reserving context for output and the chat template. Samples
that exceed this allowance return `422` before calling the model. This is intentionally
more restrictive than the general 60,000-character request limit. Reduce the sample;
only raise context size if the selected model and available memory support it.

The model receives publication IDs, titles, years, abstracts, keywords, language, and
the declared total count. Researcher name, department, DOI, and provider metrics are
not sent to the model. Provider metrics are returned unchanged in the report.

References: [Ollama local authentication](https://docs.ollama.com/api/authentication),
[structured outputs](https://docs.ollama.com/capabilities/structured-outputs),
[thinking controls](https://docs.ollama.com/capabilities/thinking), and
[server configuration](https://docs.ollama.com/faq).

## Optional paid provider

The OpenAI adapter remains available only when explicitly selected. To use it, set
`Ai:Provider` to `OpenAI` and configure `Ai:Model` and `Ai:ApiKey` in the new project's
**Manage User Secrets**. Choose a model supporting the Responses API with strict
JSON Schema output. These secrets are independent of the collector's secrets.
Production equivalents are `Ai__Provider`, `Ai__Model`, and `Ai__ApiKey`.

The OpenAI request sets `store: false`; this is not a claim of zero provider retention.
Implementation reference: [official Structured Outputs guide](https://developers.openai.com/api/docs/guides/structured-outputs).
Never put credentials in source control or `.http` examples.

## HTTP contract

The collector-facing examples in [Requests/ResearcherAnalysis.http](../Requests/ResearcherAnalysis.http) build this full internal request automatically.

`POST /api/v1/analyze` returns the completed report synchronously. `language` accepts
`en` (default) or `tr`. Findings and coverage notes follow that language; source quotes
remain verbatim. There is no automatic provider retry or stored job to resume.

Input rules:

- `PersonelID`, `researcherName`, and a non-default `snapshotAt` are required.
- Submit 1–100 already-deduplicated publications, with unique snapshot-local IDs,
  nonempty titles, and source names. Use `publication-<summaryId>` for collector summaries.
- `totalPublicationCount` is the number of unique publications in the intended scope,
  including any omitted from the submitted sample. It must be at least the sample size.
- Text is limited to 60,000 characters across titles, abstracts, and keywords; each
  abstract is limited to 12,000 characters. Oversized input is rejected, never silently truncated.
- `year`, `abstract`, `keywords`, and metrics may be null. Missing metrics stay null;
  zero remains an actual reported zero. Every metric retains its provider and collection date.
- The HTTP body limit is 512,000 bytes. Keep author-specific raw provider records,
  identity numbers, and credentials out of the snapshot.

The caller owns publication deduplication, researcher identity matching, source dates,
and sample selection. The service does not independently verify submitted records.
Keep the collector's separate OpenAlex comparison counts separate from the normalized
publication sample. Website display approval is not an analysis-permission decision.

The response contains:

| Field | Meaning |
| --- | --- |
| `schemaVersion`, `PersonelID`, `generatedAt` | Contract version and report identity |
| `model`, `promptVersion` | Actual provider model identifier and analysis instructions version |
| `findings.researchFocus` | Up to six supported research themes |
| `findings.writingObservations` | Up to six observations about supplied abstracts; can be empty |
| `activity` | Sample publication counts by year and category, calculated in C# |
| `citationMetrics` | Caller-provided provider metrics, kept separate |
| `coverage` | Snapshot date, total/sample counts, abstract coverage, missing years, and limitations |

Every finding contains `observation` and `evidence` entries with `publicationId`,
`field` (`title`, `abstract`, or `keywords`), and a verbatim `quote`. The service rejects
unknown IDs and quotes not present in the referenced field. Writing observations must
cite abstracts. These checks establish that cited text exists; they do not prove that
the model's interpretation is correct. Review the passages before relying on a finding.

Activity counts cover submitted publications only, and missing years are counted in
coverage rather than assigned a fabricated year. Provider citation metrics may cover
the entire profile and must not be presented as metrics for just the sample.

The analysis instructions exclude intelligence/personality scores, inferred emotional
states, AI authorship percentages, and misconduct judgments. Findings concern documents;
coauthored writing cannot be attributed to one person. An abstract is not a full paper.

## Access and failures

Local loopback requests in `Development` can run without a service access key. For
remote or non-development use, configure `Service:ApiKey` (`Service__ApiKey`) and send
it in the `X-Analysis-Key` header. This key is separate from the AI provider key. Use
HTTPS for remote service calls and keep the key on the collector backend, not in browser code.

| Status | Meaning |
| --- | --- |
| `200` | Completed report |
| `400` | Invalid snapshot; the model is not called |
| `401` | Missing or incorrect configured service access key |
| `413` | Request exceeds the HTTP body limit |
| `422` | Publication sample exceeds the configured local context allowance |
| `502` | Provider error, refusal, incomplete output, or invalid evidence. Invalid reports include a safe `reason` and `detail` in the response. |
| `503` | Model/provider configuration missing, Ollama unavailable/model not downloaded, or remote service access not configured |
| `504` | Provider request timed out |

`Ai:TimeoutSeconds` defaults to 180 and `Ai:MaxOutputTokens` to 2000. Caller cancellation
is forwarded to the provider request. Timeouts or invalid output do not trigger a second
paid request. Submitted text and provider response/error bodies are not logged by the service.

## Verification

```powershell
dotnet build AcademicCollectorDemo.sln --configuration Release
dotnet test ResearcherAnalysisService.Tests/ResearcherAnalysisService.Tests.csproj --configuration Release
```

Tests start the real HTTP pipeline on an ephemeral local port, inject a fake generator,
and test the provider adapter with synthetic HTTP responses. They use no SQL database,
developer secrets, or paid AI requests. Real-model quality and account/model availability
still require a separately configured evaluation with representative article samples.

For a `502` invalid report, inspect the HTTP response without attaching a debugger:
`OutputLimit` means generation hit the output token limit; try fewer publications first.
`InvalidJson` means the response failed JSON parsing or the required structure.
`QuoteMismatch` means a quote was not copied verbatim from the cited field;
`UnknownPublication` means a cited ID was not supplied. These reports remain rejected.
The same reason is logged as `AI analysis failed (InvalidAnalysisException, QuoteMismatch)`
(for example). Neither diagnostic includes submitted text or raw model output.

Ollama uses a request-specific structured-output schema with quote length (10–600),
evidence count (1–5), and observation count (up to 6) bounds. Observations must
be nonempty; their 2000-character maximum is enforced by the backend and prompt.
Publication IDs are limited to the supplied snapshot. Writing evidence must
use abstracts; snapshots without abstracts request an empty writing-observation array.
These constraints follow Ollama's [structured-output API](https://docs.ollama.com/capabilities/structured-outputs).
The backend still verifies quotes against source text and rejects invalid reports;
the schema does not establish that an observation is substantively correct. Ollama
reports now identify the prompt as `research-profile-v1-ollama-v3`.

A `502` with `reason: ProviderRequestFailed` means the provider request failed,
before a report could be validated. `providerStatus` contains the upstream HTTP
status when available (otherwise null); `detail` provides a safe explanation.
For Ollama failures, inspect its server logs for the actual runtime error.
The `type` URL ending in `section-15.6.3` is HTTP error documentation, not an
analysis endpoint or a diagnostic reason. Provider error bodies remain private.

Ollama 0.33.3 rejects the former `maxLength: 2000` observation schema with HTTP 400
(`Failed to initialize samplers: failed to parse grammar`). Its grammar parser
rejects the generated repetition complexity. The v3 Ollama schema omits that one
sampling bound while retaining backend validation and the quote constraints.

The JSON embedded in Ollama message text preserves Turkish letters rather than
turning them into literal Unicode escape sequences. Turkish requests also receive
an explicit instruction to write observations in Turkish; source quotes stay verbatim.
