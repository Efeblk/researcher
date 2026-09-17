# AI system plan

The system has two intended outputs: an evidence dossier for authorized HR decisions and a faculty-facing research assistant. SQL Server remains authoritative for personnel identity, provider observations, canonical publications, source snapshots, analysis runs, and exact claim evidence. Private faculty context and HR-only data remain separate inputs and authorization domains.

## Completed foundation

- Canonical DOI works, source-scoped fallbacks, current researcher memberships, provider observations, retraction signals, and guarded read APIs.
- Immutable article page/span snapshots, exact claim-to-span evidence, coverage and verifier metadata, and retained exact request/report JSON.
- Durable default-on article-summary automation after individual, bulk, YÖKSİS, Crossref, and Semantic Scholar source persistence. The off switch retains pending work and manual regeneration remains available.
- Stable metadata-input and extracted-content identities, explicit analysis-policy versions, crash recovery, bounded retries, stale-generation fencing, manual/automatic coordination, and safe status reads.
- Durable precomputed `publication-metrics-v4` snapshots: unique collected canonical-work and provider-observation counts, year/category conflict handling, unmapped-work visibility, DOI/abstract/recorded-source coverage, descriptive eligibility, and cross-provider consistency with formulas and denominators. Separate provider blocks retain saved bibliometrics with per-field provenance and typed SQL history. Bounded OpenAlex work context adds owner-scoped primary topics, field/subfield/year/type groups, and honest availability/conflict reporting for provider-supplied FWCI and percentile values. Atomic invalidation, bounded refresh, safe retries, stale last-good reads, provider-only collection invalidation, and catalog/year rollover are complete. This remains collected-work and saved-provider coverage, not a complete metric catalog or institution-complete output.

## Implemented deterministic data and product layer

The V4 catalog now pins descriptive category eligibility, reference-population readiness, and cross-provider comparison rules. Reviewed reference-population manifests can be stored and mechanically validated, while internal field normalization remains unavailable until a compatible population, policy, and formula receive scientific acceptance. Owner-scoped `academic-evidence-search-v4` retrieval exposes stable global evidence IDs, bounded Turkish intent recognition and English source-term expansion, per-work verified-section diversity for explicit multi-work comparisons, strict condition/limitation diversity, and exact-source plus saved-claim provenance. It performs no model call and does not claim general semantic search or complete recall. A deterministic graph export remains a deletable, rebuildable SQL projection with proposed relationships quarantined from validated evidence. See [Deterministic data and knowledge layer](DATA_KNOWLEDGE_LAYER.md).

## Completed specialist review

The opt-in workflow now runs bounded method, quantitative, claim/evidence, and teaching passes through durable Analysis Service stages. Each specialist receives only the authorized immutable source spans, returns at most three structured candidates with exact evidence IDs, and cannot write a final HR judgment. Generation and verifier batches are checkpointed independently in SQL; an attested output-limit result can split a verifier batch, while a separately attested high-thinking generation output limit may make one identical medium-thinking recovery call. An ordinary retry or process restart resumes only missing work. Analysis Service persists cumulative call and spend admission across requests, leaves dispatched attempts with unknown outcomes closed to automatic retry, and requires every one of the four roles before it can persist a final report. Concurrent requests have one workflow owner, and source, current researcher association, model settings, prompt policy, evidence offsets, and exact span text are rechecked before dispatch and final persistence. The staged product route currently requires Gemini so it can quote and attest hosted usage; the stateless one-shot endpoint retains the existing provider abstraction.

