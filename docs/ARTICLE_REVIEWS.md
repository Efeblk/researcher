# Specialist article reviews

Specialist review is an opt-in, evidence-bound Analysis Service workflow over an already saved canonical article analysis. `POST /api/v1/articles/review/generate` creates or reuses it; `POST /api/v1/articles/analysis` reads it together with status and evidence. The workflow never fetches a URL, recollects provider data, reads private HR or faculty context, or runs from article-summary automation. A current `PersonelID`–canonical-work association and a successful canonical article analysis in the requested language must already exist. These compatibility actions do not use the separate protected-product access adapter. Runnable requests are in [Articles.http](../ResearcherAnalysisService/Requests/Articles.http).

`/api/v1/articles/review/generate` runs four fixed passes in deterministic order:

| Role | Allowed finding kinds | Purpose |
| --- | --- | --- |
| `method` | `source_observation`, `review_question` | Reported design, sampling, controls, and procedure |
| `quantitative` | `source_observation`, `review_question` | Reported numbers, units, denominators, uncertainty, and comparisons |
| `claim_evidence` | `source_observation`, `review_question` | Claim scope, causal language, and reported evidence |
| `teaching` | `teaching_adaptation`, `review_question` | Evidence-grounded instructional examples and questions |

Each pass returns at most three candidates with one or two immutable source IDs. A separate verifier checks the entire candidate. Only `supported` candidates are retained; `unsupported` and `uncertain` candidates are omitted and counted in `coverage`. A review question or teaching adaptation is always a suggestion: its directly supported `basis` is stored separately from `suggestion`. Exact quote, page, and offsets are resolved on the server from the saved source ID rather than accepted from the model.

All four specialists receive the same complete saved source catalog. Generation is checkpointed once per role. Verification starts with one batch per role; an attested provider `output_limit` splits that batch deterministically until it succeeds or a singleton exhausts the limit. Input is never silently truncated. Analysis Service applies explicit byte/context budgets and returns HTTP 413 when the exact serialized stage request cannot fit. The product workflow applies an overall timeout; each provider request applies the configured HTTP timeout and propagates cancellation.

Analysis Service obtains an attested settings fingerprint and a conservative maximum cost quote before every provider dispatch. A serializable SQL admission step persists the attempt GUID, call reservation, and spend reservation before the network call. The same GUID is the Gemini usage-ledger primary key. Successful role generations and verification batches are reused after a retry or process restart. A dispatched attempt with unknown cost or outcome is never retried automatically, and no partial report is exposed: all four roles and every candidate verdict must be complete before final assembly. `MaximumProviderCalls` and `MaximumSpendUsd` are cumulative limits on one immutable work item; repeating the request cannot reset them. `forceRegeneration=true` explicitly creates a new work item.

The source coverage block comes from the original immutable `SavedArticleSummary.SnapshotJson`, validated against saved pages and spans. It describes the source actually available for analysis. Summary-claim omissions by the earlier verifier do not make full text partial. Abstract-only input remains explicitly partial with its original scope reason.

Review runs are append-only SQL artifacts linked to the exact base analysis run, source snapshot, canonical work, and span evidence. Durable work additionally pins the saved source request, policy, prompt/model settings fingerprint, and force-generation identity. An ordinary retry resumes the newest matching unfinished work, including an interrupted forced run; otherwise it reuses the latest matching completed report. Set `forceRegeneration` to `true` to start another work item for the same source. Source, policy, prompt, model, thinking, output-limit, or pricing changes cannot reuse mismatched checkpoints.

`/api/v1/articles/analysis` only reads the combined saved state. It makes no network request and performs no write. Its nullable `Review` returns `isStale=true` when a newer base analysis exists or the configured review policy changed. Both generation and read paths recheck the current researcher association; responses contain the caller's `PersonelID`, canonical identifiers, review metadata, findings, evidence, coverage, and staleness, but no source URL or other researcher identifier.

Configure the Analysis product workflow in `ResearcherAnalysisService/appsettings.json`:

```json
"ArticleReview": {
  "PolicyVersion": "article-specialist-review-policy-v3",
  "TotalTimeoutSeconds": 330,
  "MaximumSourceBytes": 100000,
  "MaximumProviderCalls": 24,
  "MaximumSpendUsd": 1.0
}
```

