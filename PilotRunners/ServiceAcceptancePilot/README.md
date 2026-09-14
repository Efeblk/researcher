# Service acceptance runner

This is the dated opt-in acceptance lifecycle for `service-acceptance-20260913-v1`. Its recorded collector/product orchestration reflects the pre-separation host topology; current persistent product routes, persistence, and workers are owned by Researcher Analysis Service at `/api/v1/products/[action]`. The runner does not execute or mutate the historical ProductPilot or FullTextResumePilot lifecycles or their artifacts.

Run the offline checks first:

```powershell
dotnet run --project PilotRunners/ServiceAcceptancePilot -- self-test
dotnet run --project PilotRunners/ServiceAcceptancePilot -- preflight
dotnet run --project PilotRunners/ServiceAcceptancePilot -- rehearse
```

Preflight performs no external network requests. It hashes and freshly extracts the two saved public PDFs, validates required source and schema coverage, checks only whether `Gemini:ApiKey` exists in the analysis-service user secrets, and writes a sanitized report to `docs/service-acceptance-20260913-v1/preflight.json`.

Rehearsal creates and migrates an isolated SQL Server database, submits a real bulk API request, disposes and rebuilds the in-process collector host, executes the normal collection handler through the ORCID HTTP DTO adapter, and checks the two saved canonical works and pending automatic-summary jobs. This is a host-lifetime restart, not an OS-process crash or soak test. The ORCID-only metadata replay is expected to finish `Partial` because OpenAlex is intentionally disabled; both replayed works must still be saved and canonicalized.

It then uses a typed synthetic internal analysis host to exercise automatic summary persistence, current metric refresh/readback, HR dossier create/read parity, faculty context persistence, both queued faculty modes, completed readback, and duplicate request replay with zero additional analysis calls. The synthetic rehearsal never invokes Gemini and its calls are separate from the live budget ledger.

Paid execution remains locked unless both the reviewed argument and coordinator release value are present:

```powershell
$env:SERVICE_ACCEPTANCE_RELEASE = 'ROOT_RELEASED_SERVICE_ACCEPTANCE_20260913_V1'
dotnet run --project PilotRunners/ServiceAcceptancePilot -- live --execute-paid-acceptance
```

The live ledger is aggregate across every attempt of this run. It reserves before dispatch, permits at most 32 exact `gemini-3.8-flash` calls and USD 1.00 estimated spend, and permanently stops after incomplete, unknown, mismatched-model, or unreliable usage. A failed ledger is never reset.

Successful live execution stops its owned hosts and preserves the isolated database for root audit. The separately release-locked `cleanup` command drops only the exact database recorded by this run after that audit.
