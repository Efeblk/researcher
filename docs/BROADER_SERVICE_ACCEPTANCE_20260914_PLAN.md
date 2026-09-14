# Broader service acceptance — 14 September 2026

This one-use acceptance lifecycle tests two held-out social-science PDFs through the production collection, canonicalization, source acquisition, extraction, automatic-summary, evidence-search, metrics, HR-dossier, and faculty-assistant paths. Provider metadata and PDF transport are hash-pinned local fixtures; Gemini is the only live provider. No historical database or artifact is cloned, modified, or deleted.

The primary synthetic researcher owns *A Fine Is a Price* and *Estimating the reproducibility of psychological science*. A second synthetic researcher owns the reproducibility paper. Bulk collection must produce two canonical works, three researcher-work associations, and one canonical record for the shared DOI. Both PDFs must pass native extraction and exact source-catalog slicing before any paid call.

The live matrix was revised once before implementation because the first draft omitted the two public modes that had never received live coverage. The final matrix is fixed:

1. Both works, Turkish, `ExploreOwnRecord`: summarize common themes and the main method difference with sources.
2. Both works, Turkish, `RelatedWorks`: show only method relationships supported by the current records and state the scope of suggestions.
3. Fine, English, `TeachingHelp`: create a classroom exercise with a learner task, worked guidance, and a separate discussion question.
4. Reproducibility, English, `OwnPaperMethods`: explain selection, success measurement, and one generalization limitation.
5. Reproducibility, Turkish, `OwnPaperIssues`: identify points to recheck while separating uncertainty from established error.
6. Fine, Turkish, unsupported BERT/GPU request: return no invented architecture or hardware fact; a no-match `422`, `no_supported_items`, or honest partial/unanswered result is acceptable.

Positive requests must retain useful source-supported output and fulfill their explicit deliverables. Every citation must match the authorized evidence, public retrieval response, saved SQL span, and exact UTF-16 page slice. The run also checks current metrics, HR dossier readback, owner isolation, freshness, identical zero-call replays, changed-payload `409`, exact protocol/model identity, request and response capture, and SQL/ledger pricing reconciliation.

The exact model is `gemini-3.8-flash`; faculty protocols are `faculty-evidence-assistant-v8`, `faculty-evidence-assistant-verification-v6`, `faculty-evidence-assistant-repair-v1`, and `faculty-request-coverage-v1`. The aggregate ceiling is 96 calls and USD 3.00. Automatic summaries may consume at most 30 calls before the six faculty phases begin, preserving 66 calls of headroom. Each faculty phase is blocked before a twelfth call. Unknown usage stops all dispatch. Failed runs preserve their owned database and artifacts, and this run ID cannot be reused.

The bounded correction retains the original and continuation artifacts, database, two summaries, three accepted
positive outputs, and deterministic unsupported result. It reruns only the unchanged `RelatedWorks` and `TeachingHelp`
requests after the generic prompt protocols advance to `faculty-evidence-assistant-v9`,
`faculty-evidence-assistant-verification-v7`, and `faculty-evidence-assistant-repair-v2`. The correction starts from the
exact 57-call, USD 0.6004965 SQL ledger, permits at most 22 new calls and USD 2.3995035, and keeps the original aggregate
ceilings of 96 calls and USD 3.00. Its zero-call preflight must verify the frozen continuation manifest, current retrieval,
and readback of the three accepted v8 reports before a separate one-use release can run.

The final RelatedWorks check preserves the accepted teaching result and all earlier outputs. It advances only generation
to `faculty-evidence-assistant-v10` and request coverage to `faculty-request-coverage-v2`; verification v7 and repair v2
remain unchanged. It reruns the unchanged RelatedWorks query once, from the exact 69-call, USD 0.70719 ledger, with at
most 11 new calls and USD 2.29281. The answer must use reader-facing paper labels, give actual pairwise method synthesis,
and state the conditional application and selected-record evidence scope without claiming universal or exhaustive reach.

The owned database prefix is `AcademicBroaderServiceAcceptance_`. The preserved `AcademicQualifiedServiceAcceptanceV10_2bbb62932a7b47f684be7ed267856447` database is outside this lifecycle.