The deterministic aggregator retains differing supported observations without adding a conclusion or quality score. New reports also record the source-fidelity boundary: extracted text can cover every saved page while formula and table layout remains unverified and figure imagery remains unanalysed. Flattened or ambiguous math and table text cannot establish a paper error or a numeric cell relationship. This metadata is additive, and old cached reports do not acquire a retrospective verification claim. SQL runs link the exact base analysis and source snapshot, cached reads remain local and report staleness, and no automatic summary path triggers review. See [Specialist article reviews](ARTICLE_REVIEWS.md) and the [OpenAI document-review example](https://developers.openai.com/showcase/agents-api-document-review).

## Completed article-evaluation slice

The opt-in evaluation workflow compares three named, versioned profiles without changing the production AI provider or model. Every run includes an immutable bilingual synthetic calibration pack with 18 controlled source-reading claims balanced across `supported`, `unsupported`, and `uncertain`; expected answers remain in Analysis Service's private evaluation data and are never sent to the model. A run may also snapshot up to three already-saved canonical sources for real-article review and ask later profiles to blind-cross-check the first profile's findings.

SQL stores the run, pinned profile snapshots and fingerprints, immutable cases, work items, attempts, telemetry, results, and safe aggregate views. The durable worker bounds total work and weighted provider calls, fences writes with per-attempt tokens, and does not automatically retry an interrupted or failed call whose remote cost may be unknown. Controlled accuracy, confusion matrices, failures, pending claims, exact evidence-link checks, latency, tokens, and nullable cost estimates remain distinct from cross-model agreement. Scientific accuracy and real-article omission recall remain `null` because this slice has no independent expert gold. See [Makale değerlendirmeleri](ARTICLE_EVALUATIONS.md).

The immediate evaluation step compared six pinned Gemini and local models on the corrected 18-claim controlled pack, then ran a paired saved-source review pilot with the leading cloud and existing local baseline candidates. Its frozen method and completed results are recorded in [Model comparison — 12 September 2026](MODEL_COMPARISON_20260912.md). The run did not change production defaults. The paired candidates both stopped during method verification and produced no final report. A later [bounded live Gemini pilot](AI_PILOT_20260912.md) completed the real-review flow under controlled budgets and passed its operational gate, but its single abstract-only source does not establish scientific correctness or review quality; a separate coordinating-agent review found a material Turkish terminology error, so the quality gate did not pass. The controlled mechanical corpus and representative sampling can then expand without asking the user to supply expert gold. An independently expert-labelled real-article set with a documented sampling and adjudication process is a later optional requirement only if the system needs to make scientific-accuracy or omission-recall claims. Until that evidence exists, the system must not infer scientific correctness, omission recall, a winning model, or personnel suitability from calibration accuracy, exact evidence links, or model agreement.

The HR dossier captures traceable facts, metric definitions, coverage gaps, conflicts, freshness, and append-only reviewer actions without a personnel score or decision. The durable faculty assistant pins the V3 search manifest, passes only original source-span text to analysis, uses versioned private context and cited saved evidence, then reauthorizes identity and current work association immediately before generation. Authorization operations, prompts, stored records, and outputs remain separate between these products even when they reuse the same public article evidence. The standalone access adapter remains unconfigured and fails closed until a consuming deployment supplies a trusted principal-to-scope and principal-to-personnel mapping. See [Academic AI products](ACADEMIC_AI_PRODUCTS.md).

## Service-only delivery boundary

The project deliverable is the service/API: guarded single and bulk requests, SQL-saved outputs and status, durable
background jobs for continuous operation, generic authorization boundaries, evidence and coverage reporting, and exact
`gemini-3.8-flash` generation plus separate verification. A deployment-specific
Production institutional identity integration is outside this delivery scope. A web or institutional client is an optional demonstration and is not a completion
blocker. This boundary does not remove endpoint access checks or make the default adapter usable; the live product pilot
used synthetic runner-only authorization and did not validate production identity integration.

Verified partial responses and conditional question wording are covered by the acceptance slice below. Saved Turkish
claims can now bridge lexical queries to exact English source spans without a runtime AI call. This is limited by saved
summary coverage, retained claims, an explicit five-category vocabulary, and candidate caps; it does not establish general
translation, semantic retrieval, or representative recall. The change does not alter the pinned model route.

### Verified partial-answer acceptance

The implemented service slice keeps only verifier-supported faculty items. Prompt v8 produces at most six candidates;
verifier v6 checks each one at medium thinking against only its own one or two spans and records supported, unsupported,
uncertain, unverified, and omitted counts. A known attributed verifier output limit or invalid response omits that item
without retry. One repair-v2 pass may address the first two semantically checked omissions, preserves supported items,
and checks replacements independently without recursion. Mixed outcomes remain honest partial reports; zero retained
items become `no_supported_items`, and request coverage runs once only when items survive. Gemini remains pinned to
`gemini-3.8-flash` with 16,384-token generation/repair and 8,192-token verification/coverage ceilings. Generation defaults
to medium; an explicitly configured high start may make one identical medium recovery only after a durably recorded,
exact-model, priced output limit. Fresh reports retain attempt IDs and typed generation, source-check, repair, and coverage
audits. The default shape has at most 11 dispatches, or 12 with high-generation recovery, under a 1,800-second total outer
bound and 180-second per-call bound. Offline tests cover all-supported, mixed, all-rejected, zero-candidate, malformed-output, HTTP, SQL persistence,
idempotent replay, and legacy stored-report paths.

## Full-plan acceptance checklist

- [x] Durable review stages: generation and verifier batches persist independently; an attested last-role output limit splits into smaller batches, completed stages are not repeated after a retry or process restart, and a report requires all four roles.
- [x] Cumulative review bounds: call and conservative spend admission remains attached to the work item across requests and retries; concurrent requests have one owner, and cancelled, crashed, dispatched-unknown, or unattested attempts retain their reservation and are not blindly retried.
- [x] Review identity and source guards: source snapshot, current researcher association, evidence offsets and text, actual model settings, pricing attestation, and prompt policy must match before dispatch, checkpoint completion, reuse, and final persistence.
- [x] Honest summary fallback and source fidelity: output-limit fallback is bounded, singleton verifier exhaustion remains budget-unverified and is excluded from `AutomaticallyCheckedClaims`, and text-page coverage is reported separately from unverified formula/table layout and unanalysed figure imagery.
- [x] Canonical data and retrieval: every supported source maps to stable canonical work and researcher identities with explicit provenance, conflict and coverage states; retrieval has a versioned corpus and offline coverage/ranking evaluation rather than anecdotal examples.
- [x] Reference policies: reference eligibility, deduplication, retraction, version, and cross-provider comparison policies are explicit and tested.
- [x] Derived graph: any relationship graph is a versioned, deletable, rebuildable projection from authoritative SQL, retains canonical IDs and evidence links, separates proposed from validated edges, and excludes private HR and faculty context from public/sanitized pilots.
- [x] HR dossier: authorized reviewers receive traceable facts, metric definitions and denominators, coverage gaps, conflicts, freshness, evidence, and review actions. The product does not generate a final personnel judgment or silently substitute provider totals for evaluated metrics.
- [x] Faculty identity and assistant: every request resolves a current faculty association, scopes private context to that identity, cites saved evidence, reports missing coverage, and cannot read HR-only inputs or another faculty member's private context.
- [x] Scope-separated authorization: service, faculty, and HR permissions are distinct at endpoints, retrieval, prompts, stored artifacts, and audit records, with denial and identity-leakage tests for each boundary.
- [x] Offline final gates: .NET, TypeScript, and JavaScript checks pass using fake HTTP providers and isolated SQL only. Test hosts cannot load development user secrets, clear inherited provider credentials, use test-only worker settings, and never start against the application database.

The current offline gate passed 462 collector tests and 279 analysis-service tests, TypeScript type checking, and 9 JavaScript tests (750 automated tests total). The .NET suites used fake HTTP providers and isolated LocalDB databases; those automated tests ran no operational batch or live model/provider call.

External scientific validation remains a separate, explicitly authorized gate. Offline success demonstrates contracts, deterministic policy, isolation, and workflow behavior; it does not by itself validate real Gemini completion behavior, establish scientific accuracy or omission recall, or justify a production routing decision.

## Current validation boundary

The 14 September V5 service-acceptance lifecycle is preserved as a historical collection and automation run. Its nine semantic
probes, two automatic PDF summaries, Adam specialist review, publication metrics, faculty context, HR dossier, and
Methods response ran against the real fixed Gemini model. The lifecycle then stopped without retry when the first
Teaching generation returned an attributable `OutputLimit`. SQL records 51 calls, 386,738 tokens, USD 0.945550500,
48 successes, three output limits, and zero unknown usage; all requested/returned models, token arithmetic, price
calculations, and audited immutable evidence links matched. The Methods report and earlier phases remain available for
semantic review, but V5 is not a complete product acceptance. Its 24 raw artifact files are frozen; the accompanying
manifest records path, byte length, and hash for 18 executed source files but does not contain copied source text.
The composed current boundary adds later retained evidence without rewriting V5. V10 established the current policy-v5
four-role specialist review, HR dossier, and a useful Methods answer; V11 safely rejected a placeholder Teaching answer.
V14 then ran five fresh qualification probes and new Methods, Teaching, and Issues requests against the preserved two-PDF
database. All five expected verifier decisions matched, and independent semantic review accepted all three mode outputs,
including honest partial omissions and one bounded Teaching repair. Readback, identical replay, changed-payload conflicts,
one append-only HR action, and SQL/source/citation/model/token/pricing/cost parity added no AI calls. V14 used 26 exact
`gemini-3.8-flash` calls for USD 0.219307500 with zero unknown usage; the database finished at 104 calls and USD 1.834842750.
See [Final service acceptance — 14 September 2026](SERVICE_ACCEPTANCE_20260914_FINAL.md).

The repository can implement and test provider adapters, retrieval policy, evidence contracts, product boundaries, and fail-closed authorization without calling a paid service. Those offline checks remain distinct from the failed paired real-source benchmark, whose two recorded candidates stopped during method verification. A subsequent [bounded live Gemini pilot](AI_PILOT_20260912.md) produced a persisted specialist report and passed the operational gate, but the single abstract-only run and its observed Turkish terminology error do not establish scientific correctness or review quality.

The later [nine-page full-text pilot](FULLTEXT_PILOT_20260912.md) persisted a complete-source Adam v1 summary with exact SQL evidence, then failed closed during the final `teaching` verification call because Gemini reached the unchanged output-token limit. No review report was persisted. That run established only a verified full-text summary pass and a review output-limit failure; it did not establish general full-text review success or scientific correctness.

The completed reliability slice directly covers that failure shape with durable stages and adaptive verifier splitting, and it corrects source-size admission so the page and span catalog is not counted twice. The subsequent [full-text resume pilot](FULLTEXT_RESUME_PILOT_20260912.md) completed Adam v1 summary and all four review roles through the real provider: 22 calls, four recovered output limits, 215,448 tokens, and an estimated USD 0.491310000. Twelve review checkpoint attempt IDs and costs matched the usage ledger, all 23 evidence links had exact page, text, and UTF-16 offsets, and saved reads plus a repeated review created no new calls. The frozen raw harness comparison flag remains false; a separate typed offline replay corrects its public/SQL serialization-shape comparison and establishes the operational pass, and the runner suite passed 13 tests.

The 13-page football source also passed offline source-limit and stage-quote preflight, but its sampled eight review-stage reservations of USD 0.53948025 exceeded the USD 0.508690 remaining global ceiling before summary cost, so no football provider call was made. A coordinating AI review of the Adam output found a retained purpose claim whose selected quote fragments did not contain its RMSProp term even though neighboring source text did. The live result therefore establishes one bounded operational full-text pass, not a semantic-quality or scientific-correctness pass, and the second live sample remains budget-deferred.

The citation-alignment correction now gives every verifier item only its own cited spans, excludes uncited neighbors, and requires generation to cite support for every material clause. Summary and review prompt and policy identities advanced so older cached analyses and review checkpoints cannot be reused under the corrected rules. In a bounded live regression, the original Adam claim with its incomplete two citations became `uncertain`, while the same claim with the complete citations was `supported`, both alone and beside another claim that cited the missing span. The subsequent two-PDF batch completed both summaries and all four review roles in 44 total calls for an estimated USD 0.812765250 from recorded token usage and pinned pricing, with no unknown usage. A coordinating AI audit matched all 79 evidence links to exact UTF-16 source slices and all four saved HTTP reports to SQL, but the conservative verifier omitted 8 of 40 Adam summary candidates, 7 of 13 Football summary candidates, and 13 of 24 review candidates. The evidence is recorded in [Full-text citation-alignment pilot — 12 September 2026](FULLTEXT_CITATION_ALIGNMENT_PILOT_20260912.md). This fixes the observed citation mismatch and demonstrates bounded operational recovery; it does not establish comprehensive or universally accurate scientific output.

Production institutional role and identity claims are not available in this standalone repository, and integrating them is outside the service-only deliverable. The default adapter therefore keeps private HR and faculty endpoints unavailable unless a consuming deployment supplies trusted principal-to-scope and principal-to-personnel mappings. Development-only permissive authorization and the pilot's synthetic adapter are not evidence that a deployment mapping works.

Any internal field-normalized metric remains descriptive or unavailable until the reference population, category eligibility, time window, coverage, and expert review are accepted. The Neo4j projection remains optional and unbenchmarked because no Docker engine is available in the current environment; code and export-shape tests cannot establish a graph-assisted retrieval benefit.

## Optional Neo4j pilot

With the deterministic catalog and specialist evaluation in place, test Neo4j on 20–50 public or sanitized canonical works using the [Neo4j LLM Graph Builder](https://github.com/neo4j-labs/llm-graph-builder) as a derived SQL projection. SQL stays authoritative; graph nodes retain canonical IDs, analysis-policy versions, and evidence links back to immutable spans. Proposed LLM relationships remain separate until evidence validation accepts them. Do not put private faculty context and HR records in the same graph projection.

Compare the graph-assisted prototype with SQL/retrieval baselines on claim accuracy, evidence coverage, entity-merge errors, latency, operational complexity, and model/provider cost. Promote it only if it improves a measured use case without weakening association guards, evidence gates, deletion/rebuild behavior, or auditability.
