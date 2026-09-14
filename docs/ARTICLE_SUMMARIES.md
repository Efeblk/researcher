# Article summaries

Manual specialist review of an existing successful canonical analysis is documented separately in [Specialist article reviews](ARTICLE_REVIEWS.md). It does not add source fetching or automatic model calls to this summary workflow.

Start the collector and the independent analysis service. Researcher reports continue to use the independent `Ai:Provider` and `Ai:Model` settings. Article summaries use `Ai:ArticleProvider` (`Gemini` by default), `Ai:ArticleModel` (`gemini-3.8-flash`), a conservative 131,072-token application budget, and 8,192-token summary and verifier output reservations. Configure the hosted credential outside source control:

```powershell
dotnet user-secrets set "Gemini:ApiKey" "YOUR_KEY" --project ResearcherAnalysisService
dotnet user-secrets set "ConnectionStrings:UsageDatabase" "Server=(localdb)\MSSQLLocalDB;Database=AcademicCollectorDemo;Integrated Security=true;TrustServerCertificate=true" --project ResearcherAnalysisService
```

`ConnectionStrings:UsageDatabase` has no default or fallback. Point it explicitly at the same SQL
Server database that the collector migrates. Start the collector first so migration `202609110001`
creates `GeminiUsageAttempts`, then start or restart `ResearcherAnalysisService`. A Gemini generation
request is not sent when its Pending usage-attempt row cannot be inserted.

Gemini requests use structured JSON output, temperature zero, and high thinking for both summary generation and claim verification. The API key is sent in the `x-goog-api-key` header and is never included in the request URL. To use local Ollama explicitly, set `Ai:ArticleProvider` to `Ollama`, set `Ai:ArticleModel` (and optionally `Ai:ArticleVerifierModel`) to installed local models, and run Ollama 0.33.3 or newer. This does not change the provider used for researcher reports.

Each `generateContent` HTTP attempt is recorded in SQL before it is sent and completed afterward,
including rejected, timed-out, cancelled, network-failed, malformed, and successful responses. The
ledger stores timestamps, model identifiers, outcome, HTTP status, token counts, pricing version,
and the estimate only. It never stores prompts, source content, keys, request URLs, researcher IDs,
or DOIs. A completion-write failure intentionally leaves the durable row Pending so its cost remains
unknown. The metadata-only Gemini provider-status request and Ollama calls are not usage attempts.

Call `SummarizeArticle` with `PersonelID`, `AcademicWorkId`, and optional language (`tr` by default). The work must belong to that researcher. The collector never accepts a fetch URL from the request. `AcademicWork.Link` is the canonical article landing page and `FullTextUrl` is the preferred PDF candidate. Additional provider and previously stored URLs are retained with their origin in `core.AcademicWorkSources`. Candidates from the selected work and same-researcher works with the exact normalized DOI are tried with PDF candidates first. A successful final PDF URL is saved back as the preferred candidate. The collector downloads only public HTTP(S) content and extracts layout-aware page text. The SHA-256 value stored as `SourceHash` and `ExtractedTextHash` identifies the serialized extracted pages; it is not a hash of downloaded PDF or HTML bytes.

The workflow first recovers abstracts already present in the selected or exact-DOI provider payloads. It then tries saved PDF and landing-page candidates. HTML is accepted as `sourceKind: html` only when a semantic article body passes the extraction checks; its actual final URL is saved as an HTML source and is not written to `FullTextUrl` or labeled `ResolvedPdf`. PDF pages without extractable text can use bounded Tesseract OCR and report `pdf_ocr`; native PDFs report `pdf_text`. Unread or OCR-limited pages make the snapshot partial and remain visible in its scope reason.

If saved sources fail, a DOI metadata lookup queries the configured OpenAlex, Crossref, and Unpaywall integrations once through their cache and tries newly discovered unique candidates within the remaining source budget. A semantic HTML abstract can still be retained when the page is not credible full text. When no full text can be used, the recovered abstract is summarized with `sourceKind: abstract`, partial coverage, and an explicit reason. Metadata without usable article text returns an explicit unavailable result and never creates findings. Login/menu pages are not treated as article text. Source acquisition uses only part of the total timeout so an already recovered abstract and the model request retain time. Limits are configured under `ArticleSummary` and `ArticleMetadataEnrichment`.

