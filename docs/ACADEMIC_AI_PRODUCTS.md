# Academic AI products

The Analysis Service product APIs at `/api/v1/...` expose two separate authorization and persistence domains over saved academic evidence.
They do not make personnel suitability or publication-quality decisions.

## Service access boundary

Protected product endpoints call `IAcademicProductAccessService` before looking up the subject or product record.
The default implementation fails closed: anonymous callers receive `401`, and an authenticated caller receives
`503` because no principal-to-scope or principal-to-`PersonelID` mapping is configured. The service/API deliverable
ends at this generic access boundary. A consuming deployment may supply a trusted adapter, including a BYS adapter,
without making BYS integration part of this project's completion criteria. Once an adapter is configured, a denial
should be returned as the same `404` used for a missing subject, so the endpoint does not become an identity oracle.

The access service issues an opaque authorization grant ID and a stable audit actor ID. The faculty worker stores
that server-issued grant when it enqueues a run and reauthorizes it immediately before execution. Request bodies
and headers never supply a trusted actor, role, or grant. Current canonical researcher associations remain required
in addition to product authorization. Collector exposes no duplicate product route. Runnable persistent HR/faculty requests
are in [AcademicAiProducts.http](../ResearcherAnalysisService/Requests/AcademicAiProducts.http).

## HR evidence dossiers

`/api/v1/hr/dossiers/create` captures saved facts only. It stores an immutable dossier, its policy version, an input
manifest, and a SHA-256 fingerprint covering the exact captured content. The dossier includes the chosen publication
metric snapshot with catalog definitions, formulas, denominators, coverage, conflicts, provider-specific scope and
quality metadata; current owner-scoped canonical observations; and the latest saved specialist review for each selected
work and language with exact immutable spans. It reports metric and article-review staleness at capture time.

The response omits the T.C. identity number, raw provider payloads, faculty private context, other personnel IDs, and
provider acquisition URLs. Retraction values remain provider observations. Exact citations establish where text was
found, not scientific correctness, completeness, quality, or personnel suitability.

`/api/v1/hr/dossiers/actions/append` supports only `Opened`, `NoteAdded`, `EvidenceQuestioned`, `FollowUpRequested`, and
`ReviewCompleted`. Actions are append-only and receive actor/time on the server. `ClientRequestId` makes an identical
retry idempotent; reusing it for different normalized content returns `409`. There are no hire, reject, rank, score,
quality, or suitability action types.

## Faculty context and assistant

Faculty context lives only in the `faculty` schema as immutable, owner-authorized versions with optimistic concurrency.
It is supplied only to a faculty assistant request and never enters the HR dossier or shared retrieval corpus.

`/api/v1/faculty/assistant/start` returns `202` after saving the exact authorized input and retrieval identity. Evidence search is
deterministic and owner-bound: only current valid latest canonical source spans are eligible, and every model-facing
evidence ID is globally scoped as `work:{work}:snapshot:{snapshot}:span:{span}`. The original document-local source ID,
offsets, exact text, extraction version, and hash remain separate provenance.

Each selected analysis reports `Current`, `Stale`, or `Unknown` freshness against saved provider/source metadata and the
currently configured analysis policy. `Current` does not prove that bytes at a remote URL are unchanged or real-time
fresh. The immutable corpus hash continues to identify saved source/run content; a separate freshness hash identifies the
ordered run, automation-job, and current-policy state. Legacy manifests without those identities remain explicitly unknown.

The pinned `academic-evidence-search-v4` catalog keeps direct source matching and the saved-claim bridge, then builds a
deterministic query plan. Turkish case, diacritic, and bounded suffix recognition identifies query intent; a bounded
English source-term expansion can retrieve source prose for that intent. Result diversification reserves condition or
limitation positions only for spans with explicit condition or limitation markers. `QueryHash` identifies the normalized
original query, while `QueryPlanHash` also pins the normalized tokens, detected intents, expanded source terms, and section
intents. Claim text remains a retrieval hint: the faculty analysis request receives only exact source-span text. Search
and enqueue make no model or provider call, and the authorized person and optional canonical-work scopes are unchanged.

This remains bounded, nonsemantic lexical recall rather than general semantic search, translation, or complete
morphological analysis. Missing summaries, omitted claims, wording outside the deterministic plan, and bounded candidate
truncation can still cause misses. Direct-source and claim-bridge coverage and truncation are reported separately, and
exact direct phrases remain protected from weaker intent matches.

