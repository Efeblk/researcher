# Broader service acceptance — 14 September 2026

The held-out broader acceptance passed for two public social-science papers. The result is composed from immutable
runs: three faculty answers accepted under generation v8, Teaching accepted under v9, and RelatedWorks accepted under
v10. The unsupported BERT/GPU request was rejected before any model call. This establishes useful behavior for the
fixed sample and requests; it does not establish universal scientific accuracy or exhaustive recall.

## Released lifecycle

| Run | Result | New Gemini usage | Manifest SHA-256 |
| --- | --- | ---: | --- |
| `broader-service-acceptance-20260914-v1` | Collection, two summaries, metrics, context, and HR completed; a harness-only 403/404 expectation stopped the run. | 14 calls / USD 0.293676750 | `e6fc27ca4ffba4bdd1d2e75493dc2ac27efc28559e3135256d3fc74bb30075e2` |
| `broader-service-acceptance-20260914-v1-continuation` | Three positive modes and the unsupported request passed; RelatedWorks and Teaching were retained as nonpasses. | 43 calls / USD 0.306819750 | `b250c027bd3851d6388da178842d53f827d4f7afbc33e1fa96365cd09f9a2d3f` |
| `broader-service-acceptance-20260914-v1-correction` | Teaching passed under v9/v7/v2; RelatedWorks still omitted an explicit scope statement. | 12 calls / USD 0.106693500 | `6c8e0d0a8a692a9c641754f7018cc54bbf4cecff038213fbf340a820ee083b1b` |
| `broader-service-acceptance-20260914-v1-related-final` | RelatedWorks passed under generation v10 and coverage v2. | 6 calls / USD 0.041727750 | `ca5ea85d933d6e3ae7a43378ddf2f1908696b4e3ced0c9d067c1b887da026588` |

The owned database is
`AcademicBroaderServiceAcceptance_a96a075e8eba4e3f975a5aa438404fec`. It contains 75 exactly attributed Gemini calls
costing USD 0.748917750, with zero unknown usage. The preserved historical database
`AcademicQualifiedServiceAcceptanceV10_2bbb62932a7b47f684be7ed267856447` was not cloned, modified, or deleted.
The earlier acceptance effort used 178 calls and USD 3.168960750; together, known local usage is 253 calls and USD
3.917878500. The older 13 September unknown reservation of USD 0.06427125 remains separate from these known totals.

Raw statuses remain unchanged: the initial run is `failed_database_preserved`, the continuation and first correction are
`awaiting_root_audit_nonpass`, and the final run is `awaiting_root_audit`. These are captured states from before subsequent
review. No raw response, ledger row, status, or manifest was rewritten to reflect the final decision.

## Fixed faculty matrix

| Request | Accepted behavior | Pinned protocol configuration |
| --- | --- | --- |
| Turkish `ExploreOwnRecord`, both papers | Summarized the common empirical theme and contrasted a single field intervention with a multi-study replication design, citing both works. | v8 / verification v6 / repair v1 / coverage v1 |
| Turkish `RelatedWorks`, both papers | Four supported pairwise method comparisons with suggestions explicitly limited to conditional use of the selected records. No universal, exhaustive, or citation-link claim. | v10 / verification v7 / repair v2 / coverage v2 |
| English `TeachingHelp`, Fine paper | A clearly labelled hypothetical learner exercise, worked guidance, and a separate discussion question. | v9 / verification v7 / repair v2 / coverage v1 |
| English `OwnPaperMethods`, reproducibility paper | Study selection, distinct success measures, and a limit on generalizing beyond the sampled journals and studies. | v8 / verification v6 / repair v1 / coverage v1 |
| Turkish `OwnPaperIssues`, reproducibility paper | Source-grounded rechecks that keep nonreplication distinct from proof of misconduct, error, or a false original result. | v8 / verification v6 / repair v1 / coverage v1 |
| Turkish unsupported BERT/GPU request, Fine paper | Deterministic `422`, no AI call, and no invented model or hardware fact. | deterministic evidence boundary |

The accepted RelatedWorks answer uses reader-facing “first paper” and “second paper” labels while retaining exact source
IDs only in citation arrays. Its missing-data item is a conditional transparency question; it does not classify the Fine
paper's exclusion as selective reporting. Its count-based description of delayed parents does not deny the paper's later
statistical analysis. The Teaching hypothetical setup stays in the response and is not presented as a reported fact.
The table records the pinned generation, verification, repair, and coverage versions; a configured repair version does
not imply that an accepted case dispatched a repair call.