Landing-page discovery recognizes citation PDF metadata, PDF link elements, `.pdf` anchors including query strings, and DergiPark article-file download links. `MaximumSourceRequests` is one shared HTTP request budget across all saved and enriched candidates, redirects, and landing-page PDF hops for a summary attempt; metadata provider requests have their own limits. Redirect depth, landing-page depth, response bytes, per-address connection time, and total fetch time are also bounded. Every redirect and discovered URL is revalidated, DNS connections are pinned only to public addresses, and failed candidates do not prevent another candidate from being tried while budget remains. Diagnostics identify the candidate origin and a bounded cause without exposing saved URLs, query strings, or raw transport exception details.

The analysis service first sends the complete extracted source. It reserves `Ai:ArticleMaxOutputTokens` inside `Ai:ArticleContextTokens` and conservatively checks the complete UTF-8 request size before sending. Gemini `MAX_TOKENS`, incomplete-output responses, and provider context overflows retry with immutable source-span chunks; a fallback chunk is subdivided again only between spans. Verification batches use the same adaptive behavior. A verifier singleton that still reaches an output/context limit is omitted as budget-unverified, never accepted. It remains an `uncertain` candidate for omission accounting but is excluded from `AutomaticallyCheckedClaims`. `Ai:ArticleFallbackChunkBytes` bounds chunks by UTF-8 bytes. No source character is silently removed. Safety blocks, malformed JSON, and unexpected finish reasons fail closed rather than being accepted.

New summary reports include deterministic `sourceFidelity` metadata derived from source kind and extraction version. It distinguishes supplied-page text coverage from formula/table layout, which is not verified after text flattening, and figure imagery, which is not analyzed. Generation and verification prompts must not infer numeric cell or symbol relationships from ambiguous flattened text and must not label extraction loss as a paper error. This is a text-only reliability guard, not visual scientific-document parsing. Existing stored reports remain readable with the field absent and do not acquire invented fidelity claims.

Gemini's response schema does not enumerate every source ID because large source-ID enums are rejected by Gemini before generation. The IDs remain in the model input, and the Gemini adapter rejects every returned source ID that is not an exact member of the current source-span set. Ollama continues to receive the enumerated source-ID schema.

Each source span has a deterministic ID derived from its canonical page, offsets, and exact text. The summary model returns claims and source IDs only. The service rejects unknown IDs or IDs outside the current chunk, then resolves quotes, page numbers, and offsets from the stored source itself. The model cannot alter quoted evidence.

A separate automatic model pass checks every candidate claim against only its cited spans. Batched input nests each claim with its own cite-only evidence, so an uncited neighbor or another claim's citation is not attributed to that claim. It checks quantities, named methods and actors, conditions, comparisons, negation, causality, and whether language is an author assertion or an established result. Missing, duplicate, or unknown verdicts fail closed. Unsupported and uncertain claims are omitted and counted with reasons. Verification batches split automatically on context or output limits; a singleton that still exhausts its budget is conservatively omitted as budget-unverified rather than accepted. If no supported claims remain, the report status is `insufficient_evidence` and the collector does not persist it as a successful summary. `Ai:ArticleVerifierModel` may select another model compatible with the chosen article provider; when unset it uses `Ai:ArticleModel`. This input structure removes ambiguous citation attribution but cannot guarantee model compliance or correctness, and using the same model family can produce correlated errors.

A bounded live regression confirmed the observed Adam claim is `uncertain` with the original incomplete citations and `supported` when its complete supporting spans are cited, both standalone and in a cross-item batch. See [Full-text citation-alignment pilot — 12 September 2026](FULLTEXT_CITATION_ALIGNMENT_PILOT_20260912.md). This result covers the observed mismatch only and is not a general semantic-accuracy claim.

Every successful `SummarizeArticle` save writes a normalized evidence graph in the same transaction as the existing exact `SnapshotJson` and `ReportJson`. `analysis.ArticleSourceSnapshots` is scoped to the canonical work and identified by extracted-text hash, source kind, and extraction version. Ordered page and span rows preserve the exact extracted text. Analysis runs preserve language, analysis-policy, model and prompt versions, coverage, verifier metadata, acquisition origin/time, and the internal audit URL. Purpose, methods, data, findings, and limitations claims link to their exact deterministic source spans. The collector accepts new reports only when every cited source ID, page, offset, and quote exactly matches the immutable request catalog.

