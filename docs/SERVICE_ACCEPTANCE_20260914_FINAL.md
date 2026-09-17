# Final service acceptance — 14 September 2026

The backend academic AI service passed the released composed acceptance boundary. The original acceptance exercised two
public PDFs; the broader held-out lifecycle added two public social-science papers, all five faculty modes,
bilingual requests, and an unsupported-query abstention. Earlier failed and nonpassing runs remain immutable evidence of
the defects they exposed.

## Broader held-out result

The broader lifecycle collected *A Fine Is a Price* and *Estimating the reproducibility of psychological science* for two
synthetic researchers through the normal bulk path. Hash-pinned PDF bytes passed through the production fetcher and
extractor, while synthetic provider metadata kept collection deterministic. Two canonical works, three researcher-work
associations, and three observations were saved; the shared DOI resolved to one canonical work. Gemini was the only live
provider.

The six fixed faculty requests passed as a composed result:

- `ExploreOwnRecord`, `OwnPaperMethods`, and `OwnPaperIssues` retain their accepted generation-v8 reports.
- `TeachingHelp` retains its accepted generation-v9 report with a clearly labelled hypothetical learner exercise,
  worked guidance, and a separate discussion question.
- `RelatedWorks` passed under generation v10 with four source-supported pairwise method comparisons and explicit
  conditional, selected-record scope. It makes no universal, exhaustive, or inferred citation-link claim.
- The Fine-paper BERT/GPU request returned deterministic `422` without an AI call or invented model fact.

The current implementation uses `faculty-evidence-assistant-v10`,
`faculty-evidence-assistant-verification-v7`, `faculty-evidence-assistant-repair-v2`, and
`faculty-request-coverage-v2`. Historical accepted v8 and v9 reports remain exactly readable and are not described as
fresh v10 generations.

The broader database contains 75 exactly attributed Gemini calls costing USD 0.748917750, with zero unknown usage. The
final RelatedWorks run used six calls and USD 0.041727750. Independent review matched those six request/response captures
to six SQL rows, exact `gemini-3.8-flash`, `STOP`, HTTP 200, thinking levels, tokens, pricing, attempt IDs, citations,
readback, and replay. It recomputed all 21 final manifest entries without mismatch and confirmed owned ports 5135 and
5235 were closed. The final manifest hash is
`ca5ea85d933d6e3ae7a43378ddf2f1908696b4e3ced0c9d067c1b887da026588`.

The Fine extractor retained 17 text-bearing pages from an 18-page PDF and conservatively marked the source partial;
independent rendering found the unread eighteenth page visually blank. The reproducibility PDF retained all 10 physical
pages, including a journal-summary first page and publisher-tooling last page. Source review passed the retained claims
with one stated qualification: the reproducibility summary's 97% significant headline is faithfully quoted, while the
article body notes that 4 of those 97 original results had `P > .05`.

The fixed comparison queries initially exposed methods evidence being displaced by incidental bibliography and footnote
matches. Retrieval catalog `academic-evidence-search-v4` now preserves exact-phrase priority and reserves verified
section-intent evidence across explicitly selected works. The unchanged two-paper queries then retrieved both core
methods spans first. Global candidate caps still limit large work sets, so this is bounded recall rather than exhaustive
semantic retrieval.

## Prior V14 result

V14 established the final original-sample Methods, Teaching, and Issues behavior under generation v8, verification v6,
repair v1, and coverage v1. Methods retained an Adam explanation, non-convex limitation, and conditional empirical
control. Teaching retained a complete hypothetical missing-trajectory exercise and discussion question after one repair.
Issues retained supported conditional rechecks and distinguished uncertainty from an established error. Five singleton
verifier regressions correctly separated qualified from unsupported theoretical and empirical claims.

V14 made 26 calls, used 107,686 tokens, and cost USD 0.219307500. Its retained database finished with 104 calls and USD
1.834842750. The immutable 125-entry V14 manifest has file hash
`1b9ff4dd992feaf50ecb5f8f72f0eb5fc49d44434bd5beb9e2315ce16f207b7e`.

## Validation and accounting

- Collector: 463 tests passed.
- Analysis service: 279 tests passed.
- Browser JavaScript: 9 tests passed.
- TypeScript type checking passed.

The 751 automated tests used fake providers and isolated SQL and made no paid calls. Across the earlier acceptance work
and the broader held-out lifecycle, known local usage is 253 calls and USD 3.917878500. The older 13 September unknown
reservation of USD 0.06427125 remains separately disclosed. These are locally attributed usage figures, not a provider
account balance.

## Boundary

This acceptance covers service APIs, durable jobs, SQL persistence, exact evidence, bounded retrieval, summaries,
specialist review, descriptive metrics, HR dossier, all five faculty modes, bilingual requests, access isolation,
idempotency, and fail-closed usage accounting for four fixed public PDFs and synthetic personnel metadata. It does not
establish universal scientific accuracy, exhaustive retrieval or omission recall, a production routing decision, institutional
identity mapping, UI completion, deployment readiness, live remote-URL byte freshness, or sustained unattended uptime. The
standalone product access adapter remains unconfigured and fails closed until a consuming deployment supplies trusted
scope and personnel mappings.

Published references: [broader final report](BROADER_SERVICE_ACCEPTANCE_20260914_FINAL.md) and
[product guide](ACADEMIC_AI_PRODUCTS.md). The machine-readable broader result and manifest, plus the V14 result,
remain in their local acceptance directories and are not published with the source tree.

The delivery worktree is `researcher-canonical-data` on `feature/canonical-academic-data`; the coordination checkout on
`main` was left unchanged.