## Sources and retrieval

The normal bulk path consumed synthetic ORCID metadata for two synthetic researchers. Hash-pinned copies of the public
PDFs were replayed through the production source fetcher; Gemini was the only live provider. Collection produced two
canonical works, three researcher-work associations, and three observations, including a shared DOI that resolved to one
canonical work.

| Paper | Frozen source and extraction |
| --- | --- |
| *A Fine Is a Price* (2000), DOI `10.1086/468061` | 200,291 bytes; SHA-256 `55b0b122a4a2b702b8214482e9f8fabf492c4ed50d6747a5de0c091eaa4d9f89`. Production retained 17 text-bearing pages from 18 physical pages, 41,079 characters, 86 spans, and exact UTF-16 slices. It conservatively remains partial because page 18 was unread; independent rendering found that page visually blank. |
| *Estimating the reproducibility of psychological science* (2015), DOI `10.1126/science.aac4716` | 677,051 bytes; SHA-256 `84b0f9e63c3117be15f08ee31776d30b90e7fd4426fc0aeb0ef8d288b62fa5b4`. Production retained all 10 physical pages, 83,853 characters, 162 spans, and exact UTF-16 slices. Page 1 is a journal summary and page 10 publisher tooling, so not all 10 are described as article-body pages. |

Independent source review passed the four retained Fine summary claims and six of seven reproducibility summary claims
without qualification. The seventh faithfully repeats the PDF summary's headline that 97% of original studies reported
significant results. The body adds that 4 of those 97 had `P > .05`; the headline should be read with this full-document
qualification. The reproducibility sample was quasi-randomly drawn from 2008 issues of three journals and is not
representative of all psychology. Significance, confidence-interval, subjective, and meta-analytic criteria are distinct
measures rather than one uniform definition of replication success.

The first zero-call continuation preflight exposed a retrieval problem: both work IDs were present, but incidental
bibliography and footnote spans displaced the Fine methods evidence. Generic catalog `academic-evidence-search-v4`
preserves exact-phrase priority, then reserves verified section-intent evidence across explicitly selected works before
the global diversity pass. The unchanged fixed queries subsequently led with the Fine design and reproducibility methods
spans. The change has no paper-specific title, DOI, span, or query term. Candidate and evidence-link caps still apply
before diversification, so very large work sets can truncate later IDs; this is not exhaustive large-corpus coverage.

## Service, audit, and limits

- Metrics, HR dossier, and versioned private context were generated and read back through production endpoints. Metrics
  remained descriptive, missing OpenAlex context stayed explicit, and HR output contained no personnel score or
  recommendation.
- A private canary remained absent from public evidence, HR output, and faculty answers. Cross-owner access returned the
  intended uniform `404`. Identical requests reused saved runs without AI; changed payloads returned `409`.
- SQL reports, API readback, authorized evidence, citations, UTF-16 page slices, freshness, fingerprints, and costs
  matched. Independent review recomputed all 21 final manifest entries and all 35 first-correction entries without a
  mismatch.
- The final six capture pairs matched six SQL rows: five medium-thinking calls and one high-thinking coverage call, all
  HTTP 200 with `STOP`, exact requested and returned `gemini-3.8-flash`, reliable token arithmetic, current pricing,
  owned citations, unique attempt IDs, readback, and replay. Ports 5135 and 5235 were closed afterward.

The current protocols are generation v10, verification v7, repair v2, and coverage v2. Historical accepted v8 and v9
reports remain exactly readable; they were not reinterpreted as current-protocol generations.

Final gates passed 463 collector tests, 279 analysis-service tests, 9 browser JavaScript tests, and TypeScript type
checking: 751 automated tests. Automated tests used fake providers and isolated SQL and made no paid calls. Runner builds
and self-tests passed without warnings or errors.

This result covers the fixed two-paper held-out sample, six fixed requests, production persistence and endpoints, exact
saved-source citations, bounded retrieval, live Gemini accounting, access isolation, and replay behavior. It does not
establish universal truth, complete scientific interpretation, exhaustive retrieval or omission recall, production BYS
identity mapping, UI readiness, live remote-URL byte freshness, deployment readiness, or sustained unattended uptime. Provider
metadata and PDF transport were synthetic or hash-pinned local fixtures during acceptance.

Published reference: [plan](BROADER_SERVICE_ACCEPTANCE_20260914_PLAN.md). The initial failure, continuation,
teaching correction, final result, and manifest remain in local acceptance directories and are not published with
the source tree.
