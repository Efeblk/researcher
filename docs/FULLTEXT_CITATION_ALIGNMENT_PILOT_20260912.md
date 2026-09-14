# Full-text citation-alignment pilot — 2026-09-12

The citation-alignment acceptance run passed its bounded operational gate. The exact historical Adam
claim was `uncertain` when its citations omitted the span containing RMSProp and `supported` when the
complete supporting spans were cited, both in a paired batch and alone. The two PDF workflows then
completed Turkish summary generation and all four specialist review roles. This fixes the observed
claim/citation mismatch; it does not establish universal semantic accuracy or scientific correctness.

## Change under test

Verifier input now nests every claim or finding with only the spans it cites. Uncited neighbors and
sources cited by another batch item are excluded from that item's evidence. Generation prompts require
atomic output and citations that cover every material named method, entity, actor, number, condition,
comparison, qualification, and negation. Summary and review prompt and policy versions advanced so
older cached outputs and checkpoints cannot be reused under the corrected rules.

All Gemini requests used `gemini-3.8-flash`, high generation and verifier thinking, and 8,192 output
tokens. One pre-send guard covered both Gemini client paths, admitted calls against the exact serialized
request body, allowed only the pinned HTTPS generation endpoint with redirects disabled, and enforced
one global ceiling of 64 calls and USD 2.00. The API key was loaded in memory from user secrets and was
not written to the artifacts.

The offline preflight made no provider or public-network request. It validated the two previously
downloaded PDFs, their hashes, extracted page/span catalogs, UTF-16 offsets, unchanged source-size
limit, exact stage quotes, and the historical positive and negative citation selections. The preflight
artifact is retained locally as `PilotRunners/FullTextResumePilot/preflight-fulltext-citation-alignment-pilot-20260912.json`.

## Live results

| Work | Stage | HTTP | Paid elapsed | Calls | Tokens | Estimated cost | Retained output | Evidence links |
| --- | --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| Adam v1, 9 pages | Summary | 200 | 223.305 s | 19 | 121,284 | USD 0.313416000 | 32 of 40 candidates; 8 uncertain omitted | 50 |
| Adam v1, 9 pages | Four-role review | 200 | 88.366 s | 8 | 85,426 | USD 0.151840500 | 5 of 12 candidates; 7 uncertain omitted | 7 |
| Football, 13 pages | Summary | 200 | 64.277 s | 6 | 54,994 | USD 0.109003500 | 6 of 13 candidates; 7 uncertain omitted | 11 |
| Football, 13 pages | Four-role review | 200 | 101.484 s | 8 | 145,277 | USD 0.219999750 | 6 of 12 candidates; 6 uncertain omitted | 11 |

The three regression calls had an estimated cost of USD 0.018505500 and used 7,338 tokens. The complete
run made 44 calls, used 414,319 tokens, and had an estimated cost of USD 0.812765250 from recorded token
usage and pinned pricing. All 44 usages had a known estimate; one Adam summary call
ended at the output limit and the existing bounded fallback recovered it. The guard did not stop a
dispatch.

Both summaries are deliberately evidence-conservative partial outputs. Adam retained 32 claims and
omitted 8 uncertain candidates. Football retained 6 claims, omitted 7 uncertain candidates, and left
its Data and Findings sections empty. Both review reports completed all four roles, but retained only 5
of 12 Adam findings and 6 of 12 Football findings. These omissions are evidence that the verifier failed
closed on incomplete or ambiguous support, not evidence that the summaries or reviews are comprehensive.

## Citation and terminology checks

The regenerated Adam purpose claim about combining AdaGrad and RMSProp cites both page-one spans:
`src-1-3061-5f51631768f3e9eb` and `src-1-3537-ca26c25a53207ed4`. The second span explicitly contains
RMSProp. The historical negative used the original incomplete citations and returned `uncertain`; the
positive with both supporting spans returned `supported`. Putting the missing span under another claim
in the same batch did not make the negative claim pass.

The Football output describes off-screen players with `ekran dışında` and the visible area as `kadraj`.
It does not repeat the earlier misleading `saha dışı oyuncu` translation. This is a coordinating AI
spot-check, not a human translation or domain-expert review.

## SQL, cache, and recovery evidence

The coordinating AI audit compared native SQL pages, saved HTTP reports, and raw SQL `ReportJson`.
All 79 claim/finding evidence links matched the exact page and .NET UTF-16 source slice. All four saved
HTTP reports matched their SQL reports after property-case and omitted-null normalization. The 16 review
checkpoints were `Completed`; their distinct attempt IDs and actual per-attempt costs matched the usage
ledger. Both model fields were `gemini-3.8-flash`.

Saved summary, evidence, and review reads passed. A repeated review and the final recovery created zero
provider calls: the guard remained at 44 calls and USD 0.812765250. The recovery validated already
completed saved records; it did not exercise restarting an unfinished stage. The runner stopped its owned
hosts and dropped the exact isolated database.

The first completed harness snapshot reported a false operational failure because the Football URL used
percent-encoded `%20` in one representation and equivalent spaces in another. It preserved the database
and original paid responses. The URI comparison was corrected and tested, then the completed records were
reconciled without a paid rerun. The final `result.json` therefore contains fast cached review response
times; the paid timings in the table come from the immutable `phases/019-final.json` snapshot.

## Artifacts and boundary

The compact evidence set is:

- `result.json`, the reconciled final state;
- `phases/019-final.json`, original paid responses and timings plus the URI comparison false negative;
- `audit-summary.json`, compact acceptance and coordinating-audit totals;
- `reconciliation.json`, the offline cleanup correction.

These files remain under the local `docs/fulltext-citation-alignment-pilot-20260912/` evidence directory and are not published with the source tree.

`reconciliation.json` names `phases/032-final.json`, which remains only in the frozen QA checkout. It was
intentionally omitted from the compact repository evidence set because `result.json` and the reconciliation
record retain the final zero-call cleanup facts. The original paid `phases/019-final.json` remains included
and unchanged.

The runner is pinned to `fulltext-citation-alignment-pilot-20260912` and refuses existing artifacts.
Its documented commands describe this frozen run. Any new paid pilot needs a distinct reviewed run ID and
release value.

The run establishes bounded citation-isolation behavior for one known regression and operational success
for two public PDFs. It does not establish scientific correctness, omission recall, complete extraction of
figures or table layout, cross-discipline reliability, or production-scale cost. No human expert review was
performed.
