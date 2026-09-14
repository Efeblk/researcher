# Product API Pilot Runner — 2026-09-13

This is the dedicated runner for the dated HR and faculty product API acceptance pilot. It is tied to the reviewed 2026-09-13 fixture, artifact hashes, pre-separation endpoint sequence, and retry history; it is not a general-purpose product runner. Current HR and faculty product routes are owned by Researcher Analysis Service at `/api/v1/products/[action]` on port 5011.

The pilot reuses immutable public Adam source artifacts and an isolated SQL Server database. It performs no new publication collection, download, extraction, summary, or article-review generation. Synthetic authorization and provider capture exist only inside this runner.

Run the offline guard checks with:

```powershell
dotnet run --project PilotRunners/ProductPilot/ProductPilot.csproj -- self-test
```

Run the initial offline preflight with:

```powershell
dotnet run --project PilotRunners/ProductPilot/ProductPilot.csproj -- preflight
```

The dated retest has its own offline preflight:

```powershell
dotnet run --project PilotRunners/ProductPilot/ProductPilot.csproj -- retest-preflight
```

Paid commands are locked. `live` and `retest3-live` refuse execution unless `--execute-paid-pilot` is present and the corresponding exact root-issued release environment value is set. The aggregate guard permits at most 16 Gemini calls and USD 1.00, including carried calls and cost from earlier failed attempts. Do not reset or subtract the carried ledger for a retest.

The runner preserves completed and failed artifacts with expected hashes, reconciles its budget ledger with isolated SQL usage, and refuses ambiguous or mismatched provider accounting. One preserved retest reused old client request IDs and correctly produced zero new dispatches; the later path requires fresh IDs and proves the SQL run is absent before worker execution.

Preflight and self-test do not authorize a paid run. A completed result is acceptable only when its artifact, model, citation, idempotency, usage, and SQL reconciliation guards all pass.

The dated pilot database was removed after the independent audit and explicit cleanup authorization. Existing artifacts remain readable and immutable, but `retest-preflight` can no longer operate against that removed database. Any future paid pilot requires a separately authorized new pilot lifecycle and must not reconstruct or resume this historical ledger automatically.