The product workflow's configured outer limit covers database work and every staged request. Each Gemini HTTP call uses `Ai:TimeoutSeconds` (180 seconds by default). `Ai:ArticleReviewTimeoutSeconds` remains the 300-second overall limit for the stateless one-shot analysis endpoint; it is not a second overall timer around the persistent product sequence.

The analysis service continues to use `Ai:ArticleProvider`, `Ai:ArticleModel`, `Ai:ArticleVerifierModel`, and the existing Gemini usage ledger. `Ai:ArticleGenerationThinkingLevel` and `Ai:ArticleVerifierThinkingLevel` independently configure Gemini reasoning as `low`, `medium`, or `high`; both retain the existing `high` default. `Ai:ArticleMaxOutputTokens` and `Ai:ArticleVerifierMaxOutputTokens` remain separate and default to 8192. Gemini counts thinking within the output-token limit, so raising that limit does not itself reduce thinking effort. `Ai:ArticleReviewMaximumInputBytes` adds a review-specific cap, and `Ai:ArticleReviewTimeoutSeconds` bounds only the stateless one-shot four-role operation at 300 seconds by default. The durable product route currently requires Gemini because it fails closed unless hosted-call pricing and usage can be attributed. The stateless one-shot analysis and evaluation routes retain the local Ollama adapter. See [Gemini thinking](https://ai.google.dev/gemini-api/docs/thinking).

New reports include deterministic `sourceFidelity` metadata. It states which supplied PDF pages, HTML text, or abstract text were processed, while explicitly recording that flattened formula/table layout was not verified and figure imagery was not analyzed. Prompts prohibit treating ambiguous flattened relationships or extraction loss as an error in the paper; verification returns `uncertain` when direct material support is unavailable. This metadata describes a text-only guard and does not claim visual formula, table, or figure parsing. Older stored report JSON remains readable with absent fidelity metadata and is never retroactively labelled verified.

Verification batches nest each finding with only the spans it cites. Uncited neighbors and citations belonging to another finding are excluded from that finding's evidence, while generation still receives the complete source catalog and must cite every span needed for each material clause. This removes ambiguous citation attribution in the verifier input; it does not guarantee that a model will follow the isolation rule or reach a correct verdict.

The corresponding bounded summary-verifier regression and full-text batch are documented in [Full-text citation-alignment pilot — 12 September 2026](FULLTEXT_CITATION_ALIGNMENT_PILOT_20260912.md). That evidence covers the observed mismatch and does not establish general review accuracy.

Failed review responses retain the existing generic `errorCode` and message and may include a bounded `failure` object with `reason`, `stage`, and `role`. Reasons distinguish an output limit, incomplete output, malformed JSON, and invalid evidence without retaining raw provider content. Stage is `generation` or `verification`; role is one of the four fixed specialist roles. Evaluation telemetry records the corresponding specific reason on the provider attempt while the top-level evaluation code remains backward compatible. Gemini `MAX_TOKENS` is reported as `output_limit`; an empty otherwise-complete response remains `incomplete_output`.

`no_supported_findings` means the bounded automatic verifier retained no candidate. It is not proof that the article is flawless, that every possible issue was found, or that a suggested question identifies an actual error. The output contains no aggregate quality score, HR recommendation, or model-generated final judgment. Human reviewers remain responsible for interpretation.

Generation and verification may share a model family and therefore correlated errors. Synthetic tests verify orchestration, schema checks, evidence matching, and failure propagation only. A separately authorized live Gemini pilot completed one real, abstract-only paper through summary and specialist review; see [the 12 September 2026 pilot report](AI_PILOT_20260912.md). The workflow passed its operational gate, but a separate coordinating-agent review found a material Turkish terminology error and the quality gate did not pass. That bounded observation does not establish scientific correctness, full-text quality, cross-discipline reliability, or production-scale cost. Broader real-paper quality and cost benchmarking, plus adaptive review for oversized sources, remain follow-up work.

A later [nine-page full-text pilot](FULLTEXT_PILOT_20260912.md) persisted an evidence-exact Adam v1 summary, but its single review request failed closed when the final `teaching` verification subcall reached the unchanged 8,192-token output limit. No review row was saved. This is a bounded full-text summary pass and review output-limit failure, not evidence that full-text review succeeds generally.
