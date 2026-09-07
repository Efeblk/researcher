# Researcher analysis service

`ResearcherAnalysisService` is a separate .NET 10 HTTP application in `AcademicCollectorDemo.sln`.
It accepts a caller-supplied researcher snapshot and returns a structured research
profile and abstract writing review. It runs without SQL Server, Serenity, or Node.js.

This first slice provides the service contract and provider integration. The collector
does not call it yet, and the existing page has no report button. A collector snapshot
adapter and report viewer are the next integration step. There is no report persistence,
queue, embedding index, web crawling, full-text retrieval, or PDF upload in this version.

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

Health only confirms that the HTTP service is running. Analysis also requires an AI
API key and a model that supports the Responses API with strict JSON Schema output.
The model is deliberately unset; choose an available model for your account and budget.

Right-click **ResearcherAnalysisService → Manage User Secrets** and set:

```json
{
  "Ai": {
    "ApiKey": "YOUR_API_KEY",
    "Model": "YOUR_MODEL_ID"
  }
}
```

These secrets belong to the new project, independently of the collector's secrets.
Production equivalents are `Ai__ApiKey` and `Ai__Model` environment variables.
Never put credentials in source control or `.http` examples.

The first provider adapter uses the OpenAI Responses API with strict structured output.
It sends publication IDs, titles, years, abstracts, keywords, report language, and the
declared total count. Researcher name, department, DOI, and provider metrics are not
sent to the model. Provider metrics are returned unchanged in the report.
The request sets `store: false`; this is not a claim of zero provider retention.
Implementation reference: [official Structured Outputs guide](https://developers.openai.com/api/docs/guides/structured-outputs).

## HTTP contract

Use the synthetic example in [Requests/ResearcherAnalysis.http](../Requests/ResearcherAnalysis.http).

`POST /api/v1/analyze` returns the completed report synchronously. `language` accepts
`en` (default) or `tr`. Findings and coverage notes follow that language; source quotes
remain verbatim. There is no automatic provider retry or stored job to resume.

Input rules:

- `researcherId`, `researcherName`, and a non-default `snapshotAt` are required.
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
| `schemaVersion`, `researcherId`, `generatedAt` | Contract version and report identity |
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
| `502` | Provider error, refusal, incomplete output, or invalid evidence |
| `503` | AI configuration missing, or remote service access not configured |
| `504` | Provider request timed out |

`Ai:TimeoutSeconds` defaults to 90 and `Ai:MaxOutputTokens` to 4000. Caller cancellation
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
