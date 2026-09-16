# Full-text resume pilot runner

This dated opt-in runner recorded the pre-separation collector-hosted full-text summary and staged article-review workflow against an isolated SQL Server database. Its 5097/5197 host topology and frozen artifacts remain historical evidence; the current persistent product routes are owned by Researcher Analysis Service at `/api/v1/...` on port 5011. It launches the analysis service in-process on `127.0.0.1:5097` and the already-built collector DLL on `127.0.0.1:5197`; it never uses `dotnet run` for either live host.

The offline preflight requires a source directory containing `adam-v1-fulltext.pdf` and `football-fulltext.pdf`:

```powershell
$env:FULLTEXT_PILOT_SOURCE_DIRECTORY = 'C:\path\to\pilot-pdfs'
dotnet .\PilotRunners\FullTextResumePilot\bin\Debug\net10.0\FullTextResumePilot.dll preflight
```

The files are obtained from these public URLs and are not committed:

- `https://arxiv.org/pdf/1412.6980v1` — SHA-256 `935a5a15616961aff21529d86a754570028843407adfe858f1d18584b84293a7`
- `https://livrepository.liverpool.ac.uk/3166141/1/Multiagent%20off-screen%20behavior%20prediction%20in%20football.pdf` — SHA-256 `9c3d977e50059edce06618dc5bc5454b393ff50d9539d2400aafb252cc4dae58`

Build and test the runner explicitly:

```powershell
dotnet test .\PilotRunners\FullTextResumePilot.Tests\FullTextResumePilot.Tests.csproj -p:SkipTSBuild=true
```

Run the acceptance preflight before release. It validates both PDFs and the exact historical citation-regression source selections without network access:

```powershell
dotnet .\PilotRunners\FullTextResumePilot\bin\Debug\net10.0\FullTextResumePilot.dll acceptance-preflight
```

These commands describe the frozen `fulltext-citation-alignment-pilot-20260912` run. `EnsureNewRun`
refuses to start when that run's artifact directory already exists. A new paid pilot requires a distinct
reviewed run ID and release value; do not overwrite or append to the frozen evidence.

Paid execution is refused unless both the `--execute-paid-pilot` argument and the exact root release value are supplied. The Gemini key is loaded in memory from the analysis service's user-secret ID using only `Gemini:ApiKey`; the runner does not print or export it.

```powershell
$env:FULLTEXT_CITATION_ALIGNMENT_PILOT_RELEASE = 'ROOT_RELEASED_FULLTEXT_CITATION_ALIGNMENT_20260912'
dotnet .\PilotRunners\FullTextResumePilot\bin\Debug\net10.0\FullTextResumePilot.dll acceptance-live --execute-paid-pilot
```

The live run writes only under `docs/fulltext-citation-alignment-pilot-20260912/`, including initial and per-phase snapshots, a 30-second budget heartbeat, the final result, and raw SQL report JSON captured before cleanup. Three required calibration calls replay the exact known citation mismatch before the two public-PDF summary and four-role review flows. A failed probe stops the PDF batch.

Live execution creates only a database named `AcademicFullTextResumePilot_<32 hex characters>`. Success stops owned processes and drops that exact database. Failure stops owned processes but preserves the database and artifacts for `acceptance-live --execute-paid-pilot --resume`. Resume refuses dispatch unless the restored 64-call/USD 2.00 ledger is complete, internally consistent, and agrees exactly with SQL usage; it also rechecks the cached probe profile fingerprint and prompt settings. The pre-send handler covers both the production typed Gemini client and the named evaluation client, permits only HTTPS `generativelanguage.googleapis.com` generation requests with redirects disabled, and enforces 64 calls and USD 2.00 across probes and article work.

The recorded acceptance recovery reopened an already completed preserved database, validated its saved
records, reads, usage, and cleanup state, and required the provider call and spend counters to remain
unchanged. It did not exercise a missing-stage restart and made no new provider call.

This is a two-article supported endpoint pilot with synthetic SQL seeds. It is not a `BulkSubmit` researcher collection run; all background workers and unrelated publication providers remain disabled.