The worker has `Pending`, `Running`, `Completed`, `Failed`, and `Interrupted` outcomes. It rechecks the server-issued
authorization grant and all current work associations before any model request. It fences completion by attempt token,
does not automatically retry a call whose remote cost may be unknown, and persists a bounded safe error instead of a
provider body. `/api/v1/faculty/assistant/run` rechecks owner authorization and current associations.
Its response includes the pinned retrieval policy, context version ID/version/fingerprint, corpus, query-plan,
claim-bridge and complete input hashes, direct and bridge coverage/truncation, and selected evidence provenance including
partial-source coverage and bridge claim/run/language IDs. The immutable saved manifest is reused on an idempotent replay.
Older manifests remain readable without invented newer-catalog metadata. The response does not expose the stored
authorized model input or private-context content.

Generation currently supports Gemini only and uses `Ai:ArticleModel`, whose default is `gemini-3.8-flash`; it never
uses the legacy researcher-profile `Ai:Provider`/`Ai:Model` route. Article summaries and the legacy one-shot review
abstraction can use Ollama; the durable Analysis Service review stage requires Gemini usage attestation and rejects a non-Gemini
configuration. The faculty-assistant endpoint returns `503 ProviderUnavailable` before any outbound request because no
Ollama faculty generator exists. `faculty-evidence-assistant-v10` follows the requested academic task within the fixed
mode, evidence, access, and output rules and produces at most six items. Methods include explanation, sourced limitations
or scope, and conditional controls. Teaching includes a ready-to-use exercise or scenario, worked guidance, and a distinct
discussion question. Issues uses sourced conditional rechecks and distinguishes uncertainty from an established error.

Every item cites one or two supplied global evidence IDs and exact source text. The
`faculty-evidence-assistant-verification-v7` verifier checks every candidate separately against only its own spans,
including each response premise and conditional branch. Known attributed output limits or invalid responses leave that
item unverified and omitted without retry. One `faculty-evidence-assistant-repair-v2` pass may replace the first two
source-checked unsupported or uncertain slots; supported items stay immutable and replacements are checked separately
without recursion. `faculty-request-coverage-v2` then checks the retained final items and returns up to eight ordered
requirements whose `ItemIndexes` are one-based. A response is `completed` only when source coverage is nonpartial and
every request requirement is fulfilled; otherwise supported items are returned honestly as `partial`.

Faculty generation defaults to one medium-thinking request. If it is explicitly configured to start at high and returns
an exact-model, priced, attributable `OutputLimit` whose usage was durably recorded, it makes one identical medium request
with the same 16,384-token ceiling. Every other failure stops without a generation retry. Fresh reports expose attempt IDs,
thinking levels, outcomes, model, tokens, cost, source checks, repair, and coverage; Analysis Service rejects inconsistent
provenance. Source verification independently defaults to medium. Generation, up to six initial checks, one repair, up to
two replacement checks, and final coverage use at most 11 dispatches by default or 12 with explicit high-generation
recovery. The per-call timeout is 180 seconds and the Analysis workflow's total outer bound defaults to 1,800 seconds; it does not
promise 11 complete maximum-length windows.
When no item survives source verification, deterministic request coverage is `unanswered`, the outcome is
`no_supported_items`, and no request-coverage call is made. If only the request-coverage call is unavailable, times out,
hits an output limit, or returns malformed output, source-supported items are preserved with `unavailable` request
coverage and a `partial` outcome; there is no automatic retry. Newly returned service responses require non-null request
coverage, while legacy reports already stored in SQL may deserialize a null value meaning unknown. The source verifier
and request-coverage checker report model-family and automatic-check limitations. They do not independently establish
scientific correctness, exhaustive publication or record coverage, complete recall, or that every omission was found.

## Service delivery acceptance

Delivery covers guarded single and bulk HTTP request handling, SQL-saved outputs and status, durable background jobs
for continuous operation, generic access checks, idempotency, exact evidence links, coverage metadata, and fail-closed
generation and verification. Faculty generation, its bounded output-limit recovery, and its separate verifier retain
exact `gemini-3.8-flash` routing. A web or BYS client may be built as an optional demo;
it is not required to complete the service/API deliverable and does not replace the access checks.

The default access adapter remains intentionally unconfigured, so this repository does not expose a generally usable
or unrestricted public API by itself. The product pilot used synthetic runner-only authorization and did not validate
BYS claims or identity mapping. Verified partial responses with explicit coverage and conditional wording for questions
about unknown user work are implemented offline. Representative Turkish/English retrieval evaluation remains a service
quality priority.

The retained V14 acceptance completed Methods, Teaching, and Issues with exact `gemini-3.8-flash`, prompt v8, verifier
v6, repair v1, cited evidence, request-coverage audits, SQL readback, idempotent replay, conflict checks, and zero unknown
usage. Its 26 fresh calls cost USD 0.219307500 in the local ledger. This demonstrates the frozen two-PDF sample and
recorded tasks; it does not establish scientific correctness, exhaustive recall, or BYS integration.
Automated tests must use synthetic sources, fake access grants, and fake analysis HTTP responses with the isolated SQL
fixture. They must not call live providers, use application database configuration, or spend money.
