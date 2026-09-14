# Full-text resume pilot — 2026-09-12

The corrected operational result is a pass for the single Adam v1 full-text workflow. The frozen local `docs/fulltext-resume-pilot-20260912-result.json` retains `operationalSuccess: false` because its raw JSON comparison did not normalize all public-response and typed-SQL serialization shapes. The separate local `docs/fulltext-resume-pilot-20260912-corrected-audit.json` replays those values through the public contracts, normalizes casing and omitted optional nulls, and confirms that the generated review, saved SQL read, and cached repeat are semantically equal. These machine-readable artifacts remain outside source control. The raw artifact remains unchanged with SHA-256 `15FF877D2689BFB9F914194878B5A0BE471BBE4401F99F13DE841566375889C5`.

## Method and bounds

The opt-in runner used synthetic researcher/work associations, an isolated SQL Server database, explicit local host ports, and the staged Gemini article workflow. The report language was `tr`; every request asked for and received `gemini-3.8-flash`, with `high` generation and verifier thinking and 8,192 generation and verifier output tokens. Broad background workers and unrelated publication providers were disabled. A delegating handler admitted every outbound Gemini request atomically under one global ceiling of 32 calls and USD 1.00, using the exact serialized body, configured output limit, and the same pinned pricing as production. It stopped before dispatch if the conservative reservation could not fit. No secret is stored in the runner or artifacts.

The offline preflight made no provider or public-network request. Both already downloaded sources fit the unchanged source limit after correcting the earlier double accounting of pages and spans.

| Source | PDF SHA-256 | Pages | Spans | Counted source payload | Fits unchanged limit | Sampled eight review-stage reservation |
| --- | --- | ---: | ---: | ---: | --- | ---: |
| Adam v1, <https://arxiv.org/pdf/1412.6980v1> | `935a5a15616961aff21529d86a754570028843407adfe858f1d18584b84293a7` | 9 | 64 | 38,448 bytes | yes | USD 0.39877725 |
| Football, <https://livrepository.liverpool.ac.uk/3166141/1/Multiagent%20off-screen%20behavior%20prediction%20in%20football.pdf> | `9c3d977e50059edce06618dc5bc5454b393ff50d9539d2400aafb252cc4dae58` | 13 | 136 | 82,174 bytes | yes | USD 0.53948025 |

## Adam operational result

| Stage | HTTP result | Elapsed | Provider calls | Estimated cost | Total tokens | Retained output | Exact evidence links |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| Turkish summary | 200 | 142.615 s | 10 | USD 0.205703250 | 84,463 | 6 of 13 candidates supported; 7 uncertain omitted | 12 |
| Four-role review | 200 | 182.985 s | 12 | USD 0.285606750 | 130,985 | 7 of 12 candidates supported; 5 uncertain omitted | 11 |
| Total | — | — | 22 | USD 0.491310000 | 215,448 | all four review roles completed | 23 |

Four Gemini calls returned `OutputLimit`: two during summary verification and two during review verification. Adaptive splitting recovered all four without increasing the unchanged `high` thinking level or 8,192-token output limit. The review SQL work item contains four distinct completed generation roles and twelve correlated checkpoints; completed role generation was not repeated. All twelve review checkpoint attempt IDs and actual costs match the Gemini usage ledger.

The coordinating audit checked all 23 evidence links, representing 21 unique spans, against the same nine persisted source pages using .NET UTF-16 offsets and found zero quote, page, or offset mismatches. Summary and review rows, evidence rows, source snapshot, work item, and checkpoints were present before the isolated database was dropped. Saved summary, evidence, and review reads plus an ordinary repeated review request created zero new provider calls.

## Raw comparison correction

The raw harness flag and `savedAndReusedReviewReportsMatchSql: false` remain frozen. Public JSON, the stored report, and typed deserialization can differ in property-name casing and representation of omitted nullable properties. One observed example is three public `source_observation` findings that omit `Suggestion`, while typed SQL deserialization emits `suggestion: null`; both carry the same semantic null value. That example does not prove a single sole cause for the raw comparison failure. The corrected typed replay covers the relevant shapes and passed all 13 runner tests.

## Football budget decision

Football passed offline extraction, span-offset, source-limit, and exact serialized stage-quote preflight. After Adam, 10 calls and USD 0.508690 remained. The corrected budget audit confirms that the sampled eight football review-stage reservations alone required USD 0.53948025, with summary cost still additional. The runner therefore did not admit Football and the per-request outbound guard never received a Football call. The second live sample is budget-deferred rather than an operational failure or pass.

## Quality boundary

The reports truthfully label the input as provided PDF-page text while formula and table layout remains unverified and figure imagery remains unanalysed. This run did not add visual formula, table, or figure parsing.

A coordinating AI review found one concrete semantic citation concern: a retained Turkish purpose claim says Adam combines advantages of AdaGrad and RMSProp, but its two selected quote fragments do not themselves contain `RMSProp`; that support occurs in neighboring source text. Exact IDs and offsets therefore passed while the selected evidence was semantically incomplete for the full claim. This is an AI QA observation, not a human domain-expert judgment.

The pilot establishes one bounded operational full-text pass for durable staging, output-limit recovery, SQL attribution, evidence offsets, and cache reads. It does not establish scientific correctness, semantic citation quality, omission recall, cross-paper reliability, or production-scale cost. No human domain-expert review was performed.

Runner usage and isolation instructions are in [`PilotRunners/FullTextResumePilot/README.md`](../PilotRunners/FullTextResumePilot/README.md). The frozen artifact contains the full HTTP, timing, checkpoint, usage, SQL, and read/cache evidence.