`GetCanonicalArticleEvidence` accepts `PersonelID`, `CanonicalWorkId`, language, and bounded claim paging. It returns the latest successful run for that language only while the requested researcher has a current association with the canonical publication. It exposes extracted-text provenance, coverage, verifier metadata, and exact cited spans, but omits the acquisition URL and any other researcher's identifiers or provider payload. Repeated reads never contact the analysis service. Existing legacy summary JSON is retained and is not assigned invented source offsets.

```json
{
  "personelID": "P-1001",
  "canonicalWorkId": 42,
  "language": "tr",
  "source": {
    "extractedTextHash": "...",
    "sourceKind": "pdf",
    "extractionVersion": "pdfpig-layout-spans-v2",
    "pageCount": 12,
    "spanCount": 86
  },
  "claims": [{
    "section": "Findings",
    "text": "...",
    "evidence": [{ "sourceId": "src-7-120-...", "pageNumber": 7, "startOffset": 120, "endOffset": 241, "quote": "..." }]
  }]
}
```

Successful individual and bulk collection enqueue one durable automatic job per canonical work and configured language. `ArticleSummaryAutomation.Enabled` and `WorkerEnabled` default to `true`; turning `Enabled` off stops enqueue and claim while retaining pending jobs and allowing an in-flight attempt to finish. Manual `SummarizeArticle` remains available while automation is off and always regenerates. `Language`, `PollSeconds`, `RetrySeconds`, `MaximumAttempts`, and `PolicyVersion` are reloadable configuration. Ordinary test hosts disable both switches explicitly.

```json
"ArticleSummaryAutomation": {
  "Enabled": true,
  "WorkerEnabled": true,
  "Language": "tr",
  "PollSeconds": 5,
  "RetrySeconds": 60,
  "MaximumAttempts": 3,
  "PolicyVersion": "article-summary-v3"
}
```

`WorkerEnabled=false` pauses consumption without preventing collection from enqueueing new work. Increase `PolicyVersion` whenever model selection, prompts, extraction rules, or acquisition policy changes in a way that should invalidate automatic reuse.

`analysis.ArticleSummaryAutomationJobs` stores desired, running, and processed metadata fingerprints separately. The fingerprint contains the deterministic bounded source-candidate list and recovered abstract, excluding timestamps, observation IDs, counts, and researcher membership. A changed source during a running attempt creates another pending generation. A generation token fences evidence persistence and queue completion, and both occur in one final transaction. A global SQL session lock permits one automatic article worker across hosts; canonical/language locks coordinate it with manual requests. Source acquisition and model calls occur outside SQL transactions and the canonical write gate.

An abandoned same-input `Running` attempt and an attempt cancelled during analysis finish as `Failed` with outcome `Interrupted`. HTTP, transport, invalid-report, post-analysis source-change, and unexpected processing failures may have incurred remote cost, so automatic processing does not retry the same job after that unknown result. When the desired input was independently synchronized during the attempt, it remains eligible as a new generation; explicit pre-analysis input-change detection requeues the current fingerprint without a model call. Pre-analysis source acquisition failures and local lock contention retain their bounded retry behavior. An operator can explicitly recover a terminal job with `SummarizeArticle`; a successful manual summary coalesces the failed automatic job without a second automatic model call.

Automatic execution reuses a successful run only when canonical work, language, policy version, extracted-content hash, source kind, and extraction version all match. Legacy runs with no policy version are never reused. Collection with unchanged metadata does not re-fetch a remote URL merely to discover that its bytes changed; a later collection that changes source metadata, or an explicit manual regeneration, provides that refresh boundary. Persisting a discovered abstract or resolved source coalesces the post-write fingerprint so it does not schedule itself repeatedly.

`GetArticleSummaryAutomationStatus` accepts `PersonelID`, `CanonicalWorkId`, and language. It requires a current canonical association and returns safe queue state, enabled switches, attempts/times, bounded outcome, and last-success metadata. It omits provider URLs, exception text, payloads, and other personnel IDs. `GetArticleSummary` resolves the latest successful canonical run for a currently associated duplicate work, remaps the response to the requested work/person, and suppresses another observation owner's source URL. Historical own-work reads remain available after the provider row is deleted. See `Requests/ArticleSummary.http` for all four article calls.
